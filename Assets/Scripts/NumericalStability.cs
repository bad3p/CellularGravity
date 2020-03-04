using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class NumericalStability : MonoBehaviour
{
	const int GPUGroupSize = 128;

	[Header("UI")] 
	public Text NumIterationsRemaining;
	
    [Header("Settings")]
    public int     Resolution = 256;
    public Vector2 InitialBias = new Vector2( 0.0f, 1.0f );
    public int     SATSampleSize = 8;
    public int     NumIterations = 10;
    
    private Image    _image;
    private Material _material;
    private ComputeBuffer _imageBuffer32  = null;
    private ComputeBuffer _inSATBuffer32  = null;
    private ComputeBuffer _outSATBuffer32 = null;
    private ComputeBuffer _imageBuffer64  = null;
    private ComputeBuffer _inSATBuffer64  = null;
    private ComputeBuffer _outSATBuffer64 = null;
    private ComputeShader _computeShader  = null;
    private RenderTexture _stepRenderTexture = null;
    private RenderTexture _integralRenderTexture = null;
    private RenderTexture _resultRenderTexture = null;
    private int _numIterations = 0;
    private float[] _image32;
    private double[] _image64;
    
    private static void Swap(ref ComputeBuffer cbRef0, ref ComputeBuffer cbRef1)
    {
	    ComputeBuffer temp = cbRef0;
	    cbRef0 = cbRef1;
	    cbRef1 = temp;
    }
    
    private void Awake()
	{
		_image = GetComponent<Image>();
		
		_material = new Material( Shader.Find("Unlit/Texture") );
		_image.material = _material;
		_image.SetMaterialDirty();

		_stepRenderTexture = new RenderTexture( Resolution, Resolution, 0, RenderTextureFormat.ARGBHalf );		
		_stepRenderTexture.enableRandomWrite = true;
		_stepRenderTexture.filterMode = FilterMode.Point;
		_stepRenderTexture.Create();
		
		_integralRenderTexture = new RenderTexture( Resolution, Resolution, 0, RenderTextureFormat.ARGBHalf );		
		_integralRenderTexture.enableRandomWrite = true;
		_integralRenderTexture.filterMode = FilterMode.Point;
		_integralRenderTexture.Create();
		
		_resultRenderTexture = new RenderTexture( Resolution, Resolution, 0, RenderTextureFormat.ARGBHalf );		
		_resultRenderTexture.enableRandomWrite = true;
		_resultRenderTexture.filterMode = FilterMode.Bilinear;
		_resultRenderTexture.Create();
		
		_material.mainTexture = _resultRenderTexture;

		_imageBuffer32 = new ComputeBuffer( Resolution * Resolution, sizeof(float) );
		_inSATBuffer32 = new ComputeBuffer( Resolution * Resolution, sizeof(float) * 3 );
		_outSATBuffer32 = new ComputeBuffer( Resolution * Resolution, sizeof(float) * 3 );
		_imageBuffer64 = new ComputeBuffer( Resolution * Resolution, sizeof(double) );
		_inSATBuffer64 = new ComputeBuffer( Resolution * Resolution, sizeof(double) * 3 );
		_outSATBuffer64 = new ComputeBuffer( Resolution * Resolution, sizeof(double) * 3 );
		_computeShader = Resources.Load<ComputeShader>( "NumericalStability" );
		
		int numberOfGroups = Mathf.CeilToInt((float) (Resolution*Resolution) / GPUGroupSize);
		int resetRenderTexture = _computeShader.FindKernel("ResetRenderTexture");
		_computeShader.SetTexture(resetRenderTexture, "outRenderTexture", _integralRenderTexture);
		_computeShader.Dispatch(resetRenderTexture, numberOfGroups, 1, 1);
		
		_image32 = new float[Resolution * Resolution];
		_image64 = new double[Resolution * Resolution];
	}

    void Update()
    {
	    if (_numIterations >= NumIterations)
	    {
		    return;
	    }
	    _numIterations++;
	    NumIterationsRemaining.text = (NumIterations - _numIterations).ToString(); 
	    
		for (int i = 0; i < Resolution * Resolution; i++)
		{
			float value = Random.Range( InitialBias.x, InitialBias.y );
			_image32[i] = value;
			_image64[i] = value;
		}
		
		_imageBuffer32.SetData(_image32);
		_imageBuffer64.SetData(_image64);
		
		int initSATBuffers = _computeShader.FindKernel("InitSATBuffers");
		int transposeSATBuffers = _computeShader.FindKernel("TransposeSATBuffers");
		int computeSAT = _computeShader.FindKernel("ComputeSAT");
		int drawSATDifference = _computeShader.FindKernel("DrawSATDifference");
		int integrateIteration = _computeShader.FindKernel("IntegrateIteration");
		int drawResult = _computeShader.FindKernel("DrawResult");
		
		int numberOfGroups = Mathf.CeilToInt((float) (Resolution*Resolution) / GPUGroupSize);
        
		_computeShader.SetInt("width", Resolution);
		_computeShader.SetInt("height", Resolution);
		_computeShader.SetInt("satSampleSize", SATSampleSize);
		_computeShader.SetInt("numIterations", _numIterations);
		
		_computeShader.SetBuffer(initSATBuffers, "inImageBuffer32", _imageBuffer32);
		_computeShader.SetBuffer(initSATBuffers, "inImageBuffer64", _imageBuffer64);
		_computeShader.SetBuffer(initSATBuffers, "outSATBuffer32", _outSATBuffer32);
		_computeShader.SetBuffer(initSATBuffers, "outSATBuffer64", _outSATBuffer64);
		_computeShader.Dispatch(initSATBuffers, numberOfGroups, 1, 1);
		Swap(ref _outSATBuffer32, ref _inSATBuffer32);
		Swap(ref _outSATBuffer64, ref _inSATBuffer64);
		
		_computeShader.SetBuffer(computeSAT, "inSATBuffer32", _inSATBuffer32);
		_computeShader.SetBuffer(computeSAT, "outSATBuffer32", _outSATBuffer32);
		_computeShader.SetBuffer(computeSAT, "inSATBuffer64", _inSATBuffer64);
		_computeShader.SetBuffer(computeSAT, "outSATBuffer64", _outSATBuffer64);
		_computeShader.Dispatch(computeSAT, numberOfGroups, 1, 1);
		Swap(ref _outSATBuffer32, ref _inSATBuffer32);
		Swap(ref _outSATBuffer64, ref _inSATBuffer64);
		
		_computeShader.SetBuffer(transposeSATBuffers, "inSATBuffer32", _inSATBuffer32);
		_computeShader.SetBuffer(transposeSATBuffers, "outSATBuffer32", _outSATBuffer32);
		_computeShader.SetBuffer(transposeSATBuffers, "inSATBuffer64", _inSATBuffer64);
		_computeShader.SetBuffer(transposeSATBuffers, "outSATBuffer64", _outSATBuffer64);
		_computeShader.Dispatch(transposeSATBuffers, numberOfGroups, 1, 1);
		Swap(ref _outSATBuffer32, ref _inSATBuffer32);
		Swap(ref _outSATBuffer64, ref _inSATBuffer64);
		
		_computeShader.SetBuffer(computeSAT, "inSATBuffer32", _inSATBuffer32);
		_computeShader.SetBuffer(computeSAT, "outSATBuffer32", _outSATBuffer32);
		_computeShader.SetBuffer(computeSAT, "inSATBuffer64", _inSATBuffer64);
		_computeShader.SetBuffer(computeSAT, "outSATBuffer64", _outSATBuffer64);
		_computeShader.Dispatch(computeSAT, numberOfGroups, 1, 1);
		Swap(ref _outSATBuffer32, ref _inSATBuffer32);
		Swap(ref _outSATBuffer64, ref _inSATBuffer64);
		
		_computeShader.SetBuffer(transposeSATBuffers, "inSATBuffer32", _inSATBuffer32);
		_computeShader.SetBuffer(transposeSATBuffers, "outSATBuffer32", _outSATBuffer32);
		_computeShader.SetBuffer(transposeSATBuffers, "inSATBuffer64", _inSATBuffer64);
		_computeShader.SetBuffer(transposeSATBuffers, "outSATBuffer64", _outSATBuffer64);
		_computeShader.Dispatch(transposeSATBuffers, numberOfGroups, 1, 1);
		Swap(ref _outSATBuffer32, ref _inSATBuffer32);
		Swap(ref _outSATBuffer64, ref _inSATBuffer64);

		_computeShader.SetBuffer(drawSATDifference, "inSATBuffer32", _inSATBuffer32);
		_computeShader.SetBuffer(drawSATDifference, "inSATBuffer64", _inSATBuffer64);
		_computeShader.SetTexture(drawSATDifference, "outRenderTexture", _stepRenderTexture);
		_computeShader.Dispatch(drawSATDifference, numberOfGroups, 1, 1);
		
		_computeShader.SetTexture(integrateIteration, "inRenderTexture", _stepRenderTexture);
		_computeShader.SetTexture(integrateIteration, "outRenderTexture", _integralRenderTexture);
		_computeShader.Dispatch(integrateIteration, numberOfGroups, 1, 1);
		
		_computeShader.SetTexture(drawResult, "inRenderTexture", _integralRenderTexture);
		_computeShader.SetTexture(drawResult, "outRenderTexture", _resultRenderTexture);
		_computeShader.Dispatch(drawResult, numberOfGroups, 1, 1);
    }
}
