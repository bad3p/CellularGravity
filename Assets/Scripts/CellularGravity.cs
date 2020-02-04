
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
	Momentums,
	Forces,
	MassSAT
};

[RequireComponent(typeof(Image))]
public partial class CellularGravity : MonoBehaviour
{
	[Header("UI")]
	public Text DeltaTime;
	public Text TotalMass;
	[Header("Settings")]
	public Resolution Resolution = Resolution._81x81;
	public float InitialMassMultiplier = 1.0f;
	public float CellSize = 1.0f;
	public float Gravity = 9.8f;
	public float Density = 1.0f;
	public float MaxCellOffset = 0.1f;
	public float MaxDeltaTime = 1.0f;
	public DisplayMode DisplayMode = DisplayMode.Masses;
	public bool UseSAT = true;
	[Header("Seed")]
	public Texture2D MassTexture;
	public Gradient MassGradient;
	
	public struct Cell
	{
		public float   mass;    // sizeof(float) = 4
		public Vector2 vel;     // 2 * sizeof(float) = 8
		public Vector2 force;   // 2 * sizeof(float) = 8

		public const int SizeOf = 20; // ComputeShader stride
	};
	
	public struct RowStats
	{
		public float maxMass; // sizeof(float) = 4
		public float maxVel;  // sizeof(float) = 4
		public float totalMass; // sizeof(float) = 4
		
		public const int SizeOf = 12; // ComputeShader stride
	};

	private int _width;
	private int _height;
	private Image _image;
	private Material _gridMaterial;
	private Cell[] _cells = new Cell[0];
	private RowStats[] _rowStats = new RowStats[0];
	private ComputeBuffer _inCellBuffer = null;
	private ComputeBuffer _outCellBuffer = null;
	private ComputeBuffer _inMassSATBuffer = null;
	private ComputeBuffer _outMassSATBuffer = null;
	private ComputeBuffer _outRowStatsBuffer = null;
	private ComputeShader _computeShader = null;
	private RenderTexture _gridRenderTexture = null;

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
			
		_cells = new Cell[_width*_height];
		_rowStats = new RowStats[_height];

		_inCellBuffer = new ComputeBuffer( _cells.Length, Cell.SizeOf );			
		_outCellBuffer = new ComputeBuffer( _cells.Length, Cell.SizeOf );
		_inMassSATBuffer = new ComputeBuffer( _cells.Length, sizeof(float) );
		_outMassSATBuffer = new ComputeBuffer( _cells.Length, sizeof(float) );
		_outRowStatsBuffer = new ComputeBuffer( _rowStats.Length, RowStats.SizeOf );
		_computeShader = Resources.Load<ComputeShader>( "CellularGravity" );

		FindKernels( _computeShader );
		Initialize( massTexture );
	}

	private void Start()
	{
		var sizeDelta = _image.rectTransform.sizeDelta;
	}

	void OnDestroy()
	{
		_inCellBuffer.Release();			
		_outCellBuffer.Release();
		_inMassSATBuffer.Release();
		_outMassSATBuffer.Release();
		_outRowStatsBuffer.Release();
	}

	public void OnShowMasses(string arg)
	{
		DisplayMode = DisplayMode.Masses;
	}
	
	public void OnShowMomentums(string arg)
	{
		DisplayMode = DisplayMode.Momentums;
	}
	
	public void OnShowForces(string arg)
	{
		DisplayMode = DisplayMode.Forces;
	}
	
	public void OnShowMassSAT(string arg)
	{
		DisplayMode = DisplayMode.MassSAT;
	}

	private void Update()
	{
		SimulateGPU();
		
		int drawMasses = _computeShader.FindKernel( "DrawMasses" );
		int drawMomentums = _computeShader.FindKernel( "DrawMomentums" );
		int drawForces = _computeShader.FindKernel( "DrawForces" );
		int drawMassSAT = _computeShader.FindKernel( "DrawMassSAT" );
		
		int numberOfGroups = Mathf.CeilToInt( (float)(_width*_height) / GPUGroupSize );

		switch (DisplayMode)
		{
			case DisplayMode.Masses:
				_computeShader.SetBuffer(drawMasses, "inOutCellBuffer", _inCellBuffer);
				_computeShader.SetTexture(drawMasses, "renderTexture", _gridRenderTexture);
				_computeShader.Dispatch(drawMasses, numberOfGroups, 1, 1);
				break;
			case DisplayMode.Momentums:
				_computeShader.SetBuffer(drawMomentums, "inOutCellBuffer", _inCellBuffer);
				_computeShader.SetTexture(drawMomentums, "renderTexture", _gridRenderTexture);
				_computeShader.Dispatch(drawMomentums, numberOfGroups, 1, 1);
				break;
			case DisplayMode.Forces:
				_computeShader.SetBuffer(drawForces, "inOutCellBuffer", _inCellBuffer);
				_computeShader.SetTexture(drawForces, "renderTexture", _gridRenderTexture);
				_computeShader.Dispatch(drawForces, numberOfGroups, 1, 1);
				break;
			case DisplayMode.MassSAT:
				_computeShader.SetBuffer(drawMassSAT, "inOutMassSATBuffer", _inMassSATBuffer);
				_computeShader.SetTexture(drawMassSAT, "renderTexture", _gridRenderTexture);
				_computeShader.Dispatch(drawMassSAT, numberOfGroups, 1, 1);
				break;
			default:
				break;
		}
	}
}
