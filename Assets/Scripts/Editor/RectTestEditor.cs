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

        Handles.Label
        (
            new Vector3(xInf, 0, yInf),
            "DeltaTime: " + rectTest.DeltaTime.ToString("F3") + "\n" +
            "ExpansionVel1: " + rectTest.ExpansionVel1.ToString("F3") + "\n" +
            "ExpansionVel2: " + rectTest.ExpansionVel2.ToString("F3")
        );
        
        Vector4 aabbIntersection1 = rectTest.AABBIntersection1;
        float xAABBCenter1 = aabbIntersection1.x + (aabbIntersection1.z - aabbIntersection1.x) / 2;
        float yAABBCenter1 = aabbIntersection1.y + (aabbIntersection1.w - aabbIntersection1.y) / 2;

        Handles.Label
        (
            new Vector3(xAABBCenter1, 0, yAABBCenter1),
            rectTest.AABBIntersectionMass1.ToString("F3")
        );
        
        Vector4 aabbIntersection2 = rectTest.AABBIntersection2;
        float xAABBCenter2 = aabbIntersection2.x + (aabbIntersection2.z - aabbIntersection2.x) / 2;
        float yAABBCenter2 = aabbIntersection2.y + (aabbIntersection2.w - aabbIntersection2.y) / 2;

        Handles.Label
        (
            new Vector3(xAABBCenter2, 0, yAABBCenter2),
            rectTest.AABBIntersectionMass2.ToString("F3")
        );
    }
}
