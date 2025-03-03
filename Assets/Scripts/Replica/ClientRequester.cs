using UnityEngine;
using TMPro; // Import the TextMeshPro namespace
using System;
using NetMQ;
using UnityMainThreadDispatcher;
using UnityEngine.UI;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;   // For List<>
using PubSub;
using NetMQ.Sockets;
using System.Collections;
using UnityEngine.Timeline;
using Vuforia;
using UnityEngine.SpatialTracking;
using UnityEngine.InputSystem;
using static PBM_Observer;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit;

namespace Kinect4Azure
{
    public class ClientRequester : MonoBehaviour
    {
        [Header("Duplicated Reality")]
        public Transform RegionOfInterest;
        public Transform DuplicatedReality;

        [Header("Networking")]
        [SerializeField] private string host;
        [SerializeField] private string port = "12345";
        private RequestSocket requestSocket;

        [Serializable]
        public struct PointcloudShader
        {
            public string ID;
            public string ShaderName;
        }
        [Header("Pointcloud Configs")]
        public ComputeShader Depth2BufferShader;
        public List<PointcloudShader> Shaders;
        private int _CurrentSelectedShader = 0;
        private Material _Buffer2SurfaceMaterial;

        [Range(0.01f, 0.1f)]
        public float MaxPointDistance = 0.02f;

        [Header("Background Configs\n(Only works if this script is attached onto the camera)")]
        public bool EnableARBackground = true;
        [Tooltip("Only needs to be set when BlitToCamera is checked")]
        public Material ARBackgroundMaterial;

        [Header("ReadOnly and exposed for Debugging: Initial Message")]
        [SerializeField] private int ColorWidth;
        [SerializeField] private int ColorHeight;
        [SerializeField] private int DepthWidth;
        [SerializeField] private int DepthHeight;
        [SerializeField] private int IRWidth;
        [SerializeField] private int IRHeight;
        [SerializeField] private Texture2D XYLookup;
        [SerializeField] private Matrix4x4 Color2DepthCalibration;
        [SerializeField] private int kernel;
        [SerializeField] private int dispatch_x;
        [SerializeField] private int dispatch_y;
        [SerializeField] private int dispatch_z;

        [Header("ReadOnly and exposed for Debugging: Update for every Frame")]
        [SerializeField] private Texture2D DepthImage;
        [SerializeField] private Texture2D ColorInDepthImage;

        [Header("Calibration")]
        [SerializeField] private GameObject marker1;
        [SerializeField] private GameObject marker2;

        private ObserverBehaviour marker1Observer;
        private ObserverBehaviour marker2Observer;

        [Header("Input Actions")]
        public InputAction CalibrationSpineAction;  // Xbox A Button
        public InputAction CalibrationKinectAction; // Xbox B Button
        public InputAction ShowShaderAction; // Xbox Y Button
        public InputAction SwitchToNextShaderAction; // Xbox D-Pad Right Button

        [Header("UI Elements")]
        public TextMeshProUGUI CalibrationText;
        public TextMeshProUGUI ShaderText;


        private bool spineCalibrated = false;
        private bool kinectCalibrated = false;
        private bool trackingSpine = false;
        private bool trackingKinect = false;
        private bool isShaderTextVisible = false;


        private byte[] cameraData;
        private byte[] xyLookupDataPart1;
        private byte[] xyLookupDataPart2;
        private byte[] xyLookupDataPart3;

        private byte[] depthData;
        private byte[] colorInDepthData;
        private static readonly object dataLock = new object();

        private bool hasReceivedCamera = false;
        private bool hasReceivedLookup1 = false;
        private bool hasReceivedLookup2 = false;
        private bool hasReceivedLookup3 = false;

        // Buffers for PointCloud Compute Shader
        private Vector3[] vertexBuffer;
        private Vector2[] uvBuffer;
        private int[] indexBuffer;
        private ComputeBuffer _ib;
        private ComputeBuffer _ub;
        private ComputeBuffer _vb;

