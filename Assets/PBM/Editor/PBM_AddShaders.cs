#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

public class PBM_AddShaders : IPreprocessBuildWithReport
{
    public int callbackOrder { get { return 0; } }

    public void OnPreprocessBuild(BuildReport report)
    {
        AddAlwaysIncludedShader("Unlit/Color");
        AddAlwaysIncludedShader("PBM/CropTransparent");
        AddAlwaysIncludedShader("PBM/Quadrilateral");
        AddAlwaysIncludedShader("PBM/ViewMerge");
    }

    public static void AddAlwaysIncludedShader(string shaderName)
    {
        var shader = Shader.Find(shaderName);
        if (shader == null)
            return;

        var graphicsSettingsObj = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
        var serializedObject = new SerializedObject(graphicsSettingsObj);
        var arrayProp = serializedObject.FindProperty("m_AlwaysIncludedShaders");
        bool hasShader = false;
        for (int i = 0; i < arrayProp.arraySize; ++i)
        {
            var arrayElem = arrayProp.GetArrayElementAtIndex(i);
            if (shader == arrayElem.objectReferenceValue)
            {
                hasShader = true;
                break;
            }
        }

        if (!hasShader)
        {
            int arrayIndex = arrayProp.arraySize;
            arrayProp.InsertArrayElementAtIndex(arrayIndex);
            var arrayElem = arrayProp.GetArrayElementAtIndex(arrayIndex);
            arrayElem.objectReferenceValue = shader;

            serializedObject.ApplyModifiedProperties();

            AssetDatabase.SaveAssets();
        }
    }


}

#endif
