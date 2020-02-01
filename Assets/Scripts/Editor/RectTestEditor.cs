using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RectTest))]
public class RectTestEditor : Editor
{
    void OnSceneGUI()
    {
        RectTest rectTest = (RectTest) target;
        if (rectTest == null)
        {
            return;
        }

        Vector4 rectTestBounds = rectTest.Bounds;
        float xInf = rectTestBounds.x;
        float yInf = rectTestBounds.y;
        float xSup = rectTestBounds.z;
        float ySup = rectTestBounds.w;
        
        Handles.color = Color.white;
        Handles.Label
        (
            new Vector3(xInf, 0, yInf),
            "DeltaTime: " +  rectTest.DeltaTime.ToString("F5")
        );
    }
}