        private IMixedRealityGazeProvider gazeProvider;
        private VuforiaBehaviour vuforia;


        private void Awake()
        {
            marker1Observer = marker1.GetComponent<ObserverBehaviour>();
            marker2Observer = marker2.GetComponent<ObserverBehaviour>();

            UpdateStatusText("Press A to calibrate spine");

            if (CoreServices.InputSystem != null)
            {
                gazeProvider = CoreServices.InputSystem.GazeProvider;
            }

            vuforia = FindObjectOfType<VuforiaBehaviour>(true);

            ShaderText.enabled = isShaderTextVisible;
        }

        private void OnEnable()
        {
            CalibrationSpineAction.Enable();
            CalibrationSpineAction.performed += OnCalibrateSpine;

            CalibrationKinectAction.Enable();
            CalibrationKinectAction.performed += OnCalibrateKinect;

            ShowShaderAction.Enable();
            ShowShaderAction.performed += OnShowShader;

            SwitchToNextShaderAction.Enable();
            SwitchToNextShaderAction.performed += OnSwitchToNextShader;
        }

        private void OnDisable()
        {
            CalibrationSpineAction.Disable();
            CalibrationSpineAction.performed -= OnCalibrateSpine;

            CalibrationKinectAction.Disable();
            CalibrationKinectAction.performed -= OnCalibrateKinect;

            ShowShaderAction.Disable();
            ShowShaderAction.performed -= OnShowShader;

            SwitchToNextShaderAction.Disable();
            SwitchToNextShaderAction.performed -= OnSwitchToNextShader;
        }

        [ContextMenu("Test Calibrate Spine")]
        public void TestCalibrateSpine()
        {
            OnCalibrateSpine(new InputAction.CallbackContext());
        }
        private void OnCalibrateSpine(InputAction.CallbackContext context)
        {
            if (!trackingSpine)
            {
                StartCoroutine(CalibrateSpine());
            }
            else
            {
                trackingSpine = false;
                UpdateStatusText("");
                Debug.Log("Spine calibration confirmed.");
            }
        }

        [ContextMenu("Test Calibrate Kinect")]
        public void TestCalibrateKinect()
        {
            OnCalibrateKinect(new InputAction.CallbackContext());
        }
        private void OnCalibrateKinect(InputAction.CallbackContext context)
        {
            if (!spineCalibrated)
            {
                UpdateStatusText("Calibrate spine first (Press A)");
                return;
            }

            if (!trackingKinect)
            {
                StartCoroutine(CalibrateKinect());
            }
            else
            {
                trackingKinect = false;
                Debug.Log("Kinect calibration confirmed.");

                if (requestSocket == null)
                {
                    InitializeSocket();
                    StartCoroutine(RequestDataLoop());
                }

                UpdateStatusText("");
            }
        }

        private void OnShowShader(InputAction.CallbackContext context)
        {
            isShaderTextVisible = !isShaderTextVisible;
            ShaderText.enabled = isShaderTextVisible;
        }

        private void OnSwitchToNextShader(InputAction.CallbackContext context)
        {
            NextShaderInList();
        }

        [ContextMenu("Test Pointcloud")]
        public void TestPointCloud()
        {
            InitializeSocket();
            StartCoroutine(RequestDataLoop());
        }

        private IEnumerator CalibrateSpine()
        {
            trackingSpine = true;
            EnableTracking(marker1Observer);
            UpdateStatusText("Tracking marker 1... Press A again to confirm.");

            while (trackingSpine)
            {
                if (marker1Observer.TargetStatus.Status == Status.TRACKED)  // Will set its children
                {
                    Debug.Log("Marker 1 tracked and updated.");
                }
                yield return null;
            }

            DisableTracking(marker1Observer);
            spineCalibrated = true;
        }

