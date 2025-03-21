﻿using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using Vuforia;
using UnityEngine.SpatialTracking;
using UnityEngine.InputSystem;
using TMPro;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit;
using System.IO;
using System;

[RequireComponent(typeof(Camera))]
public class PBM_Observer : MonoBehaviour
{
    public static PBM_Observer Instance = null;
    [Header("Default Setting\nAccess individual settings with the dictionary \"PBMs\".")]
    public bool Cropping;
    [Range(0, 1)]
    public float CropSize = 0.5f;
    [Range(0, 1)]
    public float Transparency = 1;
    [Range(0, 0.5f)]
    public float BorderSize = 0.01f;

    [Space]
    public Texture2D BorderTexture;
    public Texture2D MirrorSpecular;

    private Camera ObserverCam;
    private PBM_CaptureCamera CapturingCamera;
    private PBM pbm;

    [Header("Calibration")]
    [SerializeField] private GameObject spinePlaceholder;
    [SerializeField] private GameObject kinectPlaceholder;
    [SerializeField] private GameObject marker1;
    [SerializeField] private GameObject marker2;
    private Matrix4x4 OToMarker1;
    private Matrix4x4 OToMarker2;
    private Matrix4x4 OToKinect;

    private ObserverBehaviour marker1Observer;
    private ObserverBehaviour marker2Observer;

    [Header("UI Elements")]
    public TextMeshProUGUI CalibrationText; // UI text for displaying calibration state

    [Header("Input Actions")]
    public InputAction CalibrationSpineAction;  // Left bumper
    public InputAction CalibrationKinectAction; // Right bumper
    public InputAction ShowSpineAction; // Left trigger
    public InputAction Round0Action; // Y
    public InputAction Round1Action; // X
    public InputAction Round2Action; // B
    public InputAction Round3Action; // A

    [Header("Virtual Guidance")]
    public GameObject Spine;
    public GameObject Phantom;
    public List<GameObject> Cylinders = new List<GameObject>();

    [Header("Colliding Test")]
    public Collider Gorilla;
    public List<GameObject> SpineCubes = new List<GameObject>();

    

    private int round = 0;

    private bool spineCalibrated = false;
    private bool kinectCalibrated = false;
    private bool trackingSpine = false;
    private bool trackingKinect = false;
    private bool isSpineVisible = false;

    private VuforiaBehaviour vuforia;

    // Eye gaze tracking
    private IMixedRealityGazeProvider gazeProvider;
    private bool isRecordingGaze = false;
    private string gazeDataFilePath;

    // PBM variables
    public class PBM
    {
        public Camera SourceCamera;
        public GameObject ImageQuad;
        public Mesh ImageQuadMesh;
        public Material ImageMat;
        public MeshRenderer ImageRenderer;
        public RenderTexture Texture;
        [Header("Cropping and Transparency")]
        public Material CropAndTransparency;
        public bool EnableCropping = true;
        public float CropSize = 0.5f;
        [Range(0, 1)]
        public float Transparency = 1;
        [Range(0, 0.5f)]
        public float BorderSize = 0.002f;
        public PBM()
        {
            CropAndTransparency = new Material(Shader.Find("PBM/CropTransparent"));
        }
        public void DestroyContent()
        {
            Destroy(ImageQuad);
            Destroy(ImageQuadMesh);
            Destroy(ImageMat);
            Destroy(ImageRenderer);
            Destroy(Texture);
        }
    }

