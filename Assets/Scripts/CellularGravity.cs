
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public enum Resolution
{
	_27x27,
	_81x81,
	_243x243,
	_324x324,
	_486x486,
	_567x567,
	_648x648,
	_729x729,
	_810x810,
	_972x972,
	_2187x2187
};

public enum DisplayMode
{
	Synthetic,
	Masses,
	Momentums,
	Forces,
	MassSAT,
	Propagation
};

public enum ScopeDisplay
{
	Disabled,
	Unfiltered,
	Bicubic1x,
	Bicubic2x
};

public enum MassPropagationWindow
{
	_3x3, // max offset : 1.0 x cell size
	_4x4, // max offset : 2.0 x cell size
	_5x5, // max offset : 2.5 x cell size
	_6x6, // max offset : 3.0 x cell size
	_7x7, // max offset : 3.5 x cell size
	_8x8, // max offset : 4.0 x cell size
	_9x9  // mas offset : 4.5 x cell size
};

[RequireComponent(typeof(Image))]
public partial class CellularGravity : MonoBehaviour, IPointerClickHandler
{
	[Header("UI")]
	public Text DeltaTime;
	public Text TotalMass;
	public Image ScopeImage;
	public int ScopeResolution = 27;
	public int ScopeDetails = 64;
	[Header("Settings")]
	public Resolution Resolution = Resolution._81x81;
	public float InitialMassMultiplier = 1.0f;
	public float CellSize = 1.0f;
	public float Gravity = 9.8f;
	public float Density = 1.0f;
	public MassPropagationWindow MaxMassPropagationWindow = MassPropagationWindow._3x3; 
	public float MaxCellOffset = 0.1f;
	public float MaxDeltaTime = 1.0f;
	[Header("Display")]
	public DisplayMode DisplayMode = DisplayMode.Masses;
	public ScopeDisplay ScopeDisplay = ScopeDisplay.Unfiltered;
	[Header("Seed")]
	public Texture2D MassTexture;
	public Vector2 InitialVelocityBias = new Vector2(0.825f,1.125f);
	public Vector2 InitialMassBias = new Vector2(0.75f, 1.25f);
	
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

	private bool _pause = false;
	private int _width;
	private int _height;
	private int _scopeImageWidth;
	private int _scopeImageHeight;
	private int _scopeImagePosX;
	private int _scopeImagePosY;
	private Image _image;
	private Material _gridMaterial;
	private Material _scopeMaterial;
	private Cell[] _cells = new Cell[0];
	private RowStats[] _rowStats = new RowStats[0];
	private int[] _massPropagations = new int[0];
	private ComputeBuffer _inCellBuffer = null;
	private ComputeBuffer _outCellBuffer = null;
	private ComputeBuffer _inMassSATBuffer = null;
	private ComputeBuffer _outMassSATBuffer = null;
	private ComputeBuffer _outRowStatsBuffer = null;
	private ComputeBuffer _inOutCellRectBuffer = null;
	private ComputeBuffer _inOutMassPropagationBuffer = null;
	private ComputeShader _computeShader = null;
	private RenderTexture _gridRenderTexture = null;
	private RenderTexture _scopeRenderTexture = null;
	private RenderTexture _downsampledScopeRenderTexture = null;

	private int NumMassPropagationIndices
	{
		get
		{
			switch (MaxMassPropagationWindow)
			{
				case MassPropagationWindow._3x3: return 9;
				case MassPropagationWindow._4x4: return 16;
				case MassPropagationWindow._5x5: return 25;
				case MassPropagationWindow._6x6: return 36;
				case MassPropagationWindow._7x7: return 47;
				case MassPropagationWindow._8x8: return 64;
				case MassPropagationWindow._9x9: return 81;
				default: return 4;
			}
		}
	}

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

		_scopeMaterial = new Material( Shader.Find("Unlit/Texture") );
		ScopeImage.material = _scopeMaterial;
		ScopeImage.SetMaterialDirty();
		
		string[] resolution = Resolution.ToString().Trim( '_' ).Split( 'x' );
		if (!int.TryParse(resolution[0], out _width) || !int.TryParse(resolution[1], out _height))
		{
			_width = 27;
			_height = 27;
		}
		_scopeImagePosX = _width / 2 - ScopeResolution / 2;
		_scopeImagePosY = _height / 2 - ScopeResolution / 2;
		
		Texture2D massTexture = GetReadableTexture(MassTexture, _width, _height);

		_gridRenderTexture = new RenderTexture( _width, _height, 0, RenderTextureFormat.ARGBHalf );		
		_gridRenderTexture.enableRandomWrite = true;
		_gridRenderTexture.filterMode = FilterMode.Point;
		_gridRenderTexture.Create();
		
		_gridMaterial.mainTexture = _gridRenderTexture;
		
		_scopeRenderTexture = new RenderTexture( ScopeResolution * ScopeDetails, ScopeResolution * ScopeDetails, 0, RenderTextureFormat.ARGBHalf );		
		_scopeRenderTexture.enableRandomWrite = true;
		_scopeRenderTexture.filterMode = FilterMode.Point;
		_scopeRenderTexture.Create();

