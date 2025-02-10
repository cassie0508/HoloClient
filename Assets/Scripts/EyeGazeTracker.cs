using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class EyeGazeTracker : MonoBehaviour
{
    public Transform gazeIndicator; // Assign a small sphere in the Inspector to visualize gaze

    void Start()
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.EyeTracking, devices);

        if (devices.Count > 0)
        {
            Debug.Log("Eye tracking device found.");
        }
        else
        {
            Debug.Log("No eye tracking device found.");
        }
    }


    void Update()
    {
        if (TryGetEyeGaze(out Vector3 gazeOrigin, out Vector3 gazeDirection))
        {
            Debug.Log($"Eye Gaze Origin: {gazeOrigin}, Direction: {gazeDirection}");
            if (Physics.Raycast(gazeOrigin, gazeDirection, out RaycastHit hit, 10f)) // Limit raycast distance
            {
                Debug.Log($"Hit detected at {hit.point}");
                gazeIndicator.position = hit.point;
            }
            else
            {
                Debug.Log("No hit detected.");
            }
        }
        else
        {
            Debug.Log("Eye tracking data not available.");
        }
    }


    private bool TryGetEyeGaze(out Vector3 origin, out Vector3 direction)
    {
        origin = Vector3.zero;
        direction = Vector3.forward;

        InputDevice eyeDevice = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
        if (eyeDevice.TryGetFeatureValue(CommonUsages.eyesData, out Eyes eyes))
        {
            if (eyes.TryGetFixationPoint(out Vector3 fixationPoint))
            {
                origin = Camera.main.transform.position;
                direction = (fixationPoint - origin).normalized;
                return true;
            }
        }
        return false;
    }
}