    private void Awake()
    {
        Instance = this;

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        gazeDataFilePath = Path.Combine(Application.persistentDataPath, $"GazeHitData_{timestamp}.csv");
        Debug.Log($"Save file at {gazeDataFilePath}");

        if (!File.Exists(gazeDataFilePath))
        {
            // Type: 
            // 0: HeadPosition; 1: HeadForward; 2: HeadUp; 3: HeadRight
            // 4: GazeOrigin; 5: GazeDirection; 6: GazeHitPhantom; 7: GazeHitSpineCubes; 8: GazeHitCylinders; 9: GazeHitGorilla
            File.AppendAllText(gazeDataFilePath, "Round,Type,Timestamp,HitObject,PositionX,PositionY,PositionZ\n");
        }

        ObserverCam = GetComponent<Camera>();


        if (BorderTexture == null)
            BorderTexture = Resources.Load("PBM/PBM_MirrorFrame") as Texture2D;
        if (MirrorSpecular == null)
            MirrorSpecular = Resources.Load("PBM/PBM_MirrorSpecular") as Texture2D;

        marker1Observer = marker1.GetComponent<ObserverBehaviour>();
        marker2Observer = marker2.GetComponent<ObserverBehaviour>();

        UpdateStatusText("Press left bumper to calibrate spine");

        if (CoreServices.InputSystem != null)
        {
            gazeProvider = CoreServices.InputSystem.GazeProvider;
        }

        vuforia = FindObjectOfType<VuforiaBehaviour>(true);

        UpdateCylinderVisibility();
    }
    

    private void OnEnable()
    {
        CalibrationSpineAction.Enable();
        CalibrationSpineAction.performed += OnCalibrateSpine;

        CalibrationKinectAction.Enable();
        CalibrationKinectAction.performed += OnCalibrateKinect;

        ShowSpineAction.Enable();
        ShowSpineAction.performed += OnShowSpine;

        Round0Action.Enable();
        Round0Action.performed += OnRound0;

        Round1Action.Enable();
        Round1Action.performed += OnRound1;

        Round2Action.Enable();
        Round2Action.performed += OnRound2;

        Round3Action.Enable();
        Round3Action.performed += OnRound3;
    }

    private void OnDisable()
    {
        CalibrationSpineAction.Disable();
        CalibrationSpineAction.performed -= OnCalibrateSpine;

        CalibrationKinectAction.Disable();
        CalibrationKinectAction.performed -= OnCalibrateKinect;

        ShowSpineAction.Disable();
        ShowSpineAction.performed -= OnShowSpine;

        Round0Action.Disable();
        Round0Action.performed -= OnRound0;

        Round1Action.Disable();
        Round1Action.performed -= OnRound1;

        Round2Action.Disable();
        Round2Action.performed -= OnRound2;

        Round3Action.Disable();
        Round3Action.performed -= OnRound3;
    }

    private void OnShowSpine(InputAction.CallbackContext context)
    {
        isSpineVisible = !isSpineVisible;
        Spine.GetComponent<MeshRenderer>().enabled = isSpineVisible;
    }

    private void OnRound0(InputAction.CallbackContext context)
    {
        round = 0;
        UpdateCylinderVisibility();
    }

    private void OnRound1(InputAction.CallbackContext context)
    {
        round = 1;
        UpdateCylinderVisibility();
    }

    private void OnRound2(InputAction.CallbackContext context)
    {
        round = 2;
        UpdateCylinderVisibility();
    }

    private void OnRound3(InputAction.CallbackContext context)
    {
        round = 3;
        UpdateCylinderVisibility();
    }

