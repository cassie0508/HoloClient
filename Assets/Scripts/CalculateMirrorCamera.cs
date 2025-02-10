using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CalculateMirrorCamera : MonoBehaviour
{
    public GameObject Cam;

    void Start()
    {
        Matrix4x4 CamToKinect = Matrix4x4.TRS(Cam.transform.position, Cam.transform.rotation, Vector3.one);
        Debug.Log(CamToKinect);
    }

}