        private IEnumerator CalibrateKinect()
        {
            trackingKinect = true;
            EnableTracking(marker2Observer);
            UpdateStatusText("Tracking marker 2... Press B again to confirm.");

            while (trackingKinect)
            {
                if (marker2Observer.TargetStatus.Status == Status.TRACKED)  // Will set its children
                {
                    Debug.Log("Marker 2 tracked and updated.");
                }
                yield return null;
            }

            DisableTracking(marker2Observer);
            kinectCalibrated = true;
        }

        private void EnableTracking(ObserverBehaviour marker)
        {
            vuforia.enabled = true;
            marker.enabled = true;
            Debug.Log($"Tracking enabled for {marker.name}");
        }

        private void DisableTracking(ObserverBehaviour marker)
        {
            vuforia.enabled = false;
            marker.enabled = false;
            Debug.Log($"Tracking disabled for {marker.name}");
        }

        private void UpdateStatusText(string message)
        {
            if (CalibrationText != null)
            {
                CalibrationText.text = message;
            }
        }


        private void InitializeSocket()
        {
            try
            {
                AsyncIO.ForceDotNet.Force();
                requestSocket = new RequestSocket();
                requestSocket.Connect($"tcp://{host}:{port}");
                Debug.Log("Connected to server");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to connect socket: {ex.Message}");
            }
        }

        private IEnumerator RequestDataLoop()
        {

            while (!hasReceivedCamera)
            {
                RequestCameraData();
                yield return new WaitForSeconds(0.2f); // Retry every 200ms
            }

            while (!hasReceivedLookup1)
            {
                RequestLookupData(1);
                yield return new WaitForSeconds(0.2f);
            }

            while (!hasReceivedLookup2)
            {
                RequestLookupData(2);
                yield return new WaitForSeconds(0.2f);
            }

            while (!hasReceivedLookup3)
            {
                RequestLookupData(3);
                yield return new WaitForSeconds(0.2f);
            }

            Debug.Log("All required data received.");
            ProcessInitialData();
            StartCoroutine(RequestFrameDataLoop());
        }

        private void RequestCameraData()
        {
            requestSocket.SendFrame("Camera");
            if (requestSocket.TryReceiveFrameBytes(TimeSpan.FromSeconds(1), out var data))
            {
                Debug.Log($"Received Camera data: {data.Length} bytes");
                cameraData = data;
                hasReceivedCamera = true;
            }
            else
            {
                Debug.LogWarning("Camera data request timed out");
            }
        }

        private void RequestLookupData(int part)
        {
            requestSocket.SendFrame($"Lookup{part}");
            if (requestSocket.TryReceiveFrameBytes(TimeSpan.FromSeconds(1), out var data))
            {
                Debug.Log($"Received Lookup{part} data: {data.Length} bytes");
                if (part == 1)
                { xyLookupDataPart1 = data; hasReceivedLookup1 = true; }
                if (part == 2)
                { xyLookupDataPart2 = data; hasReceivedLookup2 = true; }
                if (part == 3)
                { xyLookupDataPart3 = data; hasReceivedLookup3 = true; }
            }
            else
            {
                Debug.LogWarning($"Lookup{part} data request timed out");
            }
        }

