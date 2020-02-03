using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class RectTest : MonoBehaviour
{
    public float Density = 1.0f;
    public float CellSize = 10.0f;
    public float MaxCellOffset = 0.5f;
    [Header("Cell1")]
    public Vector2 CellPos1 = new Vector2(0,0);
    public Vector4 MassRect1 = new Vector4( -5, -5, 5, 5 );
    public float MassValue1 = 1.0f;
    public Vector2 Vel1 = Vector2.zero;
    [Header("Cell2")]
    public Vector2 CellPos2 = new Vector2(-10,0);
    public Vector4 MassRect2 = new Vector4( -5, -5, 5, 5 );
    public float MassValue2 = 1.0f;
    public Vector2 Vel2 = Vector2.zero;

    public float DeltaTime
    {
        get
        {
            float maxVel = Mathf.Max(Vel1.magnitude,Vel2.magnitude);
            if (maxVel > 0)
            {
                return CellSize / maxVel * MaxCellOffset;
            }
            else
            {
                return 1.0f;
            }
        }
    }

    public Vector4 Bounds
    {
        get
        {
            float xInf = CellPos1.x - CellSize / 2;
            float yInf = CellPos1.y - CellSize / 2;
            float xSup = CellPos1.x + CellSize / 2;
            float ySup = CellPos1.y + CellSize / 2;
            
            xInf = Mathf.Min( xInf, CellPos2.x - CellSize / 2);
            yInf = Mathf.Min( yInf, CellPos2.y - CellSize / 2);
            xSup = Mathf.Max( xSup, CellPos2.x + CellSize / 2);
            ySup = Mathf.Max( ySup, CellPos2.y + CellSize / 2);
            
            return new Vector4( xInf, yInf, xSup, ySup );
        }
    }
    
    public Vector4 AABBIntersection1 { get; private set; }
    public Vector4 AABBIntersection2 { get; private set; }
    public float AABBIntersectionMass1 { get; private set; }
    public float AABBIntersectionMass2 { get; private set; }
    public float ExpansionVel1 { get; private set; }
    public float ExpansionVel2 { get; private set; }

    Vector4 IntersectAABBs(Vector4 aabb1, Vector4 aabb2)
    {
        float xInf1 = aabb1.x;
        float yInf1 = aabb1.y;
        float xSup1 = aabb1.z;
        float ySup1 = aabb1.w;
        float xInf2 = aabb2.x;
        float yInf2 = aabb2.y;
        float xSup2 = aabb2.z;
        float ySup2 = aabb2.w;
        
        return new Vector4
        (
            Mathf.Min( Mathf.Max(xInf1,xInf2), xSup2 ),
            Mathf.Min( Mathf.Max(yInf1,yInf2), ySup2 ),
            Mathf.Max( Mathf.Min(xSup1,xSup2), xInf2 ),
            Mathf.Max( Mathf.Min(ySup1,ySup2), yInf2 )
        );
    }

    Vector4 FitToBounds(Vector4 rect, Vector4 bounds)
    {
        if (rect.z - rect.x > bounds.z - bounds.x)
        {
            rect.x = bounds.x;
            rect.z = bounds.z;
        }
        else if (rect.x < bounds.x)
        {
            float offset = bounds.x - rect.x;
            rect.x += offset;
            rect.z += offset;
        }
        else if (rect.z > bounds.z)
        {
            float offset = bounds.z - rect.z;
            rect.x += offset;
            rect.z += offset;
        }
        
        if (rect.w - rect.y > bounds.w - bounds.y)
        {
            rect.y = bounds.y;
            rect.w = bounds.w;
        }
        else if (rect.y < bounds.y)
        {
            float offset = bounds.y - rect.y;
            rect.y += offset;
            rect.w += offset;
        }
        else if (rect.w > bounds.w)
        {
            float offset = bounds.w - rect.w;
            rect.y += offset;
            rect.w += offset;
        }

        return rect;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.gray;
        Gizmos.DrawWireCube(new Vector3(CellPos1.x, 0, CellPos1.y), new Vector3(CellSize, 0, CellSize));
        Gizmos.DrawWireCube(new Vector3(CellPos2.x, 0, CellPos2.y), new Vector3(CellSize, 0, CellSize));

        Vector2 ExtentOfMass1 = new Vector2((MassRect1.z - MassRect1.x), (MassRect1.w - MassRect1.y));
        Vector2 CenterOfMass1 =
            CellPos1 + new Vector2(MassRect1.x + ExtentOfMass1.x / 2, MassRect1.y + ExtentOfMass1.y / 2);

        Gizmos.color = Color.Lerp(Color.gray, Color.yellow, 0.66f);
        Gizmos.DrawWireCube(new Vector3(CenterOfMass1.x, 0.0f, CenterOfMass1.y),
            new Vector3(ExtentOfMass1.x, 0, ExtentOfMass1.y));
        Gizmos.color = Color.Lerp(Color.gray, Color.red, 0.33f);
        Gizmos.DrawLine(new Vector3(CellPos1.x, 0, CellPos1.y),
            new Vector3(CellPos1.x + Vel1.x, 0, CellPos1.y + Vel1.y));
        Gizmos.DrawSphere(new Vector3(CellPos1.x, 0, CellPos1.y), CellSize / 50);

        Vector2 ExtentOfMass2 = new Vector2((MassRect2.z - MassRect2.x), (MassRect2.w - MassRect2.y));
        Vector2 CenterOfMass2 =
            CellPos2 + new Vector2(MassRect2.x + ExtentOfMass2.x / 2, MassRect2.y + ExtentOfMass2.y / 2);

        Gizmos.color = Color.Lerp(Color.gray, Color.green, 0.66f);
        Gizmos.DrawWireCube(new Vector3(CenterOfMass2.x, 0.0f, CenterOfMass2.y),
            new Vector3(ExtentOfMass2.x, 0, ExtentOfMass2.y));
        Gizmos.color = Color.Lerp(Color.gray, Color.red, 0.33f);
        Gizmos.DrawLine(new Vector3(CellPos2.x, 0, CellPos2.y),
            new Vector3(CellPos2.x + Vel2.x, 0, CellPos2.y + Vel2.y));
        Gizmos.DrawSphere(new Vector3(CellPos2.x, 0, CellPos2.y), CellSize / 50);

        // expansion velocities

        float cellMassRectArea1 = (MassRect1.z - MassRect1.x) * (MassRect1.w - MassRect1.y);
        ExpansionVel1 = MassValue1 * Density / cellMassRectArea1;
        float cellMassRectArea2 = (MassRect2.z - MassRect2.x) * (MassRect2.w - MassRect2.y);
        ExpansionVel2 = MassValue2 * Density / cellMassRectArea2;

        // mass rects with offset

        Vector2 MassRectOffset1 = Vel1 * DeltaTime;

        Gizmos.color = Color.Lerp(Color.gray, Color.yellow, 0.33f);
        Gizmos.DrawWireCube(new Vector3(CenterOfMass1.x + MassRectOffset1.x, 0.0f, CenterOfMass1.y + MassRectOffset1.y),
            new Vector3(ExtentOfMass1.x, 0, ExtentOfMass1.y));

        Vector2 MassRectOffset2 = Vel2 * DeltaTime;

        Gizmos.color = Color.Lerp(Color.gray, Color.green, 0.33f);
        Gizmos.DrawWireCube(new Vector3(CenterOfMass2.x + MassRectOffset2.x, 0.0f, CenterOfMass2.y + MassRectOffset2.y),
            new Vector3(ExtentOfMass2.x, 0, ExtentOfMass2.y));

        // intersect <mass rect with offset #1> and <cell #1>

        Vector4 aabbCell1 = new Vector4(CellPos1.x - CellSize / 2, CellPos1.y - CellSize / 2, CellPos1.x + CellSize / 2,
            CellPos1.y + CellSize / 2);

        Vector4 aabbMassRectWidthOffset1 = new Vector4
        (
            CenterOfMass1.x + MassRectOffset1.x - ExtentOfMass1.x / 2,
            CenterOfMass1.y + MassRectOffset1.y - ExtentOfMass1.y / 2,
            CenterOfMass1.x + MassRectOffset1.x + ExtentOfMass1.x / 2,
            CenterOfMass1.y + MassRectOffset1.y + ExtentOfMass1.y / 2
        );
        AABBIntersection1 = IntersectAABBs(aabbMassRectWidthOffset1, aabbCell1);

        Gizmos.color = Color.Lerp(Color.gray, Color.yellow, 0.99f);
        Gizmos.DrawWireCube(
            new Vector3(AABBIntersection1.x + (AABBIntersection1.z - AABBIntersection1.x) / 2, 0.0f,
                AABBIntersection1.y + (AABBIntersection1.w - AABBIntersection1.y) / 2),
            new Vector3((AABBIntersection1.z - AABBIntersection1.x), 0, (AABBIntersection1.w - AABBIntersection1.y))
        );

        // intersect <mass rect with offset #2> and <cell #1>

        Vector4 aabbMassRectWidthOffset2 = new Vector4
        (
            CenterOfMass2.x + MassRectOffset2.x - ExtentOfMass2.x / 2,
            CenterOfMass2.y + MassRectOffset2.y - ExtentOfMass2.y / 2,
            CenterOfMass2.x + MassRectOffset2.x + ExtentOfMass2.x / 2,
            CenterOfMass2.y + MassRectOffset2.y + ExtentOfMass2.y / 2
        );
        AABBIntersection2 = IntersectAABBs(aabbMassRectWidthOffset2, aabbCell1);

        Gizmos.color = Color.Lerp(Color.gray, Color.green, 0.99f);
        Gizmos.DrawWireCube(
            new Vector3(AABBIntersection2.x + (AABBIntersection2.z - AABBIntersection2.x) / 2, 0.0f,
                AABBIntersection2.y + (AABBIntersection2.w - AABBIntersection2.y) / 2),
            new Vector3((AABBIntersection2.z - AABBIntersection2.x), 0, (AABBIntersection2.w - AABBIntersection2.y))
        );

        // compute masses for rects

        float massRectArea1 = (AABBIntersection1.z - AABBIntersection1.x) * (AABBIntersection1.w - AABBIntersection1.y);
        float massRectArea2 = (AABBIntersection2.z - AABBIntersection2.x) * (AABBIntersection2.w - AABBIntersection2.y);
        AABBIntersectionMass1 = MassValue1 * massRectArea1 / cellMassRectArea1;
        AABBIntersectionMass2 = MassValue2 * massRectArea2 / cellMassRectArea2;

        // compute result mass rect of cell #1

        float massRatio = AABBIntersectionMass2 / AABBIntersectionMass1; 
        
        float resultMassRectArea = massRectArea1 + massRectArea2 * massRatio;

        Vector2 aabbResultCenter = new Vector2
        (
            AABBIntersection1.x + (AABBIntersection1.z - AABBIntersection1.x) / 2,
            AABBIntersection1.y + (AABBIntersection1.w - AABBIntersection1.y) / 2
        );
        Vector2 aabbResultExtents = new Vector2
        (
            (AABBIntersection1.z - AABBIntersection1.x),
            (AABBIntersection1.w - AABBIntersection1.y)
        );

        aabbResultExtents *= Mathf.Sqrt((resultMassRectArea) / (massRectArea1));

        Vector4 aabbResult = new Vector4
        (
            aabbResultCenter.x - aabbResultExtents.x / 2,
            aabbResultCenter.y - aabbResultExtents.y / 2,
            aabbResultCenter.x + aabbResultExtents.x / 2,
            aabbResultCenter.y + aabbResultExtents.y / 2
        );

        aabbResult = FitToBounds(aabbResult, new Vector4(CellPos1.x-CellSize/2,CellPos1.y-CellSize/2,CellPos1.x+CellSize/2,CellPos1.y+CellSize/2));
        
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube( 
            new Vector3(aabbResult.x+(aabbResult.z-aabbResult.x)/2,0.0f,aabbResult.y+(aabbResult.w-aabbResult.y)/2), 
            new Vector3((aabbResult.z-aabbResult.x),0,(aabbResult.w-aabbResult.y)) 
        );
    }
}
