using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public partial class CellularGravity : MonoBehaviour
{
    const int GPUGroupSize = 128;

    private int _computeGravityForceWithSAT;
    private int _initMassSAT;
    private int _transposeMassSAT;
    private int _computeMassSAT;
    private int _computeRowStats;
    private int _cleanupMassPropagationBuffer;
    private int _massPropagationPrepass;
    private int _massPropagationPass;

    private void FindKernels(ComputeShader computeShader)
    {
        _computeGravityForceWithSAT = computeShader.FindKernel("ComputeGravityForceWithSAT");
        _initMassSAT = computeShader.FindKernel("InitMassSAT");
        _transposeMassSAT = computeShader.FindKernel("TransposeMassSAT");
        _computeMassSAT = computeShader.FindKernel("ComputeMassSAT");
        _computeRowStats = computeShader.FindKernel("ComputeRowStats");
        _cleanupMassPropagationBuffer = computeShader.FindKernel("CleanupMassPropagationBuffer"); 
        _massPropagationPrepass = computeShader.FindKernel("MassPropagationPrepass");
        _massPropagationPass = computeShader.FindKernel("MassPropagationPass");
    }

    private static void Swap(ref ComputeBuffer cbRef0, ref ComputeBuffer cbRef1)
    {
        ComputeBuffer temp = cbRef0;
        cbRef0 = cbRef1;
        cbRef1 = temp;
    }

    private void Initialize(Texture2D massTexture)
    {
        var massPixels = massTexture.GetPixels();

        int numCells = _cells.Length;
        for (int i = 0; i < numCells; i++)
        {
            int y = i / _width;
            int x = i - y * _width;

            Vector2 c = new Vector2(_width / 2 * CellSize + CellSize / 2, _height / 2 * CellSize + CellSize / 2);
            Vector2 p = new Vector2(x * CellSize + CellSize / 2, y * CellSize + CellSize / 2);
            _cells[i].vel = (c - p).normalized * (_width / 81.0f) * Random.Range(InitialVelocityBias.x,InitialVelocityBias.y);
            _cells[i].vel = new Vector2( _cells[i].vel.y, -_cells[i].vel.x );
            _cells[i].vel += Vector2.Lerp( Vector2.zero, (c-p).normalized, Vector2.Distance(c,p) / (_width / 2 * CellSize) );
            _cells[i].vel = Vector2.Lerp(Vector2.zero, _cells[i].vel, Vector2.Distance(c, p) / (_width / 2 * CellSize));

            Color pixel = massPixels[i];
            float lum = (pixel.r + pixel.g + pixel.b) / 3.0f;  
            _cells[i].mass = InitialMassMultiplier * lum * Random.Range(InitialMassBias.x,InitialMassBias.y);
        }

        _inCellBuffer.SetData(_cells);
        _outCellBuffer.SetData(_cells);
    }

    private void ComputeMassSAT()
    {
        int numberOfInitMassSATGroups = Mathf.CeilToInt((float) (_cells.Length) / GPUGroupSize);
        int numberOfComputeMassSATGroups = Mathf.CeilToInt((float) (_height) / GPUGroupSize);
        
        _computeShader.SetInt("width", _width);
        _computeShader.SetInt("height", _height);
        _computeShader.SetFloat("cellSize", CellSize);
        
        _computeShader.SetBuffer(_initMassSAT, "inCellBuffer", _inCellBuffer);
        _computeShader.SetBuffer(_initMassSAT, "outMassSATBuffer", _outMassSATBuffer);
        _computeShader.Dispatch(_initMassSAT, numberOfInitMassSATGroups, 1, 1);
        Swap(ref _outMassSATBuffer, ref _inMassSATBuffer);
        
        _computeShader.SetBuffer(_computeMassSAT, "inMassSATBuffer", _inMassSATBuffer);
        _computeShader.SetBuffer(_computeMassSAT, "outMassSATBuffer", _outMassSATBuffer);
        _computeShader.Dispatch(_computeMassSAT, numberOfComputeMassSATGroups, 1, 1);
        Swap(ref _outMassSATBuffer, ref _inMassSATBuffer);
        
        _computeShader.SetBuffer(_transposeMassSAT, "inMassSATBuffer", _inMassSATBuffer);
        _computeShader.SetBuffer(_transposeMassSAT, "outMassSATBuffer", _outMassSATBuffer);
        _computeShader.Dispatch(_transposeMassSAT, numberOfInitMassSATGroups, 1, 1);
        Swap(ref _outMassSATBuffer, ref _inMassSATBuffer);
        
        _computeShader.SetBuffer(_computeMassSAT, "inMassSATBuffer", _inMassSATBuffer);
        _computeShader.SetBuffer(_computeMassSAT, "outMassSATBuffer", _outMassSATBuffer);
        _computeShader.Dispatch(_computeMassSAT, numberOfComputeMassSATGroups, 1, 1);
        Swap(ref _outMassSATBuffer, ref _inMassSATBuffer);
        
        _computeShader.SetBuffer(_transposeMassSAT, "inMassSATBuffer", _inMassSATBuffer);
        _computeShader.SetBuffer(_transposeMassSAT, "outMassSATBuffer", _outMassSATBuffer);
        _computeShader.Dispatch(_transposeMassSAT, numberOfInitMassSATGroups, 1, 1);
        Swap(ref _outMassSATBuffer, ref _inMassSATBuffer);

        /*
        Vector4[] sat = new Vector4[_width * _height];
        _inMassSATBuffer.GetData( sat );
        
        const int TestCount = 1;
        for (int test = 0; test < TestCount; test++)
        {
            int xMin = Random.Range(0, _width / 2);
            int yMin = Random.Range(0, _height / 2);
            int xMax = xMin + Random.Range(1, _width / 2 - 1);
            int yMax = yMin + Random.Range(1, _height / 2 - 1);
            
            //xMin = 7;
            //yMin = 7;
            //xMax = 10;
            //yMax = 10;

            Vector4 satMaxMax = sat[yMax * _width + xMax];
            Vector4 satMaxMin = sat[yMax * _width + xMin];
            Vector4 satMinMax = sat[yMin * _width + xMax];
            Vector4 satMinMin = sat[yMin * _width + xMin];
            
            Vector4 satSample = satMaxMax - satMaxMin - satMinMax + satMinMin;
            
            int divisor = (xMax - xMin) * (yMax - yMin);
            Vector2 pos = new Vector2(satSample.z,satSample.w);
            float weight = satSample.y;
            Vector2 weightedPos = pos / weight;
			
            Vector2 pInf = new Vector2( (xMin+1) * CellSize + CellSize / 2, (yMin+1) * CellSize + CellSize / 2 );
            Vector2 pSup = new Vector2( xMax * CellSize + CellSize / 2, yMax * CellSize + CellSize / 2 );
            Vector2 precise = pInf + (pSup - pInf) / 2;
			
            Debug.Log( "[" + (xMin+1) + "," + (yMin+1) + "," + xMax + "," + yMax + "] sat: " + satSample + " precise: " + precise + " weighted: " + weightedPos );
        }
        */
    }

    private void SimulateGPU()
    {
        ComputeMassSAT();

        int numberOfCellGroups = Mathf.CeilToInt((float) (_cells.Length) / GPUGroupSize);
        int numberOfRowStatsGroups = Mathf.CeilToInt((float) (_rowStats.Length) / GPUGroupSize);
        
        _computeShader.SetFloat("gravity", Gravity);
        _computeShader.SetFloat("cellSize", CellSize);
        _computeShader.SetFloat("density", Density);
        _computeShader.SetInt("width", _width);
        _computeShader.SetInt("height", _height);
        
        _computeShader.SetBuffer(_computeRowStats, "inCellBuffer", _inCellBuffer);
        _computeShader.SetBuffer(_computeRowStats, "outRowStatsBuffer", _outRowStatsBuffer);
        _computeShader.Dispatch(_computeRowStats, numberOfRowStatsGroups, 1, 1);

        _outRowStatsBuffer.GetData(_rowStats);

        float maxMass = 0.0f;
        float maxVel = 0.0f;
        float totalMass = 0.0f;
        for (int i = 0; i < _rowStats.Length; i++)
        {
            maxMass = Mathf.Max(maxMass, _rowStats[i].maxMass);
            maxVel = Mathf.Max(maxVel, _rowStats[i].maxVel);
            totalMass += _rowStats[i].totalMass;
        }

        _computeShader.SetBuffer(_computeGravityForceWithSAT, "inOutCellBuffer", _inCellBuffer);
        _computeShader.SetBuffer(_computeGravityForceWithSAT, "inOutMassSATBuffer", _inMassSATBuffer);
        _computeShader.Dispatch(_computeGravityForceWithSAT, numberOfCellGroups, 1, 1);

        float maxExpansionVel = maxMass * Density / (CellSize * CellSize);
        float deltaTime = (maxVel > 0) ? (CellSize / maxVel * MaxCellOffset) : Time.fixedDeltaTime;
        deltaTime = Mathf.Min(deltaTime, (CellSize / maxExpansionVel * MaxCellOffset));
        if (deltaTime > MaxDeltaTime) deltaTime = MaxDeltaTime;
        DeltaTime.text = deltaTime.ToString("F5");
        TotalMass.text = totalMass.ToString("F5");
        
        _computeShader.SetFloat("deltaTime", deltaTime);

        // momentum transfer with local expansion
        
        _computeShader.SetInt( "numMassPropagationIndices", NumMassPropagationIndices );
        _computeShader.SetBuffer(_cleanupMassPropagationBuffer, "inOutMassPropagationBuffer", _inOutMassPropagationBuffer);
        _computeShader.Dispatch(_cleanupMassPropagationBuffer, numberOfCellGroups, 1, 1);
        
        _computeShader.SetBuffer(_massPropagationPrepass, "inOutMassPropagationBuffer", _inOutMassPropagationBuffer);
        _computeShader.SetBuffer(_massPropagationPrepass, "inOutCellRectBuffer", _inOutCellRectBuffer);
        _computeShader.SetBuffer(_massPropagationPrepass, "inCellBuffer", _inCellBuffer);
        _computeShader.Dispatch(_massPropagationPrepass, numberOfCellGroups, 1, 1);
        

        _computeShader.SetBuffer(_massPropagationPass, "inOutMassPropagationBuffer", _inOutMassPropagationBuffer);
        _computeShader.SetBuffer(_massPropagationPass, "inOutCellRectBuffer", _inOutCellRectBuffer);
        _computeShader.SetBuffer(_massPropagationPass, "inCellBuffer", _inCellBuffer);
        _computeShader.SetBuffer(_massPropagationPass, "outCellBuffer", _outCellBuffer);
        _computeShader.Dispatch(_massPropagationPass, numberOfCellGroups, 1, 1);

        Swap(ref _inCellBuffer, ref _outCellBuffer);
    }
}