        private void ProcessInitialData()
        {
            /* Process camera data */
            try
            {
                int calibrationDataLength = BitConverter.ToInt32(cameraData, 0);
                int cameraSizeDataLength = BitConverter.ToInt32(cameraData, sizeof(int) * 1);

                byte[] calibrationData = new byte[calibrationDataLength];
                Buffer.BlockCopy(cameraData, sizeof(int) * 2, calibrationData, 0, calibrationDataLength);
                byte[] cameraSizeData = new byte[cameraSizeDataLength];
                Buffer.BlockCopy(cameraData, sizeof(int) * 2 + calibrationDataLength, cameraSizeData, 0, cameraSizeDataLength);

                int[] captureArray = new int[6];
                Buffer.BlockCopy(cameraSizeData, 0, captureArray, 0, cameraSizeData.Length);
                ColorWidth = captureArray[0];
                ColorHeight = captureArray[1];
                DepthWidth = captureArray[2];
                DepthHeight = captureArray[3];
                IRWidth = captureArray[4];
                IRHeight = captureArray[5];

                SetupTextures(ref DepthImage, ref ColorInDepthImage);

                Color2DepthCalibration = ByteArrayToMatrix4x4(calibrationData);
            }
            catch (Exception e)
            {
                Debug.LogError("Error in OnCameraReceived: " + e.Message);
            }

            /* Process lookup data */
            byte[] xyLookupData = new byte[xyLookupDataPart1.Length + xyLookupDataPart2.Length + xyLookupDataPart3.Length];
            System.Buffer.BlockCopy(xyLookupDataPart1, 0, xyLookupData, 0, xyLookupDataPart1.Length);
            System.Buffer.BlockCopy(xyLookupDataPart2, 0, xyLookupData, xyLookupDataPart1.Length, xyLookupDataPart2.Length);
            System.Buffer.BlockCopy(xyLookupDataPart3, 0, xyLookupData, xyLookupDataPart1.Length + xyLookupDataPart2.Length, xyLookupDataPart3.Length);

            XYLookup = new Texture2D(DepthImage.width, DepthImage.height, TextureFormat.RGBAFloat, false);
            XYLookup.LoadRawTextureData(xyLookupData);
            XYLookup.Apply();

            if (!SetupShaders(57 /*Standard Kinect Depth FoV*/, DepthImage.width, DepthImage.height, out kernel))
            {
                Debug.LogError("OnLookupsReceived(): Something went wrong while setting up shaders");
                return;
            }

            // Compute kernel group sizes. If it deviates from 32-32-1, this need to be adjusted inside Depth2Buffer.compute as well.
            Depth2BufferShader.GetKernelThreadGroupSizes(kernel, out var xc, out var yc, out var zc);

            dispatch_x = (DepthImage.width + (int)xc - 1) / (int)xc;
            dispatch_y = (DepthImage.height + (int)yc - 1) / (int)yc;
            dispatch_z = (1 + (int)zc - 1) / (int)zc;
            Debug.Log("OnLookupsReceived(): Kernel group sizes are " + xc + "-" + yc + "-" + zc);
        }

        private IEnumerator RequestFrameDataLoop()
        {
            while (true)
            {
                requestSocket.SendFrame("Frame");

                if (requestSocket.TryReceiveFrameBytes(TimeSpan.FromSeconds(1), out var msg))
                {
                    //Debug.Log($"Received Frame data: {msg.Length} bytes");

                    // Get delay
                    long timestamp = BitConverter.ToInt64(msg, 0);
                    byte[] data = new byte[msg.Length - sizeof(long)];
                    Buffer.BlockCopy(msg, sizeof(long), data, 0, data.Length);

                    long receivedTimestamp = DateTime.UtcNow.Ticks;
                    double delayMilliseconds = (receivedTimestamp - timestamp) / TimeSpan.TicksPerMillisecond;
                    //Debug.Log($"Delay for this processing frame: {delayMilliseconds} ms");
                    //UnityMainThreadDispatcher.Dispatcher.Enqueue(() =>
                    //{
                    //    DelayTMP.SetText($"Delay in requesting this frame: {delayMilliseconds}");
                    //});

                    // Get data
                    lock (dataLock)
                    {
                        int depthDataLength = BitConverter.ToInt32(data, 0);
                        int colorInDepthDataLength = BitConverter.ToInt32(data, sizeof(int));

                        depthData = new byte[depthDataLength];
                        Buffer.BlockCopy(data, sizeof(int) * 2, depthData, 0, depthDataLength);

                        colorInDepthData = new byte[colorInDepthDataLength];
                        Buffer.BlockCopy(data, sizeof(int) * 2 + depthDataLength, colorInDepthData, 0, colorInDepthDataLength);
                    }
                }
                else
                {
                    Debug.LogWarning("Frame data request timed out");
                }

                yield return null; // Frame requests as fast as possible
            }
        }


