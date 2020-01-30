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

    private void Initialize(Texture2D massTexture, Texture2D colorTexture)
    {
        var massPixels = massTexture.GetPixels();
        var colorPixels = colorTexture.GetPixels();

        int numCells = _cells.Length;
        for (int i = 0; i < numCells; i++)
        {
            int y = i / _width;
            int x = i - y * _width;

            _cells[i].vel = Vector2.zero;
            _cells[i].pos = new Vector2(x * CellSize + CellSize / 2, y * CellSize + CellSize / 2);
            _cells[i].mass = InitialMassMultiplier * massPixels[i].r;
            _cells[i].color = colorPixels[i];
        }

        for (int k = 1; k < _grids.Length; k++)
        {
            int widthK = _grids[k].width;
            int startK = _grids[k].start;
            int lengthK = _grids[k].length;
            float cellSizeK = CellSize * _width / widthK;

            for (int i = 0; i < lengthK; i++)
            {
                int y = i / widthK;
                int x = i - y * widthK;

                _nodes[i + startK].pos = new Vector2(x * cellSizeK + cellSizeK / 2, y * cellSizeK + cellSizeK / 2);
                _nodes[i + startK].mass = 0.0f;
                _nodes[i + startK].maxMass = 0.0f;
                _nodes[i + startK].maxVel = 0.0f;
            }
        }

        _inCellBuffer.SetData(_cells);
        _outCellBuffer.SetData(_cells);
        _nodeBuffer.SetData(_nodes);
        _gridBuffer.SetData(_grids);
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

    private void SimulateGPU()
    {
        ComputeMassSAT();

        int numberOfCellGroups = Mathf.CeilToInt((float) (_cells.Length) / GPUGroupSize);
        int numberOfRowStatsGroups = Mathf.CeilToInt((float) (_rowStats.Length) / GPUGroupSize);
        
        _computeShader.SetInt("numGrids", _grids.Length);
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
        for (int i = 0; i < _rowStats.Length; i++)
        {
            maxMass = Mathf.Max(maxMass, _rowStats[i].maxMass);
            maxVel = Mathf.Max(maxVel, _rowStats[i].maxVel);
        }

        _computeShader.SetBuffer(_computeGravityForceWithSAT, "inOutCellBuffer", _inCellBuffer);
        _computeShader.SetBuffer(_computeGravityForceWithSAT, "inOutMassSATBuffer", _inMassSATBuffer);
        _computeShader.SetBuffer(_computeGravityForceWithSAT, "nodeBuffer", _nodeBuffer);
        _computeShader.SetBuffer(_computeGravityForceWithSAT, "gridBuffer", _gridBuffer);
        _computeShader.Dispatch(_computeGravityForceWithSAT, numberOfCellGroups, 1, 1);

        float maxExpansionVel = maxMass * Density / (CellSize * CellSize);
        float deltaTime = (maxVel > 0) ? (CellSize / maxVel * MaxCellOffset) : Time.fixedDeltaTime;
        deltaTime = Mathf.Min(deltaTime, (CellSize / maxExpansionVel * MaxCellOffset));
        if (deltaTime > MaxDeltaTime) deltaTime = MaxDeltaTime;
        DeltaTime.text = deltaTime.ToString("F5");

        _computeShader.SetFloat("deltaTime", deltaTime);
        _computeShader.SetBuffer(_integrateVelocity, "inOutCellBuffer", _inCellBuffer);
        _computeShader.SetBuffer(_integrateVelocity, "gridBuffer", _gridBuffer);
        _computeShader.Dispatch(_integrateVelocity, numberOfCellGroups, 1, 1);

        _computeShader.SetBuffer(_momentumTransfer, "inCellBuffer", _inCellBuffer);
        _computeShader.SetBuffer(_momentumTransfer, "outCellBuffer", _outCellBuffer);
        _computeShader.SetBuffer(_momentumTransfer, "gridBuffer", _gridBuffer);
        _computeShader.Dispatch(_momentumTransfer, numberOfCellGroups, 1, 1);

        Swap(ref _inCellBuffer, ref _outCellBuffer);

        _computeShader.SetBuffer(_localExpansion, "inCellBuffer", _inCellBuffer);
        _computeShader.SetBuffer(_localExpansion, "outCellBuffer", _outCellBuffer);
        _computeShader.SetBuffer(_localExpansion, "gridBuffer", _gridBuffer);
        _computeShader.Dispatch(_localExpansion, numberOfCellGroups, 1, 1);

        Swap(ref _inCellBuffer, ref _outCellBuffer);
    }
}

