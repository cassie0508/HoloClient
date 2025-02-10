using PubSub;
using System;
using System.Text.RegularExpressions;
using UnityEngine;
using Vuforia;

[RequireComponent(typeof(Camera))]
public class PBM_CaptureCamera : MonoBehaviour
{
    [SerializeField] private string host;
    [SerializeField] private string port = "55555";
    private Subscriber subscriber;

    [Header("Feed the camera texture into ColorImage.")]
    [SerializeField] private Texture2D ColorImage;
    private Texture2D textureSource;
    private byte[] colorImageData;
    private static readonly object dataLock = new object();

    [Header("Resulting View (leave empty)")]
    public RenderTexture ViewRenderTexture;
    private Camera _Camera;
    private Material RealVirtualMergeMaterial;

    #region Image Variables
    // The offset of two pixel increases stability during compensation
    public int Width
    {
        get
        {
            return _Width - 2;
        }
        private set
        {
            _Width = value;
        }
    }
    private int _Width;
    public int Height
    {
        get
        {
            return _Height - 2;
        }
        private set
        {
            _Height = value;
        }
    }
    private int _Height;
    public float FocalLength
    {
        get
        {
            return _Camera.focalLength * CompensationRatio;
        }
    }

    public float Ratio
    {
        get
        {
            return CompensationRatio;
        }
    }
    private float CompensationRatio = 1;
    #endregion

    private bool isProcessingFrame = false;
    private bool hasReceivedFirstFrame = false;

    private void Awake()
    {
        _Camera = GetComponent<Camera>();
        _Camera.stereoTargetEye = StereoTargetEyeMask.None; // Set stereo target to none
        _Camera.cullingMask &= ~(1 << LayerMask.NameToLayer("PBM"));
        _Camera.usePhysicalProperties = true;
        Width = (int)_Camera.sensorSize.x;
        Height = (int)_Camera.sensorSize.y;
        _Camera.aspect = 1.0f * _Width / _Height;

        RealVirtualMergeMaterial = new Material(Shader.Find("PBM/ViewMerge"));

        ViewRenderTexture = new RenderTexture(_Width, _Height, 24);
        ViewRenderTexture.name = "PBMView";
        ViewRenderTexture.Create();
    }

    private void Start()
    {
        try
        {
            subscriber = new Subscriber(host, port);
            subscriber.AddTopicCallback("Size", data => OnColorSizeReceived(data));
            subscriber.AddTopicCallback("Frame", data => OnColorFrameReceived(data));
            Debug.Log("Subscriber setup complete with host: " + host + " and port: " + port);
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to start subscriber: " + e.Message);
        }
    }

    private void OnColorSizeReceived(byte[] data)
    {
        Debug.Log($"OnColorSizeReceived data length {data.Length}");
        if (data.Length != 2 * sizeof(int))
        {
            Debug.LogError($"PBM_CaptureCamera::OnColorSizeReceived(): Data length is not right");
            return;
        }

        int[] sizeArray = new int[2];
        Buffer.BlockCopy(data, 0, sizeArray, 0, data.Length);
        int width = sizeArray[0];
        int height = sizeArray[1];
        Debug.Log($"Receive size with width {width} and height {height}");

        UnityMainThreadDispatcher.Dispatcher.Enqueue(() =>
        {
            if (ColorImage == null)
            {
                ColorImage = new Texture2D(width, height, TextureFormat.BGRA32, false);
                Debug.Log($"PBM_CaptureCamera::OnColorSizeReceived(): Initialized new ColorImage with width: {width}, height: {height}");
            }
        });
    }

    private void OnColorFrameReceived(byte[] msg)
    {
        Debug.Log($"OnColorFrameReceived data length {msg.Length}");
        UnityMainThreadDispatcher.Dispatcher.Enqueue(() =>
        {
            if (ColorImage == null) return;
        });

        if (isProcessingFrame) return;
        isProcessingFrame = true;

        long timestamp = BitConverter.ToInt64(msg, 0);
        byte[] data = new byte[msg.Length - sizeof(long)];
        Buffer.BlockCopy(msg, sizeof(long), data, 0, data.Length);

        long receivedTimestamp = DateTime.UtcNow.Ticks;
        double delayMilliseconds = (receivedTimestamp - timestamp) / TimeSpan.TicksPerMillisecond;
        //Debug.Log($"Delay for this processing frame: {delayMilliseconds} ms");

        lock (dataLock)
        {
            colorImageData = data;
        }
            
        isProcessingFrame = false;
        hasReceivedFirstFrame = true;
    }

