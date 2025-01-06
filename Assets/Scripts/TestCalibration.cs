using UnityEngine;
using Vuforia;

public class TestCalibration : MonoBehaviour
{
    [Header("AR Camera Settings")]
    [SerializeField] private GameObject arCamera;
    [SerializeField] private GameObject imageTarget;
    [SerializeField] private GameObject model;

    private Matrix4x4 kinect2marker;
    private Matrix4x4 modelInKinect;
    private ObserverBehaviour targetObserver;
    private bool isTracking = false;

    void Start()
    {
        kinect2marker = new Matrix4x4
        {
            m00 = -0.02802f,
            m01 = 0.99953f,
            m02 = 0.01214f,
            m03 = 0.07007f,
            m10 = -0.14232f,
            m11 = 0.00803f,
            m12 = -0.98979f,
            m13 = 1.04418f,
            m20 = -0.98942f,
            m21 = -0.02946f,
            m22 = 0.14203f,
            m23 = -0.20756f,
            m30 = 0.00000f,
            m31 = 0.00000f,
            m32 = 0.00000f,
            m33 = 1.00000f
        };

        modelInKinect = new Matrix4x4
        {
            m00 = -0.00692f,
            m01 = -0.00014f,
            m02 = -0.16820f,
            m03 = -0.05479f,
            m10 = 0.24688f,
            m11 = 0.00001f,
            m12 = -0.00501f,
            m13 = -0.08454f,
            m20 = 0.00300f,
            m21 = -0.00099f,
            m22 = 0.02414f,
            m23 = 1.06214f,
            m30 = 0.00000f,
            m31 = 0.00000f,
            m32 = 0.00000f,
            m33 = 1.00000f
        };

        targetObserver = imageTarget.GetComponent<ObserverBehaviour>();
        if (targetObserver == null)
        {
            Debug.LogError("Image Target does not have ObserverBehaviour attached.");
        }
    }

    void Update()
    {
        if (targetObserver == null) return;

        if (targetObserver.TargetStatus.Status == Status.TRACKED)
        {
            if (!isTracking)
            {
                isTracking = true;
                Debug.Log("Image Target detected. Starting to track...");
                Renderer modelRenderer = model.GetComponent<Renderer>();
                if (modelRenderer != null) modelRenderer.enabled = true;
            }

            UpdateModelTransform();
        }
        else
        {
            if (isTracking)
            {
                isTracking = false;
                Debug.Log("Image Target is no longer tracked. Stopping updates.");
                Renderer modelRenderer = model.GetComponent<Renderer>();
                if (modelRenderer != null) modelRenderer.enabled = false;
            }
        }
    }

    private void UpdateModelTransform()
    {
        Matrix4x4 marker2holo = arCamera.transform.worldToLocalMatrix * imageTarget.transform.localToWorldMatrix;
        Matrix4x4 kinect2holo = marker2holo * kinect2marker;

        Matrix4x4 modelInHolo = kinect2holo * modelInKinect;

        Vector3 scale = new Vector3(
            modelInHolo.GetColumn(0).magnitude,
            modelInHolo.GetColumn(1).magnitude,
            modelInHolo.GetColumn(2).magnitude
        );

        model.transform.position = modelInHolo.GetPosition();
        model.transform.rotation = modelInHolo.rotation;
        model.transform.localScale = scale;

        Debug.Log($"model position is {model.transform.position}");
        Debug.Log($"marker position is {imageTarget.transform.position}");
    }
}
