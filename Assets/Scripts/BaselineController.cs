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

public class BaselineController : MonoBehaviour
{
    [Header("Calibration")]
    [SerializeField] private GameObject spinePlaceholder;
    [SerializeField] private GameObject marker1;

    private ObserverBehaviour marker1Observer;

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
                Phantom.transform.SetPositionAndRotation(spinePlaceholder.transform.position, spinePlaceholder.transform.rotation);
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
}
