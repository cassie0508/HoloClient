using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vuforia;

public class TestTwoMarkers : MonoBehaviour
{
    [Header("AR Camera Settings")]
    [SerializeField] private GameObject arCamera;
    [SerializeField] private GameObject marker2;
    [SerializeField] private GameObject marker1;

    [SerializeField] private GameObject kinect;

    private ObserverBehaviour marker2Observer;
    private ObserverBehaviour marker1Observer;

    void Start()
    {
        // Calculate KinectToMarker2
        Matrix4x4 KinectToMarker2 = Matrix4x4.TRS(kinect.transform.localPosition,
            kinect.transform.localRotation, 
            Vector3.one);
        Debug.Log($"KinectToMarker2 \n {KinectToMarker2}");

        marker2Observer = marker2.GetComponent<ObserverBehaviour>();
        marker1Observer = marker1.GetComponent<ObserverBehaviour>();
        if (marker2Observer == null) Debug.LogError("marker2Observer does not have ObserverBehaviour attached.");
        if (marker1Observer == null) Debug.LogError("marker1Observer does not have ObserverBehaviour attached.");
    }

    void Update()
    {
        if (marker2Observer.TargetStatus.Status == Status.TRACKED)
        {
            Matrix4x4 O2Marker2 = Matrix4x4.TRS(marker2.transform.position, marker2.transform.rotation, Vector3.one);
            Debug.Log($"O2Marker2 \n {O2Marker2} \n arcamera \n {arCamera.transform.localToWorldMatrix}");
        }

        if (marker1Observer.TargetStatus.Status == Status.TRACKED)
        {
            Matrix4x4 O2Marker1 = Matrix4x4.TRS(marker1.transform.position, marker1.transform.rotation, Vector3.one);
            Debug.Log($"O2Marker1 \n {O2Marker1} \n arcamera \n {arCamera.transform.localToWorldMatrix}");
        }
    }
}
