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

    [Header("Feed the camera texture into ColorImage. \nConfigure the Camera component to use the physical Camera property. \nMatch the sensor size with the camera resolution and configure the FoV/FocalLength."), Space]
    [SerializeField] private Texture2D ColorImage;
    private Texture2D textureSource;
    private byte[] colorImageData;
    private static readonly object dataLock = new object();

    [Header("Calibration")]
    [SerializeField] private Matrix4x4 kinectToImageTargetMatrix;
    [SerializeField] private Matrix4x4 hololensToImageTargetMatrix;
    [SerializeField] private Matrix4x4 hololensToKinectMatrix;

    [Header("AR Camera Settings")]
    [SerializeField] private GameObject arCamera; // Reference to the Vuforia AR Camera
    [SerializeField] private GameObject imageTarget; // Reference to the Image Target
    [SerializeField] private GameObject cube;
    private ObserverBehaviour targetObserver;

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

        targetObserver = imageTarget.GetComponent<ObserverBehaviour>();
        if (targetObserver == null)
        {
            Debug.LogError("Image Target does not have ObserverBehaviour attached.");
        }
    }

    private void Start()
    {
        try
        {
            subscriber = new Subscriber(host, port);
            subscriber.AddTopicCallback("Calibration", data => OnCalibrationReceived(data));
            subscriber.AddTopicCallback("Size", data => OnColorSizeReceived(data));
            subscriber.AddTopicCallback("Frame", data => OnColorFrameReceived(data));
            Debug.Log("Subscriber setup complete with host: " + host + " and port: " + port);
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to start subscriber: " + e.Message);
        }
    }

    private void OnCalibrationReceived(byte[] data)
    {
        kinectToImageTargetMatrix = ByteArrayToMatrix4x4(data);
    }

    private void OnColorSizeReceived(byte[] data)
    {
        if (data.Length != 2 * sizeof(int))
        {
            Debug.LogError($"PBM_CaptureCamera::OnColorSizeReceived(): Data length is not right");
            return;
        }

        int[] sizeArray = new int[2];
        Buffer.BlockCopy(data, 0, sizeArray, 0, data.Length);
        int width = sizeArray[0];
        int height = sizeArray[1];

        Debug.Log($"Recieve width is {width}, height is {height}");
        Debug.Log($"_Camera width is {Width}, height is {Height}");

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

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (targetObserver != null && targetObserver.TargetStatus.Status == Status.TRACKED)
        {
            Debug.Log("Target is tracked");
            hololensToImageTargetMatrix = GetHololensToImageTargetMatrix(targetObserver);

            // 计算 Extrinsics
            hololensToKinectMatrix = CalculateExtrinsics();
        }

        if (colorImageData != null && hasReceivedFirstFrame)
        {
            lock (dataLock)
            {
                ColorImage.LoadRawTextureData(colorImageData);
                ColorImage.Apply();
            }

            if (cube != null)
            {
                Vector3 cubeWorldPosition = cube.transform.position;
                Debug.Log($"Cube world position: {cubeWorldPosition}");

                // 转换到 Kinect 坐标系
                Vector3 kinectPosition = hololensToKinectMatrix.MultiplyPoint(cubeWorldPosition);

                // 使用 Intrinsics 计算像素坐标
                Matrix4x4 intrinsics = CalculateIntrinsics();
                Vector2Int pixelPosition = GetProjectedPixelCoordinates(kinectPosition, intrinsics, ColorImage.width, ColorImage.height);

                if (pixelPosition.x >= 0 && pixelPosition.x < ColorImage.width &&
                    pixelPosition.y >= 0 && pixelPosition.y < ColorImage.height)
                {
                    int rectSize = 10;
                    Debug.Log($"Drawing rectangle at ({pixelPosition.x}, {pixelPosition.y}) on ColorImage");
                    DrawRectangleOnTexture(ColorImage, pixelPosition.x, pixelPosition.y, rectSize, Color.red);
                    ColorImage.Apply();
                }
                else
                {
                    Debug.LogWarning($"Pixel position out of range: {pixelPosition.x}, {pixelPosition.y}");
                }
            }

            RealVirtualMergeMaterial.mainTexture = source;
            RealVirtualMergeMaterial.SetTexture("_RealContentTex", ColorImage);
            Graphics.Blit(source, ViewRenderTexture, RealVirtualMergeMaterial);
        }
    }


    // Helper function to draw a rectangle on a Texture2D
    private void DrawRectangleOnTexture(Texture2D texture, int x, int y, int size, Color color)
    {
        for (int i = -size; i <= size; i++)
        {
            for (int j = -size; j <= size; j++)
            {
                int drawX = Mathf.Clamp(x + i, 0, texture.width - 1);
                int drawY = Mathf.Clamp(y + j, 0, texture.height - 1);
                texture.SetPixel(drawX, drawY, color);
            }
        }
    }

    private Matrix4x4 GetHololensToImageTargetMatrix(ObserverBehaviour targetObserver)
    {
        // Retrieve the transformation matrix from AR Camera to Image Target
        Transform arTransform = arCamera.transform;
        Transform targetTransform = targetObserver.transform;
        Transform cameraTrasform = _Camera.transform;

        // Calculate the relative matrix (Kinect's position and rotation relative to Image Target)
        return Matrix4x4.TRS(
            targetTransform.position - arTransform.position,
            Quaternion.Inverse(arTransform.rotation) * targetTransform.rotation,
            Vector3.one
        );
    }

    private Vector2Int GetProjectedPixelCoordinates(Vector3 worldPosition, Matrix4x4 intrinsics, int imageWidth, int imageHeight)
    {
        float uvX = (worldPosition.x / worldPosition.z) * intrinsics[0, 0] + intrinsics[0, 2];
        float uvY = (worldPosition.y / worldPosition.z) * intrinsics[1, 1] + intrinsics[1, 2];

        int pixelX = Mathf.RoundToInt(uvX * imageWidth);
        int pixelY = Mathf.RoundToInt(uvY * imageHeight);

        return new Vector2Int(pixelX, pixelY);
    }


    // Extrinsics 计算方法
    private Matrix4x4 CalculateExtrinsics()
    {
        // 通过 client 发送的 kinectToMarkerMatrix 和 HoloLens 的 holoLensToMarkerMatrix 计算 Extrinsics
        if (kinectToImageTargetMatrix == null || hololensToImageTargetMatrix == null)
        {
            Debug.LogError("Missing kinectToMarkerMatrix or hololensToImageTargetMatrix for Extrinsics calculation.");
            return Matrix4x4.identity;
        }

        // Extrinsics = holoLensToKinectMatrix
        Matrix4x4 extrinsics = hololensToImageTargetMatrix * kinectToImageTargetMatrix.inverse;
        Debug.Log($"Calculated Extrinsics: {extrinsics}");
        return extrinsics;
    }

    // Intrinsics 计算方法
    private Matrix4x4 CalculateIntrinsics()
    {
        // 根据 _Camera 和 arCamera 的参数计算 Intrinsics
        float fx = _Camera.focalLength / _Camera.sensorSize.x * Width;
        float fy = _Camera.focalLength / _Camera.sensorSize.y * Height;

        // 从 arCamera 中获取主点 (cx, cy)
        float cx = Width / 2f; // 默认为图像中心
        float cy = Height / 2f; // 默认为图像中心

        if (arCamera != null)
        {
            Camera arCameraComponent = arCamera.GetComponent<Camera>();
            if (arCameraComponent != null)
            {
                cx = arCameraComponent.pixelWidth / 2f;
                cy = arCameraComponent.pixelHeight / 2f;
            }
        }

        // 构造 Intrinsics 矩阵
        Matrix4x4 intrinsics = Matrix4x4.zero;
        intrinsics[0, 0] = fx;
        intrinsics[1, 1] = fy;
        intrinsics[0, 2] = cx;
        intrinsics[1, 2] = cy;
        intrinsics[2, 2] = 1f;

        Debug.Log($"Calculated Intrinsics: fx={fx}, fy={fy}, cx={cx}, cy={cy}");
        return intrinsics;
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
