using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CellularGravityPrototype : MonoBehaviour
{
	public Text TotalMassText;
	public float Gravity = 9.8f;
	public Gradient MassToColorGradient;
		
	private Cell[] _cells = new Cell[0];
	private float _maxGradientMass = 0f;
	private float[] _massBuffer = new float[0];
	private Vector2[] _velocityBuffer = new Vector2[0];
	
	public float MaxGradientMass { get { return _maxGradientMass * 2; } }
	
	private static float GetRectOverlappingArea(Vector2 inf1, Vector2 sup1, Vector2 inf2, Vector2 sup2)
	{
		float left1 = inf1.x;
		float right1 = sup1.x;
		float top1 = sup1.y;
		float bottom1 = inf1.y;
		
		float left2 = inf2.x;
		float right2 = sup2.x;
		float top2 = sup2.y;
		float bottom2 = inf2.y;
		
		float xOverlap = Mathf.Max(0, Mathf.Min(right1, right2) - Mathf.Max(left1, left2));
		float yOverlap = Mathf.Max(0, Mathf.Min(top1, top2) - Mathf.Max(bottom1, bottom2));
		return xOverlap * yOverlap;
	}

	private static float GetRectOffsetArea(Vector2 inf, Vector2 sup, Vector2 offset)
	{
		return (sup.x - inf.x) * Mathf.Abs(offset.x) + (sup.y - inf.y) * Mathf.Abs(offset.y) - Mathf.Abs(offset.x * offset.y);
	}
	
	private void Awake ()
	{
		_cells = GetComponentsInChildren<Cell>();
		_massBuffer = new float[_cells.Length];
		_velocityBuffer = new Vector2[_cells.Length];

		for (int i = 0; i < _cells.Length; i++)
		{
			_maxGradientMass = Mathf.Max(_maxGradientMass, _cells[i].Mass);
		}
	}
	
	private void FixedUpdate () 
	{
		// integrate gravity force
		
		for (int i = 0; i < _cells.Length; i++)
		{
			Cell cellI = _cells[i];

			if (cellI.Mass > 0)
			{
				Vector2 force = Vector2.zero;

				for (int j = 0; j < _cells.Length; j++)
				{
					if (i != j)
					{
						Cell cellJ = _cells[j];
						Vector2 direction = cellJ.Pos - cellI.Pos;
						float r = direction.magnitude;
						direction *= 1.0f / r;
						force += direction * Gravity * cellI.Mass * cellJ.Mass / (r * r);
					}
				}

				Vector2 acceleration = force / cellI.Mass;
				_velocityBuffer[i] = cellI.Velocity + acceleration * Time.fixedDeltaTime;
			}
		}

		for (int i = 0; i < _cells.Length; i++)
		{
			_cells[i].Velocity = _velocityBuffer[i];
		}
		
		// integrate velocities

		for (int i = 0; i < _cells.Length; i++)
		{
			Cell cellI = _cells[i];
			
			Vector2 infI = cellI.Inf;
			Vector2 supI = cellI.Sup;
			
			_massBuffer[i] = cellI.Mass - cellI.Mass * GetRectOffsetArea(infI, supI, cellI.Velocity * Time.fixedDeltaTime) / cellI.Area;
			_massBuffer[i] = Mathf.Max(0f, _massBuffer[i]);
			_velocityBuffer[i] = _massBuffer[i] * cellI.Velocity;
			

			/*if (cellI.Mass > 0)
			{
				float prevMassVelocityMagnitude = cellI.Mass * cellI.Velocity.magnitude; 
				_massBuffer[i] = cellI.Mass - cellI.Mass * GetRectOffsetArea(infI, supI, cellI.Velocity * Time.fixedDeltaTime) / cellI.Area;
				_massBuffer[i] = Mathf.Max(0f, _massBuffer[i]);
				_velocityBuffer[i] = _massBuffer[i] * cellI.Velocity;
				if (_massBuffer[i] > 0)
				{
					_velocityBuffer[i] *= prevMassVelocityMagnitude / _velocityBuffer[i].magnitude;

					_velocityBuffer[i] *= 1.0f / _massBuffer[i];
					_velocityBuffer[i] = _velocityBuffer[i].normalized * Mathf.Min(_velocityBuffer[i].magnitude, cellI.Size );
					_velocityBuffer[i] *= _massBuffer[i];
				}
			}*/

			for (int j = 0; j < cellI.NeighbourCells.Length; j++)
			{
				Cell cellJ = cellI.NeighbourCells[j]; 
				Vector2 infJ = cellJ.Inf + cellJ.Velocity * Time.fixedDeltaTime;
				Vector2 supJ = cellJ.Sup + cellJ.Velocity * Time.fixedDeltaTime;
				float overlappingArea = GetRectOverlappingArea(infI, supI, infJ, supJ);
				if (overlappingArea > 0)
				{
					// no velocity dispersion
					
					float deltaMass = overlappingArea * cellJ.Mass / cellJ.Area;
					_massBuffer[i] += deltaMass;
					_velocityBuffer[i] += cellJ.Velocity * deltaMass;					
					
					// velocity dispersion
					/*
					const int NumDispersionSteps = 16;
					
					float densityI = cellI.Mass / cellI.Area;
					float densityJ = cellJ.Mass / cellJ.Area;
					float dispersionAngle = Mathf.Lerp(0f, 180f, Mathf.Clamp01(densityJ/densityI));
					
					float deltaMass = overlappingArea * cellJ.Mass / cellJ.Area;
					_cellMass[i] += deltaMass;

					for (float angle = -dispersionAngle; angle <= dispersionAngle; angle += (dispersionAngle * 2) / NumDispersionSteps)
					{
						Matrix4x4 m = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, angle), Vector3.one);

						Vector3 v0 = cellJ.Velocity.normalized;
						Vector3 v1 = m.MultiplyVector(v0);						

						float deltaMassVelocity = cellJ.Velocity.magnitude * deltaMass;

						_cellMassVelocity[i] += new Vector2(v1.x, v1.y) * deltaMassVelocity / NumDispersionSteps;
					}
					*/
				}
			}
		}

		float totalMass = 0.0f;

		for (int i = 0; i < _cells.Length; i++)
		{
			totalMass += _massBuffer[i];
			_cells[i].Mass = _massBuffer[i];
			_cells[i].Velocity = _velocityBuffer[i] / _massBuffer[i];
		}

		TotalMassText.text = totalMass.ToString("F6");
	}
}
