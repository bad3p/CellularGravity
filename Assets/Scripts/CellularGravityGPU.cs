using System;
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
    private int _integrateVelocity;
    private int _momentumTransfer;
    private int _localExpansion;
    private int _initMassSAT;
    private int _transposeMassSAT;
    private int _computeMassSAT;
    private int _computeRowStats;

    private void FindKernels(ComputeShader computeShader)
    {
        _computeGravityForceWithSAT = computeShader.FindKernel("ComputeGravityForceWithSAT");
        _integrateVelocity = computeShader.FindKernel("IntegrateVelocity");
        _momentumTransfer = computeShader.FindKernel("MomentumTransfer");
        _localExpansion = computeShader.FindKernel("LocalExpansion");
        _initMassSAT = computeShader.FindKernel("InitMassSAT");
        _transposeMassSAT = computeShader.FindKernel("TransposeMassSAT");
        _computeMassSAT = computeShader.FindKernel("ComputeMassSAT");
        _computeRowStats = computeShader.FindKernel("ComputeRowStats");
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

            _cells[i].vel = Vector2.zero;
            _cells[i].mass = InitialMassMultiplier * massPixels[i].r;
            
            _cells[i].rect = new Vector4
            (
                x * CellSize, 
                y * CellSize,
                x * CellSize + CellSize, 
                y * CellSize + CellSize
            );
            /*
            float xInf = x * CellSize;
            float yInf = y * CellSize;
            float xSup = x * CellSize + CellSize;
            float ySup = y * CellSize + CellSize;

            xInf += Random.Range(0.0f, CellSize / 2);
            yInf += Random.Range(0.0f, CellSize / 2);
            xSup -= Random.Range(0.0f, CellSize / 2);
            ySup -= Random.Range(0.0f, CellSize / 2); 
            
            _cells[i].rect = new Vector4( xInf, yInf, xSup, ySup );*/
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
    }

    private float RectArea(Vector4 rect)
    {
        return (rect.z - rect.x) * (rect.w - rect.y);
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
        TotalMass.text = totalMass.ToString("F4");

        _computeShader.SetBuffer(_computeGravityForceWithSAT, "inOutCellBuffer", _inCellBuffer);
        _computeShader.SetBuffer(_computeGravityForceWithSAT, "inOutMassSATBuffer", _inMassSATBuffer);
        _computeShader.Dispatch(_computeGravityForceWithSAT, numberOfCellGroups, 1, 1);

        float maxExpansionVel = maxMass * Density / (CellSize * CellSize);
        float deltaTime = (maxVel > 0) ? (CellSize / maxVel * MaxCellOffset) : Time.fixedDeltaTime;
        deltaTime = Mathf.Min(deltaTime, (CellSize / maxExpansionVel * MaxCellOffset));
        if (deltaTime > MaxDeltaTime) deltaTime = MaxDeltaTime;
        DeltaTime.text = deltaTime.ToString("F5");

        _computeShader.SetFloat("deltaTime", deltaTime);
        _computeShader.SetBuffer(_integrateVelocity, "inOutCellBuffer", _inCellBuffer);
        _computeShader.Dispatch(_integrateVelocity, numberOfCellGroups, 1, 1);

        _computeShader.SetBuffer(_momentumTransfer, "inCellBuffer", _inCellBuffer);
        _computeShader.SetBuffer(_momentumTransfer, "outCellBuffer", _outCellBuffer);
        _computeShader.SetBuffer(_momentumTransfer, "outDebugCellBuffer", _outDebugCellBuffer);
        _computeShader.Dispatch(_momentumTransfer, numberOfCellGroups, 1, 1);
        /*
        _outDebugCellBuffer.GetData(_debugCells);
        */
        Swap(ref _inCellBuffer, ref _outCellBuffer);

        _computeShader.SetBuffer(_localExpansion, "inCellBuffer", _inCellBuffer);
        _computeShader.SetBuffer(_localExpansion, "outCellBuffer", _outCellBuffer);
        _computeShader.Dispatch(_localExpansion, numberOfCellGroups, 1, 1);

        Swap(ref _inCellBuffer, ref _outCellBuffer);
        /*
        for (int i = 0; i < _debugCells.Length; i++)
        {
            if (_debugCells[i].rect0.magnitude > 0 ||
                _debugCells[i].rect1.magnitude > 0 ||
                _debugCells[i].rect2.magnitude > 0 ||
                _debugCells[i].rect3.magnitude > 0 ||
                _debugCells[i].rect4.magnitude > 0 ||
                _debugCells[i].rect5.magnitude > 0 ||
                _debugCells[i].rect6.magnitude > 0 ||
                _debugCells[i].rect7.magnitude > 0)
            {
                string s = _debugCells[i].cellRect.ToString() + "\n";
                
                s += "MassRect0: " + _debugCells[i].massRect0.ToString() + " area: " + RectArea(_debugCells[i].massRect0).ToString("F4") + "\n";
                s += "MassRect1: " + _debugCells[i].massRect1.ToString() + " area: " + RectArea(_debugCells[i].massRect1).ToString("F4") + "\n";
                
                if (_debugCells[i].rect0.magnitude > 0)
                {
                    s += "Rect0: " + _debugCells[i].rect0.ToString() + " area: " + RectArea(_debugCells[i].rect0).ToString("F4") + "\n";
                }
                if (_debugCells[i].rect1.magnitude > 0)
                {
                    s += "Rect1: " + _debugCells[i].rect1.ToString() + " area: " + RectArea(_debugCells[i].rect1).ToString("F4")+ "\n";
                }
                if (_debugCells[i].rect2.magnitude > 0)
                {
                    s += "Rect2: " + _debugCells[i].rect2.ToString() + " area: " + RectArea(_debugCells[i].rect2).ToString("F4")+ "\n";
                }
                if (_debugCells[i].rect3.magnitude > 0)
                {
                    s += "Rect3: " + _debugCells[i].rect3.ToString() + " area: " + RectArea(_debugCells[i].rect3).ToString("F4")+ "\n";
                }
                if (_debugCells[i].rect4.magnitude > 0)
                {
                    s += "Rect4: " + _debugCells[i].rect4.ToString() + " area: " + RectArea(_debugCells[i].rect4).ToString("F4")+ "\n";
                }
                if (_debugCells[i].rect5.magnitude > 0)
                {
                    s += "Rect5: " + _debugCells[i].rect5.ToString() + " area: " + RectArea(_debugCells[i].rect5).ToString("F4")+ "\n";
                }
                if (_debugCells[i].rect6.magnitude > 0)
                {
                    s += "Rect6: " + _debugCells[i].rect6.ToString() + " area: " + RectArea(_debugCells[i].rect6).ToString("F4")+ "\n";
                }
                if (_debugCells[i].rect7.magnitude > 0)
                {
                    s += "Rect7: " + _debugCells[i].rect7.ToString() + " area: " + RectArea(_debugCells[i].rect7).ToString("F4")+ "\n";
                }
                Debug.Log(s);
            }
        }*/
    }
}

