using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class PBM_Observer : MonoBehaviour
{
    public static PBM_Observer Instance = null;
    [Header("Default Setting\nAccess individual settings with the dictionary \"PBMs\".")]
    public bool Cropping;
    [Range(0, 1)]
    public float CropSize = 0.5f;
    [Range(0, 1)]
    public float Transparency = 1;
    [Range(0, 0.5f)]
    public float BorderSize = 0.01f;

    [Space]
    public Texture2D BorderTexture;
    public Texture2D MirrorSpecular;

    [Space]
    public List<Camera> CapturingCameras;
    private int NumCapturingCameras = 0;
    private List<Camera> CapturingCameraMemory = new List<Camera>();
    public Dictionary<PBM_CaptureCamera, PBM> PBMs;
    private Camera ObserverCam;

    // PBM variables
    public class PBM
    {
        public Camera SourceCamera;
        public GameObject ImageQuad;
        public Mesh ImageQuadMesh;
        public Material ImageMat;
        public MeshRenderer ImageRenderer;
        public RenderTexture Texture;
        [Header("Cropping and Transparency")]
        public Material CropAndTransparency;
        public bool EnableCropping = true;
        public float CropSize = 0.5f;
        [Range(0, 1)]
        public float Transparency = 1;
        [Range(0, 0.5f)]
        public float BorderSize = 0.002f;
        public PBM()
        {
            CropAndTransparency = new Material(Shader.Find("PBM/CropTransparent"));
        }
        public void DestroyContent()
        {
            Destroy(ImageQuad);
            Destroy(ImageQuadMesh);
            Destroy(ImageMat);
            Destroy(ImageRenderer);
            Destroy(Texture);
        }
    }

    private void Awake()
    {
        Instance = this;

        ObserverCam = GetComponent<Camera>();
        PBMs = new Dictionary<PBM_CaptureCamera, PBM>();

        if (BorderTexture == null)
            BorderTexture = Resources.Load("PBM/PBM_MirrorFrame") as Texture2D;
        if (MirrorSpecular == null)
            MirrorSpecular = Resources.Load("PBM/PBM_MirrorSpecular") as Texture2D;

    }

    private void LateUpdate()
    {
        if(NumCapturingCameras != CapturingCameras.Count)
        {
            
            foreach (var c in CapturingCameras)
            {
                var capturer = c.GetComponent<PBM_CaptureCamera>();
                if (!capturer)
                    capturer = c.gameObject.AddComponent<PBM_CaptureCamera>();
                RegisterCapturer(capturer);
            }
            foreach(var camera in CapturingCameraMemory)
            {
                if (!CapturingCameras.Contains(camera))
                {
                    var pbm_cam = camera.gameObject.GetComponent<PBM_CaptureCamera>();
                    if (pbm_cam)
                    {
                        if (PBMs.ContainsKey(pbm_cam))
                        {
                            PBMs[pbm_cam].DestroyContent();
                            PBMs.Remove(pbm_cam);
                        }
                            
                        Destroy(pbm_cam);
                    }
                        
                }
                    
            }
            CapturingCameraMemory = new List<Camera>(CapturingCameras);
            NumCapturingCameras = CapturingCameras.Count;
        }

        foreach (var pbm in PBMs)
        {
            if(pbm.Key!=null)
                UpdatePBM(pbm.Key);
        }
    }

    private void RegisterCapturer(PBM_CaptureCamera capturer)
    {
        PBM pbm = new PBM();
        pbm.SourceCamera = capturer.GetComponent<Camera>();
        pbm.ImageQuad = new GameObject();
        pbm.ImageQuad.name = "PBM_" + capturer.name;
        pbm.ImageQuad.transform.parent = ObserverCam.transform;
        pbm.ImageQuad.transform.localPosition = Vector3.zero;
        pbm.ImageQuad.transform.localRotation = Quaternion.identity;
        pbm.ImageQuad.transform.localScale = Vector3.one;
        pbm.ImageQuadMesh = new Mesh();
        pbm.ImageQuad.AddComponent<MeshFilter>().mesh = pbm.ImageQuadMesh;

        pbm.ImageMat = Instantiate(Resources.Load("PBM/PBMQuadMaterial") as Material);
        pbm.Texture = new RenderTexture(capturer.Width, capturer.Height, 24, RenderTextureFormat.ARGB32); //new Texture2D(capturer.Width, capturer.Height, TextureFormat.RGBA32, false);
        pbm.ImageMat.mainTexture = pbm.Texture;
        pbm.ImageRenderer = pbm.ImageQuad.AddComponent<MeshRenderer>();
        pbm.ImageRenderer.material = pbm.ImageMat;
        pbm.ImageQuad.layer = LayerMask.NameToLayer("PBM");

        pbm.EnableCropping = Cropping;
        pbm.CropSize = CropSize;
        pbm.Transparency = Transparency;
        pbm.BorderSize = 0.01f;

        PBMs[capturer] = pbm;
    }

    private void UpdatePBM(PBM_CaptureCamera capturer)
    {
        if(PBMs.ContainsKey(capturer))
        {
            var c_PBM = PBMs[capturer];

            if (!capturer.isActiveAndEnabled)
            {
                c_PBM.ImageQuad.SetActive(false);
                return;
            }

            var cameraMidPoint = (capturer.transform.position + ObserverCam.transform.position) / 2;

            var mirrorNormal = Vector3.Normalize(ObserverCam.transform.position - cameraMidPoint);

            capturer.UpdateValidAreaCompensationWithObserver(ObserverCam.transform.position);

            if (ComputePlaneCornerIntersection(capturer, cameraMidPoint, mirrorNormal,
                out var lt_world, out var rt_world, out var rb_world, out var lb_world, true))
            {
   
                if (Line3DIntersection(lt_world, rb_world, rt_world, lb_world, out var center))
                {

                    c_PBM.ImageQuad.SetActive(true);

                    c_PBM.CropAndTransparency.SetFloat("CompensationRatio", capturer.Ratio);
                    // Cropping
                    if (c_PBM.EnableCropping)
                    {
                        c_PBM.CropAndTransparency.SetFloat("_EnableCropping", 1);

                        var gazeRay = new Ray(ObserverCam.transform.position, ObserverCam.transform.forward);
                        Plane p = new Plane(mirrorNormal, cameraMidPoint);

                        if (p.Raycast(gazeRay, out float hitPlane))
                        {
                            var hitPosition = gazeRay.GetPoint(hitPlane);

                            // Project point onto top edge
                            var screenPoint = (Vector2)c_PBM.SourceCamera.WorldToViewportPoint(hitPosition);
                            var cropAndTransMat = c_PBM.CropAndTransparency;

                            cropAndTransMat.SetVector("uv_topleft", new Vector2(Mathf.Clamp01(screenPoint.x - c_PBM.CropSize), Mathf.Clamp01(screenPoint.y - c_PBM.CropSize)));
                            cropAndTransMat.SetVector("uv_topright", new Vector2(Mathf.Clamp01(screenPoint.x + c_PBM.CropSize), Mathf.Clamp01(screenPoint.y - c_PBM.CropSize)));
                            cropAndTransMat.SetVector("uv_bottomleft", new Vector2(Mathf.Clamp01(screenPoint.x - c_PBM.CropSize), Mathf.Clamp01(screenPoint.y + c_PBM.CropSize)));
                            cropAndTransMat.SetVector("uv_bottomright", new Vector2(Mathf.Clamp01(screenPoint.x + c_PBM.CropSize), Mathf.Clamp01(screenPoint.y + c_PBM.CropSize)));
                        }
                    }else
                    {
                        c_PBM.CropAndTransparency.SetFloat("_EnableCropping", 0);
                    }

                    c_PBM.CropAndTransparency.EnableKeyword("USE_MIRROR_SPECULAR_");

                    c_PBM.CropAndTransparency.SetFloat("MainTextureTransparency", c_PBM.Transparency);

                    c_PBM.CropAndTransparency.SetFloat("BorderSize", c_PBM.BorderSize);

                    c_PBM.CropAndTransparency.SetTexture("_MirrorFrameTex", BorderTexture);

                    c_PBM.CropAndTransparency.SetTexture("_MirrorSpecular", MirrorSpecular);


                    Graphics.Blit(capturer.ViewRenderTexture, c_PBM.Texture, c_PBM.CropAndTransparency);

                    var cam2Tranform = ObserverCam.transform.worldToLocalMatrix;
                    c_PBM.ImageQuadMesh.vertices = new Vector3[]
                    {
                            cam2Tranform.MultiplyPoint(lt_world),
                            cam2Tranform.MultiplyPoint(rt_world),
                            cam2Tranform.MultiplyPoint(rb_world),
                            cam2Tranform.MultiplyPoint(lb_world)
                    };

                    float lbd = (Vector3.Distance(lb_world, center) + Vector3.Distance(rt_world, center)) / Vector3.Distance(rt_world, center);
                    float rbd = (Vector3.Distance(rb_world, center) + Vector3.Distance(lt_world, center)) / Vector3.Distance(lt_world, center);
                    float rtb = (Vector3.Distance(rt_world, center) + Vector3.Distance(lb_world, center)) / Vector3.Distance(lb_world, center);
                    float ltb = (Vector3.Distance(lt_world, center) + Vector3.Distance(rb_world, center)) / Vector3.Distance(rb_world, center);

                    c_PBM.ImageQuadMesh.SetUVs(0, new Vector3[] { new Vector3(0, ltb, ltb), new Vector3(rtb, rtb, rtb), new Vector3(rbd, 0, rbd), new Vector3(0, 0, lbd) });

                    c_PBM.ImageQuadMesh.SetIndices(new int[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 }, MeshTopology.Triangles, 0);
                    c_PBM.ImageQuadMesh.RecalculateBounds();
                }
                else
                {
                    c_PBM.ImageQuad.SetActive(false);
                }

            }
            else
            {
                c_PBM.ImageQuad.SetActive(false);
            }
        }
        
    }

    private void PrintVector(string name, Vector3 v)
    {
        Debug.Log(name + ": " + v.x.ToString("0.000") + " " + v.y.ToString("0.000") + " " + v.z.ToString("0.000"));
    }
    public bool Line2DIntersection(Vector2 A1, Vector2 A2, Vector2 B1, Vector2 B2, out Vector2 intersection)
    {
        float tmp = (B2.x - B1.x) * (A2.y - A1.y) - (B2.y - B1.y) * (A2.x - A1.x);

        if (tmp == 0)
        {
            // No solution!
            intersection = Vector2.zero;
            return false;
        }

        float mu = ((A1.x - B1.x) * (A2.y - A1.y) - (A1.y - B1.y) * (A2.x - A1.x)) / tmp;

        intersection = new Vector2(
            B1.x + (B2.x - B1.x) * mu,
            B1.y + (B2.y - B1.y) * mu
        );
        return true;
    }

    // http://paulbourke.net/geometry/pointlineplane/
    public bool Line3DIntersection(Vector3 A1, Vector3 A2,
    Vector3 B1, Vector3 B2, out Vector3 intersection)
    {
        intersection = Vector3.zero;

        Vector3 p13 = A1 - B1;
        Vector3 p43 = B2 - B1;

        if (p43.sqrMagnitude < Mathf.Epsilon)
        {
            return false;
        }
        Vector3 p21 = A2 - A1;
        if (p21.sqrMagnitude < Mathf.Epsilon)
        {
            return false;
        }

        float d1343 = p13.x * p43.x + p13.y * p43.y + p13.z * p43.z;
        float d4321 = p43.x * p21.x + p43.y * p21.y + p43.z * p21.z;
        float d1321 = p13.x * p21.x + p13.y * p21.y + p13.z * p21.z;
        float d4343 = p43.x * p43.x + p43.y * p43.y + p43.z * p43.z;
        float d2121 = p21.x * p21.x + p21.y * p21.y + p21.z * p21.z;

        float denom = d2121 * d4343 - d4321 * d4321;
        if (Mathf.Abs(denom) < Mathf.Epsilon)
        {
            return false;
        }
        float numer = d1343 * d4321 - d1321 * d4343;

        float mua = numer / denom;
        float mub = (d1343 + d4321 * (mua)) / d4343;

        var MA = A1 + mua * p21;
        var MB = B1 + mub * p43;

        intersection = (MA + MB) / 2;

        return true;
    }

    private Vector3 ClosestPoint(Vector3 origin, Vector3 direction, Vector3 point)
    {
        return origin + Vector3.Project(point - origin, direction);
    }

    private Vector3 ClosestPointOnFirstRay(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        var t = (Vector3.Dot(c - a, b) * Vector3.Dot(d, d) +
                 Vector3.Dot(a - c, d) * Vector3.Dot(b, d)) /
                 (Vector3.Dot(b, b) * Vector3.Dot(d, d) - Vector3.Dot(b, d) * Vector3.Dot(b, d));

        var s = (Vector3.Dot(a - b, d) * Vector3.Dot(b, b) +
                 Vector3.Dot(c - a, b) * Vector3.Dot(b, d)) /
                 (Vector3.Dot(b, b) * Vector3.Dot(d, d) - Vector3.Dot(b, d) * Vector3.Dot(b, d));

        var onFirst = a + b * t;
        var onSecond = c + d * s;
        var mid = 0.5f * (onFirst + onSecond);

        return onFirst;
    }

    public bool ComputePlaneCornerIntersection(PBM_CaptureCamera capturer, Vector3 planeCenter, Vector3 planeNormal, 
        out Vector3 LT, out Vector3 RT, out Vector3 RB, out Vector3 LB, bool useWorldSpace = false)
    {
        var camPos = capturer.transform.position;
        float halfWidth = capturer.Width / 2;
        float halfHeight = capturer.Height / 2;
        float f = capturer.FocalLength;
        // max vertices
        var tlF = capturer.transform.TransformPoint(new Vector3(-halfWidth / f, halfHeight / f, 1));
        var trF = capturer.transform.TransformPoint(new Vector3(halfWidth / f, halfHeight / f, 1));
        var brF = capturer.transform.TransformPoint(new Vector3(halfWidth / f, -halfHeight / f, 1));
        var blF = capturer.transform.TransformPoint(new Vector3(-halfWidth / f, -halfHeight / f, 1));

        var plane = new Plane(planeNormal, planeCenter);

        var rayLT = new Ray(camPos, tlF - camPos);
        var rayRT = new Ray(camPos, trF - camPos);
        var rayRB = new Ray(camPos, brF - camPos);
        var rayLB = new Ray(camPos, blF - camPos);

        if (plane.Raycast(rayLT, out float hitlt) && plane.Raycast(rayRT, out float hitrt) && plane.Raycast(rayRB, out float hitrb) && plane.Raycast(rayLB, out float hitlb))
        {
            LT = rayLT.GetPoint(hitlt);
            RT = rayRT.GetPoint(hitrt);
            RB = rayRB.GetPoint(hitrb);
            LB = rayLB.GetPoint(hitlb);

            if (!useWorldSpace)
            {
                LT = capturer.transform.InverseTransformPoint(LT);
                RT = capturer.transform.InverseTransformPoint(RT);
                RB = capturer.transform.InverseTransformPoint(RB);
                LB = capturer.transform.InverseTransformPoint(LB);
            }
            return true;
        }
        else
        {
            LT = Vector3.zero;
            RT = Vector3.zero;
            RB = Vector3.zero;
            LB = Vector3.zero;
            return false;
        }

    }

    public static void SetGlobalScale(Transform t, Vector3 globalScale)
    {
        t.localScale = Vector3.one;
        t.localScale = new Vector3(globalScale.x / t.lossyScale.x, globalScale.y / t.lossyScale.y, globalScale.z / t.lossyScale.z);
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(PBM_Observer))]
public class PBM_ObserverEditor : Editor
{
    public override void OnInspectorGUI()
    {
        PBM_Observer myTarget = (PBM_Observer)target;

        DrawDefaultInspector();

        if (myTarget.PBMs != null)
        {
            foreach (var pbm in myTarget.PBMs)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(pbm.Value.SourceCamera.name);
                pbm.Value.EnableCropping = EditorGUILayout.Toggle("Cropping", pbm.Value.EnableCropping);
                if (pbm.Value.EnableCropping)
                {
                    EditorGUILayout.LabelField("CropSize");
                    pbm.Value.CropSize = EditorGUILayout.Slider(pbm.Value.CropSize, 0.01f, 1f);
                }
                EditorGUILayout.LabelField("Transparency");
                pbm.Value.Transparency = EditorGUILayout.Slider(pbm.Value.Transparency, 0.01f, 1);

                EditorGUILayout.LabelField("Border Size");
                pbm.Value.BorderSize = EditorGUILayout.Slider(pbm.Value.BorderSize, 0f, 0.5f);
            }
        }

    }
}
#endif