		_scopeImageWidth = 2 * Mathf.FloorToInt( ScopeImage.GetComponent<RectTransform>().sizeDelta.x );
		_scopeImageHeight = 2 * Mathf.FloorToInt( ScopeImage.GetComponent<RectTransform>().sizeDelta.y );
		_downsampledScopeRenderTexture = new RenderTexture( _scopeImageWidth, _scopeImageHeight, 0, RenderTextureFormat.ARGBHalf );
		_downsampledScopeRenderTexture.enableRandomWrite = true;
		_downsampledScopeRenderTexture.filterMode = FilterMode.Point;
		_downsampledScopeRenderTexture.Create();

		_scopeMaterial.mainTexture = _downsampledScopeRenderTexture;

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
		_massPropagations = new int[_width*_height];

		_inCellBuffer = new ComputeBuffer( _cells.Length, Cell.SizeOf );			
		_outCellBuffer = new ComputeBuffer( _cells.Length, Cell.SizeOf );
		_inMassSATBuffer = new ComputeBuffer( _cells.Length, sizeof(float) * 3 );
		_outMassSATBuffer = new ComputeBuffer( _cells.Length, sizeof(float) * 3 );
		_outRowStatsBuffer = new ComputeBuffer( _rowStats.Length, RowStats.SizeOf );
		_inOutCellRectBuffer = new ComputeBuffer(_cells.Length, sizeof(float) * 4);
		_inOutMassPropagationBuffer = new ComputeBuffer(_cells.Length * (1 + NumMassPropagationIndices), sizeof(int));
		_computeShader = Resources.Load<ComputeShader>( "CellularGravity" );