    public void UpdateValidAreaCompensationWithObserver(Vector3 ObserverWorldPos)
    {
        CompensationRatio = GetCompensationRatio(ObserverWorldPos);
    }

    private void LateUpdate()
    {
        _Camera.Render();
    }



    // https://stackoverflow.com/questions/44264468/convert-rendertexture-to-texture2d
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (ColorImage != null && colorImageData != null && hasReceivedFirstFrame)
        {
            lock (dataLock)
            {
                ColorImage.LoadRawTextureData(colorImageData);
                ColorImage.Apply();
            }

            if (textureSource == null)
            {
                textureSource = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
            }
            RenderTexture.active = source;
            textureSource.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            textureSource.Apply();

            RealVirtualMergeMaterial.mainTexture = source;
            RealVirtualMergeMaterial.SetTexture("_RealContentTex", ColorImage);

            Graphics.Blit(source, ViewRenderTexture, RealVirtualMergeMaterial);
        }
    }


    private bool IsValidObserverPosition(Vector3 worldPos)
    {
        var vfov = _Camera.fieldOfView * Mathf.Deg2Rad;
        var hfov = 2 * Mathf.Atan( Mathf.Tan(vfov / 2) * _Camera.aspect);

        var a = Mathf.Tan(hfov / 2);
        var b = Mathf.Tan(vfov / 2);

        var pointInCameraCoord = _Camera.transform.InverseTransformPoint(worldPos);
        pointInCameraCoord.z = 0;

        var angle = Mathf.Min(
            Mathf.Abs(Vector3.Angle(Vector3.right, pointInCameraCoord)),
            Mathf.Abs(Vector3.Angle(Vector3.left, pointInCameraCoord)));
   
        var phi = angle * Mathf.Deg2Rad;

        var gamma = 2 * Mathf.Atan(Mathf.Cos(phi) * a + Mathf.Sin(phi) * b);

        var theta_critical = 180 - gamma * Mathf.Rad2Deg;

        var angleObjectToForward = Vector3.Angle(
            _Camera.transform.InverseTransformPoint(worldPos),
            Vector3.forward);

        return angleObjectToForward < theta_critical / 2;
    }

    private Matrix4x4 ByteArrayToMatrix4x4(byte[] byteArray)
    {
        float[] matrixFloats = new float[16];
        Buffer.BlockCopy(byteArray, 0, matrixFloats, 0, byteArray.Length);

        Matrix4x4 matrix = new Matrix4x4();
        for (int i = 0; i < 16; i++)
        {
            matrix[i] = matrixFloats[i];
        }

        return matrix;
    }

    private float GetCompensationRatio(Vector3 ObserverWorldpos)
    {
        float ratio = 1;
        if (!IsValidObserverPosition(ObserverWorldpos))
        {
            var pointInCamera = _Camera.transform.InverseTransformPoint(ObserverWorldpos);
            pointInCamera.z = 0;

            var angle = Mathf.Min(
                    Mathf.Abs(Vector3.Angle(Vector3.right, pointInCamera)),
                    Mathf.Abs(Vector3.Angle(Vector3.left, pointInCamera)));

            var phi = angle * Mathf.Deg2Rad;

            var fov_xy = GetCameraFovForValidPBM(ObserverWorldpos).y;
            var radVFOV0 = fov_xy * Mathf.Deg2Rad;

            var f_Y = 
                Mathf.Sin(phi) * (_Height / (2 * Mathf.Tan(radVFOV0 / 2))) +
                Mathf.Cos(phi) * (_Width / (2 * Mathf.Tan(radVFOV0 / 2)));

            ratio = f_Y / _Camera.focalLength;

        }
        return ratio;
    }

    private Vector2 GetCameraFovForValidPBM(Vector3 ObserverWorldpos)
    {
        var pointInCamera = _Camera.transform.InverseTransformPoint(ObserverWorldpos);

        var theta_critical = Vector3.Angle(pointInCamera, Vector3.forward) * 2 * Mathf.Deg2Rad;

        var fov = (Mathf.PI - theta_critical) * Mathf.Rad2Deg;

        return new Vector2(fov, fov);
    }

}
