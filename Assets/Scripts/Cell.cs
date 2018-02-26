
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
    using UnityEditor; 
#endif

[ExecuteInEditMode]
public class Cell : MonoBehaviour
{
    [Header("Cell status")]
    public float Mass;
    public Vector2 Velocity;
    
    [Header("Cell graph")]
    public Cell[] NeighbourCells;
    
    [Header("Cell UI")]
    public Image ArrowImage;
    public Text MassText;
    public Text VelocityText;

    private RectTransform _rectTransform;
    public RectTransform RectTransform
    {
        get
        {
            if (!_rectTransform)
            {
                _rectTransform = GetComponent<RectTransform>();
            }
            return _rectTransform;
        }
    }

    public Vector2 Pos
    {
        get { return RectTransform.anchoredPosition; }        
    }

    public float Size
    {
        get { return Sup.x - Inf.x; }
    }

    public Vector2 Inf
    {
        get { return RectTransform.anchoredPosition - RectTransform.sizeDelta/2; }
    }

    public Vector2 Sup
    {
        get { return RectTransform.anchoredPosition + RectTransform.sizeDelta/2; }
    }

    public float Area
    {
        get { return RectTransform.sizeDelta.x * RectTransform.sizeDelta.y; }
    }

    private void Update()
    {
        /*if (Application.isPlaying)
        {
            CellularGravityPrototype cellularGravityPrototype = GetComponentInParent<CellularGravityPrototype>();
            float blend = Mathf.Clamp01(Mass / cellularGravityPrototype.MaxGradientMass);
            GetComponent<Image>().color = cellularGravityPrototype.MassToColorGradient.Evaluate(blend);

            ArrowImage.enabled = false;
            MassText.enabled = false;
            VelocityText.enabled = false;
            return;
        }*/

        if (Velocity.magnitude > Mathf.Epsilon)
        {
            ArrowImage.enabled = true;
            ArrowImage.transform.up = new Vector3(-Velocity.x, -Velocity.y, 0).normalized;
        }
        else
        {
            ArrowImage.transform.up = Vector3.up;
            ArrowImage.enabled = false;
        }
        MassText.transform.up = Vector3.up;
        MassText.text = Mass.ToString("F5");
        VelocityText.transform.up = Vector3.up;
        VelocityText.text = Velocity.x.ToString("F3") + ", " + Velocity.y.ToString("F3");
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (UnityEditor.Selection.activeGameObject == this.gameObject)
        {
            Gizmos.color = Color.red;
            for (int i = 0; i < NeighbourCells.Length; i++)
            {
                if (NeighbourCells[i])
                {
                    Gizmos.DrawLine( transform.position, NeighbourCells[i].transform.position );    
                }                
            }
        }
    }
#endif
}
