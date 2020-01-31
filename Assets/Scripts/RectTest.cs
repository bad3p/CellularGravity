using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RectTest : MonoBehaviour
{
    public float CellSize = 10.0f;
    [Space]
    public Vector2 MassRectPos1 = new Vector2( 2.5f, -2.5f );
    public Vector2 MassRectSize1 = new Vector2(2.5f, 2.5f);
    public float MassValue1 = 1.0f;
    [Space]
    public Vector2 MassRectPos2 = new Vector2( 2.5f, -2.5f );
    public Vector2 MassRectSize2 = new Vector2(2.5f, 2.5f);
    public float MassValue2 = 1.0f;

    void OnDrawGizmos()
    {
        Gizmos.DrawWireCube( Vector3.zero, new Vector3(CellSize,0,CellSize) );
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube( new Vector3(MassRectPos1.x,0.0f,MassRectPos1.y), new Vector3(MassRectSize1.x,0,MassRectSize1.y) );
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube( new Vector3(MassRectPos2.x,0.0f,MassRectPos2.y), new Vector3(MassRectSize2.x,0,MassRectSize2.y) );

        float totalMassValue = (MassValue1 + MassValue2);
        Vector2 massRectPos3 = MassRectPos1 * MassValue1 / totalMassValue + MassRectPos2 * MassValue2 / totalMassValue;
        Vector2 massRectSize3 = MassRectSize1 * MassValue1 / totalMassValue + MassRectSize2 * MassValue2 / totalMassValue;
        
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube( new Vector3(massRectPos3.x,0.0f,massRectPos3.y), new Vector3(massRectSize3.x,0,massRectSize3.y) );
    }
}
