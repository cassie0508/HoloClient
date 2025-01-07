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

    private Matrix4x4 marker2kinect;
    private ObserverBehaviour targetObserver;
    public Transform DebugObject;

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
        marker2kinect = ByteArrayToMatrix4x4(data);
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
            Matrix4x4 O2image = Matrix4x4.TRS(imageTarget.transform.position, imageTarget.transform.rotation, Vector3.one);
            Matrix4x4 O2Kinect = O2image * marker2kinect;

            if (DebugObject)
                DebugObject.SetPositionAndRotation(O2Kinect.GetPosition(), O2Kinect.rotation);
        }
    }
}