    private void UpdateCylinderVisibility()
    {
        for (int i = 0; i < Cylinders.Count; i++)
        {
            if (round == 0)
            {
                Cylinders[i].SetActive(false); // all not visible
            }
            else if (round == 1 && i < 2)
            {
                Cylinders[i].SetActive(true); // only first two visible
            }
            else if (round == 2 && i < 4)
            {
                Cylinders[i].SetActive(true); // only first four visible
            }
            else if (round == 3 && i >= 4)
            {
                Cylinders[i].SetActive(true); // only last four visible
            }
            else
            {
                Cylinders[i].SetActive(false);
            }
        }

        Debug.Log($"Round: {round}, Cylinder Visibility Updated.");
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
            string logEntryMarker1Position = $",,{DateTime.Now.ToString("yyyyMMdd_HHmmss")},Marker1,{OToMarker1.GetPosition().x},{OToMarker1.GetPosition().y},{OToMarker1.GetPosition().z}\n";
            string logEntryMarker1Rotation = $",,{DateTime.Now.ToString("yyyyMMdd_HHmmss")},Marker1,{OToMarker1.rotation.eulerAngles.x},{OToMarker1.rotation.eulerAngles.y},{OToMarker1.rotation.eulerAngles.z}\n";
            File.AppendAllText(gazeDataFilePath, logEntryMarker1Position);
            File.AppendAllText(gazeDataFilePath, logEntryMarker1Rotation);
            UpdateStatusText("");
            Debug.Log("Spine calibration confirmed.");
        }
    }

    private void OnCalibrateKinect(InputAction.CallbackContext context)
    {
        if (!spineCalibrated)
        {
            UpdateStatusText("Calibrate spine first (Press left bumper)");
            return;
        }

        if (!trackingKinect)
        {
            StartCoroutine(CalibrateKinect());
        }
        else
        {
            trackingKinect = false;
            string logEntryMarker2Position = $",,{DateTime.Now.ToString("yyyyMMdd_HHmmss")},Marker2,{OToMarker2.GetPosition().x},{OToMarker2.GetPosition().y},{OToMarker2.GetPosition().z}\n";
            string logEntryMarker2Rotation = $",,{DateTime.Now.ToString("yyyyMMdd_HHmmss")},Marker2,{OToMarker2.rotation.eulerAngles.x},{OToMarker2.rotation.eulerAngles.y},{OToMarker2.rotation.eulerAngles.z}\n";
            File.AppendAllText(gazeDataFilePath, logEntryMarker2Position);
            File.AppendAllText(gazeDataFilePath, logEntryMarker2Rotation);

            if (pbm == null && CapturingCamera == null)
            {
                // initialize pbm
                pbm = new PBM();
                CapturingCamera = FindObjectOfType<PBM_CaptureCamera>();
                CapturingCamera.transform.SetPositionAndRotation(OToKinect.GetPosition(), OToKinect.rotation);
                RegisterCapturer(CapturingCamera);
                Debug.Log("Kinect calibration confirmed. Initialize pbm and CapturingCamera");
            }
            else if (pbm != null && CapturingCamera != null)
            {
                CapturingCamera.transform.SetPositionAndRotation(OToKinect.GetPosition(), OToKinect.rotation);
                pbm.ImageQuad.transform.parent = CapturingCamera.transform;
                Debug.Log("Kinect calibration confirmed. Update pbm and CapturingCamera");
            }

            UpdateStatusText("");
        }
    }

    private void RegisterCapturer(PBM_CaptureCamera capturer)
    {
        pbm.SourceCamera = capturer.GetComponent<Camera>();
        pbm.ImageQuad = new GameObject();
        pbm.ImageQuad.name = "PBM_" + capturer.name;
        pbm.ImageQuad.transform.parent = capturer.transform;
        pbm.ImageQuad.transform.localPosition = Vector3.zero;
        pbm.ImageQuad.transform.localRotation = Quaternion.identity;
        pbm.ImageQuad.transform.localScale = Vector3.one;
        pbm.ImageQuadMesh = new Mesh();
        pbm.ImageQuad.AddComponent<MeshFilter>().mesh = pbm.ImageQuadMesh;

        pbm.ImageMat = Instantiate(Resources.Load("PBM/PBMQuadMaterial") as Material);
        pbm.Texture = new RenderTexture(capturer.Width, capturer.Height, 24, RenderTextureFormat.ARGB32); //new Texture2D(capturer.Width, capturer.Height, TextureFormat.RGBA32, false);
        pbm.ImageMat.mainTexture = pbm.Texture;
        pbm.ImageRenderer = pbm.ImageQuad.AddComponent<MeshRenderer>();
        pbm.ImageRenderer.material = pbm.ImageMat;
        pbm.ImageQuad.layer = LayerMask.NameToLayer("PBM");

        pbm.EnableCropping = Cropping;
        pbm.CropSize = CropSize;
        pbm.Transparency = Transparency;
        pbm.BorderSize = 0.01f;
    }


    private IEnumerator CalibrateSpine()
    {
        trackingSpine = true;
        EnableTracking(marker1Observer);
        UpdateStatusText("Tracking marker 1... Press left bumper again to confirm.");

        while (trackingSpine)
        {
            if (marker1Observer.TargetStatus.Status == Status.TRACKED)  // Will set its children
            {
                OToMarker1 = Matrix4x4.TRS(marker1.transform.position, marker1.transform.rotation, Vector3.one);
                Phantom.transform.SetPositionAndRotation(spinePlaceholder.transform.position, spinePlaceholder.transform.rotation);
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
            if (marker2Observer.TargetStatus.Status == Status.TRACKED)
            {
                OToMarker2 = Matrix4x4.TRS(marker2.transform.position, marker2.transform.rotation, Vector3.one);
                OToKinect = Matrix4x4.TRS(kinectPlaceholder.transform.position, kinectPlaceholder.transform.rotation, Vector3.one);
                Debug.Log("Marker 2 tracked and updated.");
            }
            yield return null;
        }

        Debug.Log($"kinect transform {OToKinect.GetPosition()}, {OToKinect.rotation.eulerAngles}");
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


    private void Update()
    {
        if (pbm != null && CapturingCamera != null)
        {
            UpdatePBM();
            CheckGazeHit();
        }
    }

    private void CheckGazeHit()
    {
        if (gazeProvider == null)
            return;

        /* Head */
        Vector3 headPosition = Camera.main.transform.position;
        Vector3 headForward = Camera.main.transform.forward;
        Vector3 headUp = Camera.main.transform.up;
        Vector3 headRight = Camera.main.transform.right;

        string logEntry0 = $"{round},0,{DateTime.Now.ToString("yyyyMMdd_HHmmss")},HeadPosition,{headPosition.x},{headPosition.y},{headPosition.z}\n";
        File.AppendAllText(gazeDataFilePath, logEntry0);
        Debug.Log($"HeadPosition: {headPosition}");
        string logEntry1 = $"{round},1,{DateTime.Now.ToString("yyyyMMdd_HHmmss")},HeadForward,{headForward.x},{headForward.y},{headForward.z}\n";
        File.AppendAllText(gazeDataFilePath, logEntry1);
        string logEntry2 = $"{round},2,{DateTime.Now.ToString("yyyyMMdd_HHmmss")},HeadUp,{headUp.x},{headUp.y},{headUp.z}\n";
        File.AppendAllText(gazeDataFilePath, logEntry2);
        string logEntry3 = $"{round},3,{DateTime.Now.ToString("yyyyMMdd_HHmmss")},HeadRight,{headRight.x},{headRight.y},{headRight.z}\n";
        File.AppendAllText(gazeDataFilePath, logEntry3);

        /* Eye */
        Vector3 gazeOrigin = gazeProvider.GazeOrigin;
        Vector3 gazeDirection = gazeProvider.GazeDirection;
        Ray gazeRay = new Ray(gazeOrigin, gazeDirection);
        RaycastHit hitInfo;

        string logEntry4 = $"{round},4,{DateTime.Now.ToString("yyyyMMdd_HHmmss")},GazeOrigin,{gazeOrigin.x},{gazeOrigin.y},{gazeOrigin.z}\n";
        File.AppendAllText(gazeDataFilePath, logEntry4);
        Debug.Log($"gazeOrigin: {gazeOrigin}");
        string logEntry5 = $"{round},5,{DateTime.Now.ToString("yyyyMMdd_HHmmss")},GazeDirection,{gazeDirection.x},{gazeDirection.y},{gazeDirection.z}\n";
        File.AppendAllText(gazeDataFilePath, logEntry5);

        // Only when it hits phantom, then it can hit others
        if (Phantom.GetComponent<Collider>().Raycast(gazeRay, out hitInfo, Mathf.Infinity))
        {
            string logEntry6 = $"{round},6,{DateTime.Now.ToString("yyyyMMdd_HHmmss")},Phantom,{hitInfo.point.x},{hitInfo.point.y},{hitInfo.point.z}\n";
            File.AppendAllText(gazeDataFilePath, logEntry6);
            Debug.Log($"Phantom: {hitInfo}");

            foreach (var cube in SpineCubes)
            {
                if (cube.GetComponent<Collider>().Raycast(gazeRay, out hitInfo, Mathf.Infinity))
                {
                    string logEntry7 = $"{round},7,{DateTime.Now.ToString("yyyyMMdd_HHmmss")},{cube.name},{hitInfo.point.x},{hitInfo.point.y},{hitInfo.point.z}\n";
                    File.AppendAllText(gazeDataFilePath, logEntry7);
                }
            }

            // Only test active cylinders
            foreach (var cylinder in Cylinders)
            {
                if (cylinder.activeSelf && cylinder.GetComponent<Collider>().Raycast(gazeRay, out hitInfo, Mathf.Infinity))
                {
                    string logEntry8 = $"{round},8,{DateTime.Now.ToString("yyyyMMdd_HHmmss")},{cylinder.name},{hitInfo.point.x},{hitInfo.point.y},{hitInfo.point.z}\n";
                    File.AppendAllText(gazeDataFilePath, logEntry8);
                }
            }

            if (Gorilla.GetComponent<Collider>().Raycast(gazeRay, out hitInfo, Mathf.Infinity))
            {
                string logEntry9 = $"{round},9,{DateTime.Now.ToString("yyyyMMdd_HHmmss")},Gorilla,{hitInfo.point.x},{hitInfo.point.y},{hitInfo.point.z}\n";
                File.AppendAllText(gazeDataFilePath, logEntry9);
            }
        }
    }

    private void UpdatePBM()
    {
        var c_PBM = pbm;

        if (!CapturingCamera.isActiveAndEnabled)
        {
            c_PBM.ImageQuad.SetActive(false);
            return;
        }

        var cameraMidPoint = (CapturingCamera.transform.position + ObserverCam.transform.position) / 2;

        var mirrorNormal = Vector3.Normalize(ObserverCam.transform.position - cameraMidPoint);

        CapturingCamera.UpdateValidAreaCompensationWithObserver(ObserverCam.transform.position);

        if (ComputePlaneCornerIntersection(CapturingCamera, cameraMidPoint, mirrorNormal,
            out var lt_world, out var rt_world, out var rb_world, out var lb_world, true))
        {

            if (Line3DIntersection(lt_world, rb_world, rt_world, lb_world, out var center))
            {

                c_PBM.ImageQuad.SetActive(true);

                c_PBM.CropAndTransparency.SetFloat("CompensationRatio", CapturingCamera.Ratio);
                // Cropping
                if (c_PBM.EnableCropping)
                {
                    c_PBM.CropAndTransparency.SetFloat("_EnableCropping", 1);

                    var gazeRay = new Ray(ObserverCam.transform.position, ObserverCam.transform.forward);
                    Plane p = new Plane(mirrorNormal, cameraMidPoint);

                    if (p.Raycast(gazeRay, out float hitPlane))
                    {
                        var hitPosition = gazeRay.GetPoint(hitPlane);

                        // Project point onto top edge
                        var screenPoint = (Vector2)c_PBM.SourceCamera.WorldToViewportPoint(hitPosition);
                        var cropAndTransMat = c_PBM.CropAndTransparency;

                        cropAndTransMat.SetVector("uv_topleft", new Vector2(Mathf.Clamp01(screenPoint.x - c_PBM.CropSize), Mathf.Clamp01(screenPoint.y - c_PBM.CropSize)));
                        cropAndTransMat.SetVector("uv_topright", new Vector2(Mathf.Clamp01(screenPoint.x + c_PBM.CropSize), Mathf.Clamp01(screenPoint.y - c_PBM.CropSize)));
                        cropAndTransMat.SetVector("uv_bottomleft", new Vector2(Mathf.Clamp01(screenPoint.x - c_PBM.CropSize), Mathf.Clamp01(screenPoint.y + c_PBM.CropSize)));
                        cropAndTransMat.SetVector("uv_bottomright", new Vector2(Mathf.Clamp01(screenPoint.x + c_PBM.CropSize), Mathf.Clamp01(screenPoint.y + c_PBM.CropSize)));
                    }
                }
                else
                {
                    c_PBM.CropAndTransparency.SetFloat("_EnableCropping", 0);
                }

                c_PBM.CropAndTransparency.EnableKeyword("USE_MIRROR_SPECULAR_");

                c_PBM.CropAndTransparency.SetFloat("MainTextureTransparency", c_PBM.Transparency);

                c_PBM.CropAndTransparency.SetFloat("BorderSize", c_PBM.BorderSize);

                c_PBM.CropAndTransparency.SetTexture("_MirrorFrameTex", BorderTexture);

                c_PBM.CropAndTransparency.SetTexture("_MirrorSpecular", MirrorSpecular);


                Graphics.Blit(CapturingCamera.ViewRenderTexture, c_PBM.Texture, c_PBM.CropAndTransparency);

                var cam2Tranform = CapturingCamera.transform.worldToLocalMatrix;
                c_PBM.ImageQuadMesh.vertices = new Vector3[]
                {
                        cam2Tranform.MultiplyPoint(lt_world),
                        cam2Tranform.MultiplyPoint(rt_world),
                        cam2Tranform.MultiplyPoint(rb_world),
                        cam2Tranform.MultiplyPoint(lb_world)
                };

                float lbd = (Vector3.Distance(lb_world, center) + Vector3.Distance(rt_world, center)) / Vector3.Distance(rt_world, center);
                float rbd = (Vector3.Distance(rb_world, center) + Vector3.Distance(lt_world, center)) / Vector3.Distance(lt_world, center);
                float rtb = (Vector3.Distance(rt_world, center) + Vector3.Distance(lb_world, center)) / Vector3.Distance(lb_world, center);
                float ltb = (Vector3.Distance(lt_world, center) + Vector3.Distance(rb_world, center)) / Vector3.Distance(rb_world, center);

                c_PBM.ImageQuadMesh.SetUVs(0, new Vector3[] { new Vector3(0, ltb, ltb), new Vector3(rtb, rtb, rtb), new Vector3(rbd, 0, rbd), new Vector3(0, 0, lbd) });

                c_PBM.ImageQuadMesh.SetIndices(new int[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 }, MeshTopology.Triangles, 0);
                c_PBM.ImageQuadMesh.RecalculateBounds();
            }
            else
            {
                c_PBM.ImageQuad.SetActive(false);
            }

        }
        else
        {
            c_PBM.ImageQuad.SetActive(false);
        }

    }

    private void PrintVector(string name, Vector3 v)
    {
        Debug.Log(name + ": " + v.x.ToString("0.000") + " " + v.y.ToString("0.000") + " " + v.z.ToString("0.000"));
    }
    public bool Line2DIntersection(Vector2 A1, Vector2 A2, Vector2 B1, Vector2 B2, out Vector2 intersection)
    {
        float tmp = (B2.x - B1.x) * (A2.y - A1.y) - (B2.y - B1.y) * (A2.x - A1.x);

        if (tmp == 0)
        {
            // No solution!
            intersection = Vector2.zero;
            return false;
        }

        float mu = ((A1.x - B1.x) * (A2.y - A1.y) - (A1.y - B1.y) * (A2.x - A1.x)) / tmp;

        intersection = new Vector2(
            B1.x + (B2.x - B1.x) * mu,
            B1.y + (B2.y - B1.y) * mu
        );
        return true;
    }

    // http://paulbourke.net/geometry/pointlineplane/
    public bool Line3DIntersection(Vector3 A1, Vector3 A2,
    Vector3 B1, Vector3 B2, out Vector3 intersection)
    {
        intersection = Vector3.zero;

        Vector3 p13 = A1 - B1;
        Vector3 p43 = B2 - B1;

        if (p43.sqrMagnitude < Mathf.Epsilon)
        {
            return false;
        }
        Vector3 p21 = A2 - A1;
        if (p21.sqrMagnitude < Mathf.Epsilon)
        {
            return false;
        }

        float d1343 = p13.x * p43.x + p13.y * p43.y + p13.z * p43.z;
        float d4321 = p43.x * p21.x + p43.y * p21.y + p43.z * p21.z;
        float d1321 = p13.x * p21.x + p13.y * p21.y + p13.z * p21.z;
        float d4343 = p43.x * p43.x + p43.y * p43.y + p43.z * p43.z;
        float d2121 = p21.x * p21.x + p21.y * p21.y + p21.z * p21.z;

        float denom = d2121 * d4343 - d4321 * d4321;
        if (Mathf.Abs(denom) < Mathf.Epsilon)
        {
            return false;
        }
        float numer = d1343 * d4321 - d1321 * d4343;

        float mua = numer / denom;
        float mub = (d1343 + d4321 * (mua)) / d4343;

        var MA = A1 + mua * p21;
        var MB = B1 + mub * p43;

        intersection = (MA + MB) / 2;

        return true;
    }

    private Vector3 ClosestPoint(Vector3 origin, Vector3 direction, Vector3 point)
    {
        return origin + Vector3.Project(point - origin, direction);
    }

    private Vector3 ClosestPointOnFirstRay(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        var t = (Vector3.Dot(c - a, b) * Vector3.Dot(d, d) +
                 Vector3.Dot(a - c, d) * Vector3.Dot(b, d)) /
                 (Vector3.Dot(b, b) * Vector3.Dot(d, d) - Vector3.Dot(b, d) * Vector3.Dot(b, d));

        var s = (Vector3.Dot(a - b, d) * Vector3.Dot(b, b) +
                 Vector3.Dot(c - a, b) * Vector3.Dot(b, d)) /
                 (Vector3.Dot(b, b) * Vector3.Dot(d, d) - Vector3.Dot(b, d) * Vector3.Dot(b, d));

        var onFirst = a + b * t;
        var onSecond = c + d * s;
        var mid = 0.5f * (onFirst + onSecond);

        return onFirst;
    }

    public bool ComputePlaneCornerIntersection(PBM_CaptureCamera capturer, Vector3 planeCenter, Vector3 planeNormal, 
        out Vector3 LT, out Vector3 RT, out Vector3 RB, out Vector3 LB, bool useWorldSpace = false)
    {
        var camPos = capturer.transform.position;
        float halfWidth = capturer.Width / 2;
        float halfHeight = capturer.Height / 2;
        float f = capturer.FocalLength;
        // max vertices
        var tlF = capturer.transform.TransformPoint(new Vector3(-halfWidth / f, halfHeight / f, 1));
        var trF = capturer.transform.TransformPoint(new Vector3(halfWidth / f, halfHeight / f, 1));
        var brF = capturer.transform.TransformPoint(new Vector3(halfWidth / f, -halfHeight / f, 1));
        var blF = capturer.transform.TransformPoint(new Vector3(-halfWidth / f, -halfHeight / f, 1));

        var plane = new Plane(planeNormal, planeCenter);

        var rayLT = new Ray(camPos, tlF - camPos);
        var rayRT = new Ray(camPos, trF - camPos);
        var rayRB = new Ray(camPos, brF - camPos);
        var rayLB = new Ray(camPos, blF - camPos);

        if (plane.Raycast(rayLT, out float hitlt) && plane.Raycast(rayRT, out float hitrt) && plane.Raycast(rayRB, out float hitrb) && plane.Raycast(rayLB, out float hitlb))
        {
            LT = rayLT.GetPoint(hitlt);
            RT = rayRT.GetPoint(hitrt);
            RB = rayRB.GetPoint(hitrb);
            LB = rayLB.GetPoint(hitlb);

            if (!useWorldSpace)
            {
                LT = capturer.transform.InverseTransformPoint(LT);
                RT = capturer.transform.InverseTransformPoint(RT);
                RB = capturer.transform.InverseTransformPoint(RB);
                LB = capturer.transform.InverseTransformPoint(LB);
            }
            return true;
        }
        else
        {
            LT = Vector3.zero;
            RT = Vector3.zero;
            RB = Vector3.zero;
            LB = Vector3.zero;
            return false;
        }

    }

    public static void SetGlobalScale(Transform t, Vector3 globalScale)
    {
        t.localScale = Vector3.one;
        t.localScale = new Vector3(globalScale.x / t.lossyScale.x, globalScale.y / t.lossyScale.y, globalScale.z / t.lossyScale.z);
    }
}