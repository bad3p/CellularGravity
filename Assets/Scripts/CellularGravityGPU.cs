using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using UnityEditor;
using UnityEngine;

public partial class CellularGravity : MonoBehaviour
{
    const int GPUGroupSize = 128;
    
    private int _accumulateCellMasses;
    private int _accumulateNodeMasses;
    private int _computeGravityForce;
    private int _integrateVelocity;
    private int _momentumTransfer;
    private int _localExpansion;
    private int _scaleCells;

    private void FindKernels(ComputeShader computeShader)
    {
        _accumulateCellMasses = computeShader.FindKernel( "AccumulateCellMasses" );
        _accumulateNodeMasses = computeShader.FindKernel( "AccumulateNodeMasses" );
        _computeGravityForce = computeShader.FindKernel( "ComputeGravityForce" );
        _integrateVelocity = computeShader.FindKernel( "IntegrateVelocity" );
        _momentumTransfer = computeShader.FindKernel( "MomentumTransfer" );
        _localExpansion = computeShader.FindKernel( "LocalExpansion" );
        _scaleCells = computeShader.FindKernel( "ScaleCells" );      
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
            _cells[i].pos = new Vector2( x * CellSize + CellSize/2, y * CellSize + CellSize/2 );
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
                    
                _nodes[i+startK].pos = new Vector2( x * cellSizeK + cellSizeK/2, y * cellSizeK + cellSizeK/2 );
                _nodes[i+startK].mass = 0.0f;
                _nodes[i+startK].maxMass = 0.0f;
                _nodes[i+startK].maxVel = 0.0f;
            }
        }
        
        _inCellBuffer.SetData(_cells);
        _outCellBuffer.SetData(_cells);
        _nodeBuffer.SetData(_nodes);
        _gridBuffer.SetData(_grids);
    }

    private void AccumulateMasses()
    {        
        int numberOfNodeGroups = Mathf.CeilToInt( (float)(_grids[1].length) / GPUGroupSize );

        _computeShader.SetBuffer(_accumulateCellMasses, "inOutCellBuffer", _inCellBuffer);
        _computeShader.SetBuffer(_accumulateCellMasses, "nodeBuffer", _nodeBuffer);
        _computeShader.SetBuffer(_accumulateCellMasses, "gridBuffer", _gridBuffer);
        _computeShader.Dispatch(_accumulateCellMasses, numberOfNodeGroups, 1, 1);
        
        for (int i = 2; i < _grids.Length; i++) // [0] is cell grid, [1] is largest node grid
        {
            numberOfNodeGroups = Mathf.CeilToInt( (float)(_grids[i].length) / GPUGroupSize );
            
            _computeShader.SetInt("gridIndex", i);
            _computeShader.SetBuffer(_accumulateNodeMasses, "inOutCellBuffer", _inCellBuffer);
            _computeShader.SetBuffer(_accumulateNodeMasses, "nodeBuffer", _nodeBuffer);
            _computeShader.SetBuffer(_accumulateNodeMasses, "gridBuffer", _gridBuffer);
            _computeShader.Dispatch(_accumulateNodeMasses, numberOfNodeGroups, 1, 1);
        }
    }

    private void SimulateGPU()
    {
        AccumulateMasses();
        
        int numberOfCellGroups = Mathf.CeilToInt( (float)(_cells.Length) / GPUGroupSize );

        _nodeBuffer.GetData(_nodes);
        //_inCellBuffer.GetData(_cells);

        float maxMass = _nodes[_nodes.Length - 1].maxMass;
        float maxVel = _nodes[_nodes.Length - 1].maxVel;
        Vector2 inf = _nodes[_nodes.Length - 1].inf;
        Vector2 sup = _nodes[_nodes.Length - 1].sup;

        _computeShader.SetInt("numGrids", _grids.Length);
        _computeShader.SetFloat("gravity", Gravity);
        _computeShader.SetFloat("cellSize", CellSize);
        _computeShader.SetFloat("density", Density);
        _computeShader.SetBuffer(_computeGravityForce, "inOutCellBuffer", _inCellBuffer);
        _computeShader.SetBuffer(_computeGravityForce, "nodeBuffer", _nodeBuffer);
        _computeShader.SetBuffer(_computeGravityForce, "gridBuffer", _gridBuffer);
        _computeShader.Dispatch(_computeGravityForce, numberOfCellGroups, 1, 1);
        
        const float MaxCellOffset = 0.1f;
        float maxExpansionVel = maxMass * Density / (CellSize * CellSize);
        float deltaTime = (maxVel > 0) ? (CellSize / maxVel * MaxCellOffset) : Time.fixedDeltaTime;
        deltaTime = Mathf.Min(deltaTime, (CellSize / maxExpansionVel * MaxCellOffset));
        if (deltaTime > Time.fixedDeltaTime) deltaTime = Time.fixedDeltaTime;
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
     
        //ScaleCells( 1, 1, _width-1, _height-1 );

        if( false )  
        //if( Time.frameCount % 100 == 0 )
        //if (inf.x > 1 && inf.y > 1 && sup.x < _width - 2 && sup.y < _height - 2)       
        {
            int xInf = 1;
            int yInf = 1;
            int xSup = _width - 1;
            int ySup = _height - 1;

            _inCellBuffer.GetData(_cells);

            Vector2 oldGridPos = _cells[0].pos - new Vector2(CellSize / 2, CellSize / 2);
            Vector2 newGridPos = oldGridPos + new Vector2(xInf, yInf) * CellSize;

            float scaledCellSize = CellSize * (xSup - xInf) / _width;
            float scaledCellArea = scaledCellSize * scaledCellSize;

            _computeShader.SetInt("xInf", xInf);
            _computeShader.SetInt("yInf", yInf);
            _computeShader.SetInt("xSup", xSup);
            _computeShader.SetInt("ySup", ySup);
            _computeShader.SetVector("oldGridPos", oldGridPos);
            _computeShader.SetVector("newGridPos", newGridPos);
            _computeShader.SetFloat("cellSize", CellSize);
            _computeShader.SetFloat("cellArea", CellSize * CellSize);
            _computeShader.SetFloat("scaledCellSize", scaledCellSize);
            _computeShader.SetFloat("scaledCellArea", scaledCellArea);

            _computeShader.SetBuffer(_scaleCells, "inCellBuffer", _inCellBuffer);
            _computeShader.SetBuffer(_scaleCells, "outCellBuffer", _outCellBuffer);
            _computeShader.SetBuffer(_scaleCells, "gridBuffer", _gridBuffer);
            _computeShader.Dispatch(_scaleCells, numberOfCellGroups, 1, 1);
            Swap(ref _inCellBuffer, ref _outCellBuffer);

            CellSize = CellSize * (xSup - xInf) / _width;
        }
    }
}

