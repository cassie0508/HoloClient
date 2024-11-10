using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class PBM_CameraFrustum
{
    public bool Enabled = true;
    private bool _Enabled;

    public Color LineColor = Color.green;
    private Color _LineColor;

    [Min(0)]
    public float LineWidth = 0.002f;
    private float _LineWidth;

    [Header("Frustum Range"), Min(0)]
    public float MinDepth = 0.1f;
    [Min(0)]
    public float MaxDepth = 0.8f;

    private GameObject FrustumGO;
    private List<LineRenderer> FrustumLines;

    public bool isVisible
    {
        get { return _isVisible; }
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                if (FrustumGO) FrustumGO.SetActive(_isVisible);
            }
        }
    }
    private bool _isVisible = true;

    public void Create(int renderLayer, Transform parent = null)
    {
        isVisible = true;
        _LineColor = LineColor;
        _LineWidth = LineWidth;
        _Enabled = Enabled;

        FrustumGO = new GameObject
        {
            name = "CameraFrustum"
        };
        FrustumGO.transform.parent = parent;
        FrustumGO.transform.localPosition = Vector3.zero;
        FrustumGO.transform.localRotation = Quaternion.identity;

        FrustumLines = new List<LineRenderer>();
        Material lineMat = new Material(Shader.Find("Unlit/Color"));
        lineMat.color = LineColor;
        for (int i = 0; i < 12; i++)
        {
            var lineGO = new GameObject();
            lineGO.name = "frustumLine_" + i;
            lineGO.layer = renderLayer;
            lineGO.transform.parent = FrustumGO.transform;
            lineGO.transform.localPosition = Vector3.zero;
            lineGO.transform.localRotation = Quaternion.identity;
            var lineRenderer = lineGO.AddComponent<LineRenderer>();
            lineRenderer.sharedMaterial = lineMat;
            lineRenderer.positionCount = 2;
            lineRenderer.startWidth = LineWidth;
            lineRenderer.endWidth = LineWidth;
            lineRenderer.useWorldSpace = false;
            FrustumLines.Add(lineRenderer);
        }
        FrustumGO.SetActive(Enabled);
    }

    public void SetLineWidth(float width)
    {
        foreach (var line in FrustumLines)
        {
            line.startWidth = width;
            line.endWidth = width;
        }
    }

    public void SetLineColor(Color color)
    {
        foreach (var line in FrustumLines)
        {
            line.sharedMaterial.color = color;
        }
    }

    public void SetFrustumLines(Vector3 tlN, Vector3 trN, Vector3 brN, Vector3 blN, Vector3 tlF, Vector3 trF, Vector3 brF, Vector3 blF)
    {
        FrustumLines[0].SetPositions(new Vector3[] { tlN, trN });
        FrustumLines[1].SetPositions(new Vector3[] { trN, brN });
        FrustumLines[2].SetPositions(new Vector3[] { brN, blN });
        FrustumLines[3].SetPositions(new Vector3[] { blN, tlN });

        FrustumLines[4].SetPositions(new Vector3[] { tlF, trF });
        FrustumLines[5].SetPositions(new Vector3[] { trF, brF });
        FrustumLines[6].SetPositions(new Vector3[] { brF, blF });
        FrustumLines[7].SetPositions(new Vector3[] { blF, tlF });

        FrustumLines[8].SetPositions(new Vector3[] { tlN, tlF });
        FrustumLines[9].SetPositions(new Vector3[] { trN, trF });
        FrustumLines[10].SetPositions(new Vector3[] { brN, brF });
        FrustumLines[11].SetPositions(new Vector3[] { blN, blF });
    }

    public void UpdateFrustum(float f, float width, float height)
    {
        if(_Enabled != Enabled)
        {
            FrustumGO.SetActive(Enabled);
        }

        if(Enabled)
        {
            if (_LineWidth != LineWidth)
            {
                _LineWidth = LineWidth;
                SetLineWidth(_LineWidth);
            }

            if (_LineColor != LineColor)
            {
                _LineColor = LineColor;
                SetLineColor(_LineColor);
            }

            float halfWidth = width / 2;
            float halfHeight = height / 2;
            ComputeFrustumFromFocalLength(
                new Vector2(-halfWidth, halfHeight),
                new Vector2(halfWidth, halfHeight),
                new Vector2(halfWidth, -halfHeight),
                new Vector2(-halfWidth, -halfHeight),
                f);
        } 
    }

    private void ComputeFrustumFromFocalLength(Vector2 TopLeft, Vector2 TopRight, Vector2 BottomRight, Vector2 BottomLeft, float f)
    {
        // min vertices
        var tlN = new Vector3(TopLeft.x * MinDepth / f, TopLeft.y * MinDepth / f, MinDepth);
        var trN = new Vector3(TopRight.x * MinDepth / f, TopRight.y * MinDepth / f, MinDepth);
        var brN = new Vector3(BottomRight.x * MinDepth / f, BottomRight.y * MinDepth / f, MinDepth);
        var blN = new Vector3(BottomLeft.x * MinDepth / f, BottomLeft.y * MinDepth / f, MinDepth);

        // max vertices
        var tlF = new Vector3(TopLeft.x * MaxDepth / f, TopLeft.y * MaxDepth / f, MaxDepth);
        var trF = new Vector3(TopRight.x * MaxDepth / f, TopRight.y * MaxDepth / f, MaxDepth);
        var brF = new Vector3(BottomRight.x * MaxDepth / f, BottomRight.y * MaxDepth / f, MaxDepth);
        var blF = new Vector3(BottomLeft.x * MaxDepth / f, BottomLeft.y * MaxDepth / f, MaxDepth);

        SetFrustumLines(tlN, trN, brN, blN, tlF, trF, brF, blF);
    }

    ~PBM_CameraFrustum()
    {
        GameObject.Destroy(FrustumGO);
    }
}