		FindKernels( _computeShader );
		Initialize( massTexture );
	}

	void OnDestroy()
	{
		_inCellBuffer.Release();			
		_outCellBuffer.Release();
		_inMassSATBuffer.Release();
		_outMassSATBuffer.Release();
		_outRowStatsBuffer.Release();
		_inOutCellRectBuffer.Release();
		_inOutMassPropagationBuffer.Release();
	}
	
	public void OnShowSyntheticImage(string arg)
	{
		DisplayMode = DisplayMode.Synthetic;
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
	
	public void OnShowPropagation(string arg)
	{
		DisplayMode = DisplayMode.Propagation;
	}
	
	public void Play(string arg)
	{
		_pause = false;
	}
	
	public void Pause(string arg)
	{
		_pause = true;
	}
	
	public void OnPointerClick(PointerEventData pointerEventData)
	{
		Vector2 localCursorPos = Vector2.zero;
		RectTransformUtility.ScreenPointToLocalPointInRectangle(
			GetComponent<RectTransform>(),
			pointerEventData.position,
			pointerEventData.pressEventCamera,
			out localCursorPos
		);

		Vector2 widgetSize = GetComponent<RectTransform>().sizeDelta;

		Vector2 localCursorUV = new Vector2
		(
			( widgetSize.x / 2 + localCursorPos.x ) / widgetSize.x,
			( widgetSize.y / 2 + localCursorPos.y ) / widgetSize.y
		);

		_scopeImagePosX = Mathf.FloorToInt( localCursorUV.x * _width ) - ScopeResolution / 2;
		_scopeImagePosY = Mathf.FloorToInt( localCursorUV.y * _height ) - ScopeResolution / 2;

		_scopeImagePosX = Mathf.Clamp(_scopeImagePosX, 0, _width - ScopeResolution);
		_scopeImagePosY = Mathf.Clamp(_scopeImagePosY, 0, _height - ScopeResolution);
	}

	private void Update()
	{
		if (!_pause)
		{
			SimulateGPU();
		}

		int drawMasses = _computeShader.FindKernel( "DrawMasses" );
		int drawMomentums = _computeShader.FindKernel( "DrawMomentums" );
		int drawForces = _computeShader.FindKernel( "DrawForces" );
		int drawMassSAT = _computeShader.FindKernel( "DrawMassSAT" );
		int drawSyntheticImage = _computeShader.FindKernel( "DrawSyntheticImage" );
		int drawPropagation = _computeShader.FindKernel( "DrawPropagation" );
		int numberOfGroups = Mathf.CeilToInt( (float)(_width*_height) / GPUGroupSize );

		switch (DisplayMode)
		{
			case DisplayMode.Synthetic:
				_computeShader.SetBuffer(drawSyntheticImage, "inOutCellBuffer", _inCellBuffer);
				_computeShader.SetTexture(drawSyntheticImage, "renderTexture", _gridRenderTexture);
				_computeShader.Dispatch(drawSyntheticImage, numberOfGroups, 1, 1);
				break;
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
			case DisplayMode.Propagation:
				_computeShader.SetBuffer(drawPropagation, "inOutMassPropagationBuffer", _inOutMassPropagationBuffer);
				_computeShader.SetTexture(drawPropagation, "renderTexture", _gridRenderTexture);
				_computeShader.Dispatch(drawPropagation, numberOfGroups, 1, 1);
				break;
			default:
				break;
		}

		if (ScopeDisplay == ScopeDisplay.Disabled)
		{
			return;
		}
		
		int drawScopeImage = _computeShader.FindKernel("DrawScopeImage");
		int numberOfScopeGroups = Mathf.CeilToInt( (float)(ScopeResolution*ScopeResolution*ScopeDetails*ScopeDetails) / GPUGroupSize );

		_computeShader.SetInt("width", _width ); 
		_computeShader.SetInt("height", _height );
		_computeShader.SetInt("scopePosX", _scopeImagePosX ); 
		_computeShader.SetInt("scopePosY", _scopeImagePosY );
		_computeShader.SetInt("scopeWindowWidth", ScopeResolution);
		_computeShader.SetInt("scopeWindowHeight", ScopeResolution);
		_computeShader.SetInt("scopeCellResolution", ScopeDetails);
		_computeShader.SetBuffer(drawScopeImage, "inOutCellBuffer", _inCellBuffer);
		_computeShader.SetTexture(drawScopeImage, "renderTexture", _scopeRenderTexture);
		_computeShader.Dispatch(drawScopeImage, numberOfScopeGroups, 1, 1);

		int bicubicDownsample = _computeShader.FindKernel("BicubicDownsample");
		int numberOfBicubicDownsampleGroups = Mathf.CeilToInt( (float)(_scopeImageWidth*_scopeImageHeight) / GPUGroupSize );
		
		switch (ScopeDisplay)
		{
			case ScopeDisplay.Unfiltered:
				{
					_scopeMaterial.mainTexture = _scopeRenderTexture;
				}
				break;
			case ScopeDisplay.Bicubic1x:
				{
					_computeShader.SetInt("originalWidth", ScopeResolution * ScopeDetails);
					_computeShader.SetInt("originalHeight", ScopeResolution * ScopeDetails);
					_computeShader.SetInt("downsampledWidth", _scopeImageWidth);
					_computeShader.SetInt("downsampledHeight", _scopeImageHeight);
					_computeShader.SetTexture(bicubicDownsample, "inRenderTexture", _scopeRenderTexture);
					_computeShader.SetTexture(bicubicDownsample, "outRenderTexture", _downsampledScopeRenderTexture);
					_computeShader.Dispatch(bicubicDownsample, numberOfBicubicDownsampleGroups, 1, 1);
					_scopeMaterial.mainTexture = _downsampledScopeRenderTexture;
				}
				break;
			case ScopeDisplay.Bicubic2x:
				{
					int intermediateWidth = (ScopeResolution * ScopeDetails + _scopeImageWidth) / 2;
					int intermediateHeight = (ScopeResolution * ScopeDetails + _scopeImageHeight) / 2;
					int numberOfIntermediateBicubicDownsampleGroups = Mathf.CeilToInt( (float)(intermediateWidth*intermediateHeight) / GPUGroupSize );

					RenderTexture intermediateRenderTexture = RenderTexture.GetTemporary(intermediateWidth, intermediateHeight, 0, RenderTextureFormat.ARGBHalf);
					intermediateRenderTexture.enableRandomWrite = true;
					intermediateRenderTexture.filterMode = FilterMode.Point;
					if (!intermediateRenderTexture.IsCreated())
					{
						intermediateRenderTexture.Create();
					}
		
					_computeShader.SetInt("originalWidth", ScopeResolution * ScopeDetails );
					_computeShader.SetInt("originalHeight", ScopeResolution * ScopeDetails );
					_computeShader.SetInt("downsampledWidth", intermediateWidth);
					_computeShader.SetInt("downsampledHeight", intermediateHeight);
					_computeShader.SetTexture(bicubicDownsample, "inRenderTexture", _scopeRenderTexture);
					_computeShader.SetTexture(bicubicDownsample, "outRenderTexture", intermediateRenderTexture);
					_computeShader.Dispatch(bicubicDownsample, numberOfIntermediateBicubicDownsampleGroups, 1, 1);
		
					_computeShader.SetInt("originalWidth", intermediateWidth );
					_computeShader.SetInt("originalHeight", intermediateHeight );
					_computeShader.SetInt("downsampledWidth", _scopeImageWidth);
					_computeShader.SetInt("downsampledHeight", _scopeImageHeight);
					_computeShader.SetTexture(bicubicDownsample, "inRenderTexture", intermediateRenderTexture);
					_computeShader.SetTexture(bicubicDownsample, "outRenderTexture", _downsampledScopeRenderTexture);
					_computeShader.Dispatch(bicubicDownsample, numberOfBicubicDownsampleGroups, 1, 1);
		
					RenderTexture.ReleaseTemporary( intermediateRenderTexture );
					_scopeMaterial.mainTexture = _downsampledScopeRenderTexture;
				}
				break;
		}
	}
}
