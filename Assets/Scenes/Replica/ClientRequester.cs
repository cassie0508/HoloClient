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

namespace Kinect4Azure
{
    public class ClientRequester : MonoBehaviour
    {
        [Header("Networking")]
        [SerializeField] private string host;
        [SerializeField] private string port = "12345";
        private RequestSocket requestSocket;

        [Header("UI Display")]
        public TextMeshProUGUI DelayTMP;
        public TextMeshProUGUI RenderTMP;
        public RawImage DebugImage;

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

        public virtual void OnSetPointcloudProperties(Material pointcloudMat) { }

        private void Start()
        {
            InitializeSocket();
            StartCoroutine(RequestDataLoop());
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
                    Debug.Log($"Received Frame data: {msg.Length} bytes");

                    // Get delay
                    long timestamp = BitConverter.ToInt64(msg, 0);
                    byte[] data = new byte[msg.Length - sizeof(long)];
                    Buffer.BlockCopy(msg, sizeof(long), data, 0, data.Length);

                    long receivedTimestamp = DateTime.UtcNow.Ticks;
                    double delayMilliseconds = (receivedTimestamp - timestamp) / TimeSpan.TicksPerMillisecond;
                    Debug.Log($"Delay for this processing frame: {delayMilliseconds} ms");
                    UnityMainThreadDispatcher.Dispatcher.Enqueue(() =>
                    {
                        DelayTMP.SetText($"Delay in requesting this frame: {delayMilliseconds}");
                    });

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
            if (depthData != null && colorInDepthData != null)
            {
                long timeBegin = DateTime.UtcNow.Ticks;
                
                lock (dataLock)
                {
                    DepthImage.LoadRawTextureData(depthData.ToArray());
                    DepthImage.Apply();

                    ColorInDepthImage.LoadRawTextureData(colorInDepthData.ToArray());
                    ColorInDepthImage.Apply();

                    DebugImage.texture = ColorInDepthImage;
                }

                // Compute triangulation of PointCloud + maybe duplicate depending on the shader
                Depth2BufferShader.SetFloat("_maxPointDistance", MaxPointDistance);
                Depth2BufferShader.SetMatrix("_Transform", Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one));
                Depth2BufferShader.Dispatch(kernel, dispatch_x, dispatch_y, dispatch_z);

                // Draw resulting PointCloud
                int pixel_count = DepthImage.width * DepthImage.height;
                OnSetPointcloudProperties(_Buffer2SurfaceMaterial);
                Graphics.DrawProcedural(_Buffer2SurfaceMaterial, new Bounds(transform.position, Vector3.one * 10), MeshTopology.Triangles, pixel_count * 6);

                long timeEnd = DateTime.UtcNow.Ticks;
                double timeDiff = (timeEnd - timeBegin) / TimeSpan.TicksPerMillisecond;
                RenderTMP.SetText($"Time in rendering this frame: {timeDiff}");
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

        private void OnDestroy()
        {
            Debug.Log("Destroying subscriber...");

            requestSocket.Dispose();
            NetMQConfig.Cleanup(false);
            requestSocket = null;
        }
    }
}
