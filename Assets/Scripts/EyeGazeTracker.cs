using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using UnityEngine;

public class EyeGazeTracker : MonoBehaviour
{
    private IMixedRealityGazeProvider gazeProvider;

    void Start()
    {
        if (CoreServices.InputSystem != null)
        {
            gazeProvider = CoreServices.InputSystem.GazeProvider;
        }

        if (gazeProvider == null)
        {
            Debug.LogError("GazeProvider not found£¡");
        }
    }

    void Update()
    {
        if (gazeProvider != null)
        {
            Vector3 gazeOrigin = gazeProvider.GazeOrigin;

            Vector3 gazeDirection = gazeProvider.GazeDirection;

            Debug.DrawRay(gazeOrigin, gazeDirection * 5, Color.red);

            Debug.Log($"Gaze Origin: {gazeOrigin}, Gaze Direction: {gazeDirection}");
        }
    }
}
