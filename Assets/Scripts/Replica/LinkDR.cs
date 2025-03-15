using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LinkDR : MonoBehaviour
{
    public static LinkDR instance;

    public Transform ROI;
    public Transform DR;
    public Material LineMat;

    private List<LineRenderer> links = new List<LineRenderer>();

    private void Awake()
    {
        instance = this;
    }

    public static void AddLink(Vector3 worldPosInROI)
    {
        if (!instance || instance.links == null) return;

        var localPosInROI = instance.ROI.InverseTransformPoint(worldPosInROI);
        var worldPosInDR = instance.DR.TransformPoint(localPosInROI);

        instance.links.Add(CreateLine(worldPosInROI, worldPosInDR));
    }


    private static LineRenderer CreateLine(Vector3 worldPos0, Vector3 worldPos1)
    {
        GameObject line = new GameObject();
        var lr = line.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.startWidth = 0.001f;
        lr.endWidth = 0.001f;
        lr.material = instance.LineMat;
        lr.SetPosition(0, worldPos0);
        lr.SetPosition(1, worldPos1);

        return lr;
    }

    public static void DeleteAllLines()
    {
        if (!instance || instance.links == null) return;

        foreach(var line in instance.links)
            Destroy(line.gameObject);
        instance.links.Clear();
    }
}