        private void Update()
        {
            //var rendererROI = RegionOfInterest.GetComponent<Renderer>();
            //if (rendererROI) rendererROI.enabled = true;

            //var rendererDup = DuplicatedReality.GetComponent<Renderer>();
            //if (rendererDup) rendererDup.enabled = true;

            if (depthData != null && colorInDepthData != null)
            {
                long timeBegin = DateTime.UtcNow.Ticks;
                
                lock (dataLock)
                {
                    DepthImage.LoadRawTextureData(depthData.ToArray());
                    DepthImage.Apply();

                    ColorInDepthImage.LoadRawTextureData(colorInDepthData.ToArray());
                    ColorInDepthImage.Apply();

                    //DebugImage.texture = ColorInDepthImage;
                }

                // Compute triangulation of PointCloud + maybe duplicate depending on the shader
                Depth2BufferShader.SetFloat("_maxPointDistance", MaxPointDistance);
                Depth2BufferShader.SetMatrix("_Transform", Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one));
                Depth2BufferShader.Dispatch(kernel, dispatch_x, dispatch_y, dispatch_z);

                // Draw resulting PointCloud
                int pixel_count = DepthImage.width * DepthImage.height;

                // Set Pointcloud Properties
                _Buffer2SurfaceMaterial.SetMatrix("_Roi2Dupl", DuplicatedReality.localToWorldMatrix * RegionOfInterest.worldToLocalMatrix);
                _Buffer2SurfaceMaterial.SetMatrix("_ROI_Inversed", RegionOfInterest.worldToLocalMatrix);
                _Buffer2SurfaceMaterial.SetMatrix("_Dupl_Inversed", DuplicatedReality.worldToLocalMatrix);

                Graphics.DrawProcedural(_Buffer2SurfaceMaterial, new Bounds(transform.position, Vector3.one * 10), MeshTopology.Triangles, pixel_count * 6);

                long timeEnd = DateTime.UtcNow.Ticks;
                double timeDiff = (timeEnd - timeBegin) / TimeSpan.TicksPerMillisecond;


                if (gazeProvider != null)
                {
                    Vector3 gazeOrigin = gazeProvider.GazeOrigin;
                    Vector3 gazeDirection = gazeProvider.GazeDirection;

                    //Debug.DrawRay(gazeOrigin, gazeDirection * 5, Color.red);
                    Debug.Log($"Gaze Origin: {gazeOrigin}, Gaze Direction: {gazeDirection}");

                    Ray gazeRay = new Ray(gazeOrigin, gazeDirection);
                    RaycastHit hitInfo;

                    //if (spine.GetComponent<BoxCollider>().Raycast(gazeRay, out hitInfo, Mathf.Infinity))
                    //{
                    //    Debug.Log($"Gaze hit Phantom at: {hitInfo.point}");
                    //    //Debug.DrawRay(gazeOrigin, gazeDirection * hitInfo.distance, Color.green);
                    //}
                }
            }
        }



