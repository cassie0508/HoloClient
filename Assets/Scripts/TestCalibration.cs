using PubSub;
using System;
using UnityEngine;
using Vuforia;

public class TestCalibration : MonoBehaviour
{
    [Header("AR Camera Settings")]
    [SerializeField] private GameObject arCamera;
    [SerializeField] private GameObject imageTarget;
    [SerializeField] private GameObject model;

    [SerializeField] private string host;
    [SerializeField] private string port = "55555";
    private Subscriber subscriber;

    private Matrix4x4 Marker2Kinect;
    private ObserverBehaviour targetObserver;
    public Transform DebugObject1;
    public Transform DebugObject2;

    private bool hasCalibrationData = false;

    void Start()
    {
        try
        {
            subscriber = new Subscriber(host, port);
            subscriber.AddTopicCallback("Calibration", data => OnCalibrationReceived(data));
            Debug.Log("Subscriber setup complete with host: " + host + " and port: " + port);
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to start subscriber: " + e.Message);
        }

        targetObserver = imageTarget.GetComponent<ObserverBehaviour>();
        if (targetObserver == null)
        {
            Debug.LogError("Image Target does not have ObserverBehaviour attached.");
        }
    }

    private void OnCalibrationReceived(byte[] data)
    {
        Marker2Kinect = ByteArrayToMatrix4x4(data);
        hasCalibrationData = true;
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

    void Update()
    {
        if (targetObserver == null || hasCalibrationData == false) return;

        if (targetObserver.TargetStatus.Status == Status.TRACKED)
        {
            Matrix4x4 O2Marker = Matrix4x4.TRS(imageTarget.transform.position, imageTarget.transform.rotation, Vector3.one);
            Matrix4x4 O2Kinect = O2Marker * Marker2Kinect;

            Vector3 position = O2Kinect.GetPosition();
            Vector3 newPosition = new Vector3 { x = position.x, y = position.y, z = position.z / 2 };

            if (DebugObject1)
                DebugObject1.SetPositionAndRotation(position, O2Kinect.rotation);

            if(DebugObject2)
            {
                DebugObject2.localPosition = Marker2Kinect.GetPosition();
                DebugObject2.localRotation = Marker2Kinect.rotation;
            }
        }
    }
}
