﻿
using UnityEngine;
using UnityEngine.UI;

public enum Resolution
{
	_27x27,
	_81x81,
	_243x243,
	_729x729,
	_2187x2187
};

[RequireComponent(typeof(Image))]
public partial class CellularGravity : MonoBehaviour
{
	public Resolution Resolution = Resolution._81x81;
	public float InitialMassMultiplier = 1.0f;
	public float CellSize = 1.0f;
	public float Gravity = 9.8f;
	public float Density = 1.0f;
	public Texture2D ColorTexture;
	public Texture2D MassTexture;
	public Gradient MassGradient;
	public bool Test;
	
	public struct Cell
	{
		public float   mass;    // sizeof(float) = 4
		public Vector4 color;   // 4 * sizeof(float) = 16
		public Vector2 vel;     // 2 * sizeof(float) = 8
		public Vector2 pos;     // 2 * sizeof(float) = 8		
		public Vector2 force;   // 2 * sizeof(float) = 8

		public const int SizeOf = 44; // ComputeShader stride
	};
	
	public struct Node
	{
		public float   mass;    // sizeof(float) = 4
		public Vector2 pos;     // 2 * sizeof(float) = 8
		public float   maxMass; // sizeof(float) = 4
		public float   maxVel;  // sizeof(float) = 4
		public Vector2 inf;     // 2 * sizeof(float) = 8
		public Vector2 sup;     // 2 * sizeof(float) = 8

		public const int SizeOf = 36; // ComputeShader stride
	};

	public struct Grid
	{
		public int start; // sizeof(int) = 4
		public int length; // sizeof(int) = 4
		public int width; // sizeof(int) = 4
		public int height; // sizeof(int) = 4
		
		public const int SizeOf = 16; // ComputeShader stride
	};

	private int _width;
	private int _height;
	private Image _image;
	private Material _material;
	private Cell[] _cells = new Cell[0];
	private Node[] _nodes = new Node[0];
	private Grid[] _grids = new Grid[0];
	private ComputeBuffer _inCellBuffer = null;
	private ComputeBuffer _outCellBuffer = null;
	private ComputeBuffer _nodeBuffer = null;
	private ComputeBuffer _gridBuffer = null;
	private ComputeShader _computeShader = null;
	private RenderTexture _renderTexture = null;		

	private Texture2D GetReadableTexture(Texture2D src, int width, int height)
	{
		RenderTexture rt = new RenderTexture( width, height, 0, RenderTextureFormat.ARGB32 );
		Graphics.Blit( src, rt );
		
		Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);
		RenderTexture.active = rt;
		result.ReadPixels( new Rect(0,0,width,height), 0, 0);
		RenderTexture.active = null;
		rt.Release();

		return result;
	}
	
	private void Awake()
	{
		_image = GetComponent<Image>();
		
		_material = new Material( Shader.Find("Unlit/Texture") );
		_image.material = _material;
		_image.SetMaterialDirty();
		
		string[] resolution = Resolution.ToString().Trim( '_' ).Split( 'x' );
		if (!int.TryParse(resolution[0], out _width) || !int.TryParse(resolution[1], out _height))
		{
			_width = 27;
			_height = 27;
		}
		
		Texture2D massTexture = GetReadableTexture(MassTexture, _width, _height);
		Texture2D colorTexture = GetReadableTexture(ColorTexture, _width, _height);

		_renderTexture = new RenderTexture( _width, _height, 0, RenderTextureFormat.ARGBHalf );		
		_renderTexture.enableRandomWrite = true;
		_renderTexture.filterMode = FilterMode.Bilinear;
		_renderTexture.Create();
		
		_material.mainTexture = _renderTexture;

		int nodeBufferLength = 0;
		int gridBufferLength = 0;
		int width = _width / 3;
		int height = _height / 3;			
		while (width >= 3 && height >= 3)
		{
			nodeBufferLength += width * height;
			gridBufferLength++;
			width /= 3;
			height /= 3;
		}
			
		_cells = new Cell[_width*_height];
		_nodes = new Node[nodeBufferLength+1]; // + node for 1x1 grid
		_grids = new Grid[gridBufferLength+2]; // + cell grid (first) + 1x1 grid (last)

		int offset = 0;
		for (int i = 0; i < _grids.Length; i++)
		{			
			if (i == 0)
			{	
				_grids[i].start = 0;
				_grids[i].length = _width * _height;
				_grids[i].width = _width;
				_grids[i].height = _height;
			}
			else
			{
				_grids[i].start = offset;
				_grids[i].length = _grids[i-1].length / 9;
				_grids[i].width = _grids[i-1].width / 3;
				_grids[i].height = _grids[i-1].height / 3;
				offset += _grids[i].length;
			}			
		}

		_inCellBuffer = new ComputeBuffer( _cells.Length, Cell.SizeOf );			
		_outCellBuffer = new ComputeBuffer( _cells.Length, Cell.SizeOf );
		_nodeBuffer = new ComputeBuffer( _nodes.Length, Node.SizeOf );			
		_gridBuffer = new ComputeBuffer( _grids.Length, Grid.SizeOf );
		_computeShader = Resources.Load<ComputeShader>( "CellularGravity" );

		FindKernels( _computeShader );
		Initialize( massTexture, colorTexture );
	}

	private void Start()
	{
		var sizeDelta = _image.rectTransform.sizeDelta;
	}

	void OnDestroy()
	{
		_inCellBuffer.Release();			
		_outCellBuffer.Release();
		_nodeBuffer.Release();			
		_gridBuffer.Release();
	}

	private void Update()
	{
		SimulateGPU();
		
		int drawMasses = _computeShader.FindKernel( "DrawMasses" );
		int drawVelocities = _computeShader.FindKernel( "DrawVelocities" );
		int drawNodes = _computeShader.FindKernel( "DrawNodes" );
		
		int numberOfGroups = Mathf.CeilToInt( (float)(_width*_height) / GPUGroupSize );
				
		_computeShader.SetBuffer( drawMasses, "inOutCellBuffer", _inCellBuffer );
		_computeShader.SetBuffer( drawMasses, "gridBuffer", _gridBuffer );
		_computeShader.SetTexture( drawMasses, "renderTexture", _renderTexture);
		_computeShader.Dispatch( drawMasses, numberOfGroups, 1, 1 );
		
		
		/*
		_computeShader.SetInt("gridIndex", 1);
		_computeShader.SetFloat("cellSize", CellSize);
		_computeShader.SetBuffer( drawNodes, "inOutCellBuffer", _inCellBuffer );
		_computeShader.SetBuffer( drawNodes, "nodeBuffer", _nodeBuffer );
		_computeShader.SetBuffer( drawNodes, "gridBuffer", _gridBuffer );
		_computeShader.SetTexture( drawNodes, "renderTexture", _renderTexture);
		_computeShader.Dispatch( drawNodes, numberOfGroups, 1, 1 );
		*/
	}
}