        private void SetupTextures(ref Texture2D Depth, ref Texture2D ColorInDepth)
        {
            Debug.Log("Setting up textures: DepthWidth=" + DepthWidth + " DepthHeight=" + DepthHeight);

            if (Depth == null)
                Depth = new Texture2D(DepthWidth, DepthHeight, TextureFormat.R16, false);
            if (ColorInDepth == null)
                ColorInDepth = new Texture2D(IRWidth, IRHeight, TextureFormat.BGRA32, false);
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

        private bool SetupShaders(float foV, int texWidth, int texHeight, out int kernelID)
        {
            kernelID = 0;
            // Setup Compute Shader
            if (!Depth2BufferShader)
            {
                Debug.LogError("KinectSubscriber::SetupShaders(): Depth2BufferShader compute shader not found");
                return false;
            }

            kernelID = Depth2BufferShader.FindKernel("Compute");

            Depth2BufferShader.SetInt("_DepthWidth", texWidth);
            Depth2BufferShader.SetInt("_DepthHeight", texHeight);

            // apply sensor to device offset
            Depth2BufferShader.SetMatrix("_Col2DepCalibration", Color2DepthCalibration);

            // Setup Depth2Mesh Shader and reading buffers
            int size = texWidth * texHeight;

            vertexBuffer = new Vector3[size];
            uvBuffer = new Vector2[size];
            indexBuffer = new int[size * 6];

            _vb = new ComputeBuffer(vertexBuffer.Length, 3 * sizeof(float));
            _ub = new ComputeBuffer(uvBuffer.Length, 2 * sizeof(float));
            _ib = new ComputeBuffer(indexBuffer.Length, sizeof(int));

            // Set Kernel variables
            Depth2BufferShader.SetBuffer(kernelID, "vertices", _vb);
            Depth2BufferShader.SetBuffer(kernelID, "uv", _ub);
            Depth2BufferShader.SetBuffer(kernelID, "triangles", _ib);

            Depth2BufferShader.SetTexture(kernelID, "_DepthTex", DepthImage);
            Depth2BufferShader.SetTexture(kernelID, "_XYLookup", XYLookup);

            if (Shaders.Count == 0)
            {
                Debug.LogError("KinectSubscriber::SetupShaders(): Provide at least one point cloud shader");
                return false;
            }

            // Setup Rendering Shaders
            SwitchPointCloudShader(_CurrentSelectedShader);

            return true;
        }

        [ContextMenu("Next Shader")]
        public void NextShaderInList()
        {
            int nextShaderIndex = (_CurrentSelectedShader + 1) % Shaders.Count;

            if (SwitchPointCloudShader(nextShaderIndex))
            {
                Debug.Log("KinectSubscriber::NextShaderInList(): Switched to PointCloud Shader " + Shaders[_CurrentSelectedShader].ID);
            }
        }

        public bool SwitchPointCloudShader(string ID)
        {
            Debug.Log("KinectSubscriber::SwitchPointCloudShader(string ID) " + ID);
            var indexShader = Shaders.FindIndex(x => x.ID == ID);
            if (indexShader >= 0)
                return SwitchPointCloudShader(indexShader);
            else
                return false;
        }

        public bool SwitchPointCloudShader(int indexInList)
        {
            Debug.Log("KinectSubscriber::SwitchPointCloudShader(int indexInList) " + indexInList);
            var currentShaderName = Shaders[indexInList].ShaderName;

            var pc_shader = Shader.Find(currentShaderName);
            if (!pc_shader)
            {
                Debug.LogError("KinectSubscriber::SwitchPointCloudShader(): " + currentShaderName + " shader not found");
                return false;
            }
            _CurrentSelectedShader = indexInList;

            if (!_Buffer2SurfaceMaterial) _Buffer2SurfaceMaterial = new Material(pc_shader);
            else _Buffer2SurfaceMaterial.shader = pc_shader;

            _Buffer2SurfaceMaterial.SetBuffer("vertices", _vb);
            _Buffer2SurfaceMaterial.SetBuffer("uv", _ub);
            _Buffer2SurfaceMaterial.SetBuffer("triangles", _ib);
            _Buffer2SurfaceMaterial.mainTexture = ColorInDepthImage;

            return true;
        }

        private void ReleaseBuffers()
        {
            _vb?.Dispose();
            _ub?.Dispose();
            _ib?.Dispose();
        }

        private void OnDestroy()
        {
            Debug.Log("Destroying subscriber...");

            if (requestSocket != null)
            {
                requestSocket.Dispose();
                requestSocket = null;
                NetMQConfig.Cleanup(false);
            }

            ReleaseBuffers();
        }
    }
}
