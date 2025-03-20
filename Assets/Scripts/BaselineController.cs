using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Vuforia;
using static PBM_Observer;

public class BaselineController : MonoBehaviour
{
    [Header("Calibration")]
    [SerializeField] private GameObject spinePlaceholder;
    [SerializeField] private GameObject marker1;

    private ObserverBehaviour marker1Observer;
    private Matrix4x4 OToMarker1;

    [Header("UI Elements")]
    public TextMeshProUGUI CalibrationText; // UI text for displaying calibration state

    [Header("Input Actions")]
    public InputAction CalibrationSpineAction;  // Left bumper
    public InputAction Round0Action; // Y
    public InputAction Round1Action; // X
    public InputAction Round2Action; // B
    public InputAction Round3Action; // A

    [Header("Virtual Guidance")]
    public GameObject Spine;
    public List<GameObject> Cylinders = new List<GameObject>();

    [Header("Colliding Test")]
    public Collider Gorilla;
    public Collider Phantom;
    public List<GameObject> SpineCubes = new List<GameObject>();

    private int round = 0;
    private bool hasCalibrationDone = false;

    private bool spineCalibrated = false;
    private bool kinectCalibrated = false;
    private bool trackingSpine = false;
    private bool trackingKinect = false;

    private VuforiaBehaviour vuforia;

    // Eye gaze tracking
    private IMixedRealityGazeProvider gazeProvider;
    private bool isRecordingGaze = false;
    private string gazeDataFilePath;

    private void Awake()
    {
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

        marker1Observer = marker1.GetComponent<ObserverBehaviour>();

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

        Round0Action.Disable();
        Round0Action.performed -= OnRound0;

        Round1Action.Disable();
        Round1Action.performed -= OnRound1;

        Round2Action.Disable();
        Round2Action.performed -= OnRound2;

        Round3Action.Disable();
        Round3Action.performed -= OnRound3;
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
            Debug.Log("Spine calibration confirmed.");
            UpdateStatusText("");

            string logEntryMarker1Position = $",,{DateTime.Now.ToString("yyyyMMdd_HHmmss")},Marker1,{OToMarker1.GetPosition().x},{OToMarker1.GetPosition().y},{OToMarker1.GetPosition().z}\n";
            string logEntryMarker1Rotation = $",,{DateTime.Now.ToString("yyyyMMdd_HHmmss")},Marker1,{OToMarker1.rotation.eulerAngles.x},{OToMarker1.rotation.eulerAngles.y},{OToMarker1.rotation.eulerAngles.z}\n";
            File.AppendAllText(gazeDataFilePath, logEntryMarker1Position);
            File.AppendAllText(gazeDataFilePath, logEntryMarker1Rotation);
        }
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
                Spine.transform.SetPositionAndRotation(spinePlaceholder.transform.position, spinePlaceholder.transform.rotation);
                Debug.Log("Marker 1 tracked and updated.");
            }
            yield return null;
        }

        DisableTracking(marker1Observer);
        spineCalibrated = true;
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
        if (hasCalibrationDone)
        {
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
        if (Phantom.Raycast(gazeRay, out hitInfo, Mathf.Infinity))
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
}
