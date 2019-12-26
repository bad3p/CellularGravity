
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

public enum DisplayMode
{
	Masses,
	Velocities
};

[RequireComponent(typeof(Image))]
public partial class CellularGravity : MonoBehaviour
{
	[Header("UI")]
	public Text DeltaTime;
	public Image[] NodeImages = new Image[0];
	[Header("Settings")]
	public Resolution Resolution = Resolution._81x81;
	public float InitialMassMultiplier = 1.0f;
	public float CellSize = 1.0f;
	public float Gravity = 9.8f;
	public float Density = 1.0f;
	public float MaxCellOffset = 0.1f;
	public float MaxDeltaTime = 1.0f;
	public DisplayMode DisplayMode = DisplayMode.Masses; 
	[Header("Seed")]
	public Texture2D ColorTexture;
	public Texture2D MassTexture;
	public Gradient MassGradient;
	
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
	private Material _gridMaterial;
	private Material[] _nodeMaterials = new Material[0];
	private Cell[] _cells = new Cell[0];
	private Node[] _nodes = new Node[0];
	private Grid[] _grids = new Grid[0];
	private ComputeBuffer _inCellBuffer = null;
	private ComputeBuffer _outCellBuffer = null;
	private ComputeBuffer _nodeBuffer = null;
	private ComputeBuffer _gridBuffer = null;
	private ComputeShader _computeShader = null;
	private RenderTexture _gridRenderTexture = null;
	private RenderTexture[] _nodeRenderTextures = new RenderTexture[0];

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

		for (int i = 0; i < NodeImages.Length; i++)
		{
			if (NodeImages[i])
			{
				NodeImages[i].material = new Material( Shader.Find("Unlit/Texture") );
				NodeImages[i].material.mainTexture = Texture2D.blackTexture;
			}
		}
		
		_gridMaterial = new Material( Shader.Find("Unlit/Texture") );
		_image.material = _gridMaterial;
		_image.SetMaterialDirty();
		
		string[] resolution = Resolution.ToString().Trim( '_' ).Split( 'x' );
		if (!int.TryParse(resolution[0], out _width) || !int.TryParse(resolution[1], out _height))
		{
			_width = 27;
			_height = 27;
		}
		
		Texture2D massTexture = GetReadableTexture(MassTexture, _width, _height);
		Texture2D colorTexture = GetReadableTexture(ColorTexture, _width, _height);

		_gridRenderTexture = new RenderTexture( _width, _height, 0, RenderTextureFormat.ARGBHalf );		
		_gridRenderTexture.enableRandomWrite = true;
		_gridRenderTexture.filterMode = FilterMode.Point;
		_gridRenderTexture.Create();
		
		_gridMaterial.mainTexture = _gridRenderTexture;

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

		_nodeRenderTextures = new RenderTexture[nodeBufferLength+1];
		_nodeMaterials = new Material[nodeBufferLength+1];
		int nodeRenderTextureWidth = _width / 3;
		int nodeRenderTextureHeight = _height / 3;
		for (int i = 0; i < nodeBufferLength + 1; i++)
		{
			_nodeRenderTextures[i] = new RenderTexture( nodeRenderTextureWidth, nodeRenderTextureHeight, 0, RenderTextureFormat.ARGBHalf );
			_nodeRenderTextures[i].enableRandomWrite = true;
			_nodeRenderTextures[i].filterMode = FilterMode.Point;
			_nodeRenderTextures[i].Create();

			if (nodeRenderTextureWidth == 1 || nodeRenderTextureHeight == 1)
			{
				break;
			}
			
			nodeRenderTextureWidth /= 3;
			nodeRenderTextureHeight /= 3;
			
			_nodeMaterials[i] = new Material( Shader.Find("Unlit/Texture") );
			_nodeMaterials[i].mainTexture = _nodeRenderTextures[i];

			if (NodeImages.Length > i && NodeImages[i] != null)
			{
				NodeImages[i].material = _nodeMaterials[i];
				NodeImages[i].SetMaterialDirty();
			}
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

	public void OnShowMasses(string arg)
	{
		DisplayMode = DisplayMode.Masses;
	}
	
	public void OnShowVelocities(string arg)
	{
		DisplayMode = DisplayMode.Velocities;
	}

	private void Update()
	{
		SimulateGPU();
		
		int drawMasses = _computeShader.FindKernel( "DrawMasses" );
		int drawVelocities = _computeShader.FindKernel( "DrawVelocities" );
		int drawNodes = _computeShader.FindKernel( "DrawNodes" );
		
		int numberOfGroups = Mathf.CeilToInt( (float)(_width*_height) / GPUGroupSize );

		switch (DisplayMode)
		{
			case DisplayMode.Masses:
				_computeShader.SetBuffer(drawMasses, "inOutCellBuffer", _inCellBuffer);
				_computeShader.SetBuffer(drawMasses, "gridBuffer", _gridBuffer);
				_computeShader.SetTexture(drawMasses, "renderTexture", _gridRenderTexture);
				_computeShader.Dispatch(drawMasses, numberOfGroups, 1, 1);
				break;
			case DisplayMode.Velocities:
				_computeShader.SetBuffer(drawVelocities, "inOutCellBuffer", _inCellBuffer);
				_computeShader.SetBuffer(drawVelocities, "gridBuffer", _gridBuffer);
				_computeShader.SetTexture(drawVelocities, "renderTexture", _gridRenderTexture);
				_computeShader.Dispatch(drawVelocities, numberOfGroups, 1, 1);
				break;
			default:
				break;
		}

		for (int i = 0; i < _nodeRenderTextures.Length; i++)
		{
			if (_nodeRenderTextures[i])
			{
				_computeShader.SetInt("gridIndex", i + 1);
				_computeShader.SetFloat("cellSize", CellSize);
				_computeShader.SetBuffer(drawNodes, "inOutCellBuffer", _inCellBuffer);
				_computeShader.SetBuffer(drawNodes, "nodeBuffer", _nodeBuffer);
				_computeShader.SetBuffer(drawNodes, "gridBuffer", _gridBuffer);
				_computeShader.SetTexture(drawNodes, "renderTexture", _nodeRenderTextures[i]);
				_computeShader.Dispatch(drawNodes, numberOfGroups, 1, 1);
			}
		}
	}
}
