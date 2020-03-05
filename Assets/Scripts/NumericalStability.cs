using System.IO;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class NumericalStability : MonoBehaviour
{
	const int GPUGroupSize = 128;

	[Header("UI")] 
	public Text NumIterationsRemaining;
	public Text BatchResolution;
	public Text BatchSampleSize;
	public Text BatchExponent;
	
    [Header("Settings")]
    public int     Resolution = 256;
    public Vector2 InitialBias = new Vector2( 0.0f, 1.0f );
    public int     SATSampleSize = 8;
    public int     NumIterations = 10;
    public bool    BatchMode = false;
    
    private Image    _image;
    private Material _material;
    private ComputeBuffer _imageBuffer32  = null;
    private ComputeBuffer _inSATBuffer32  = null;
    private ComputeBuffer _outSATBuffer32 = null;
    private ComputeBuffer _imageBuffer64  = null;
    private ComputeBuffer _inSATBuffer64  = null;
    private ComputeBuffer _outSATBuffer64 = null;
    private ComputeBuffer _statsBuffer = null;
    private ComputeShader _computeShader  = null;
    private RenderTexture _stepRenderTexture = null;
    private RenderTexture _integralRenderTexture = null;
    private RenderTexture _resultRenderTexture = null;
    private int _numIterations = 0;
    private float[] _image32 = new float[0];
    private double[] _image64 = new double[0];

    public bool Completed
    {
	    get { return _numIterations == NumIterations;  }
    }
    
    public void Restart(int resolution, Vector2 initialBias, int satSampleSize, int numIterations)
    {
	    if (resolution != Resolution)
	    {
		    _image32 = new float[0];
		    _image64 = new double[0];
		    
		    _stepRenderTexture.Release();
		    _stepRenderTexture = null;
		    
		    _integralRenderTexture.Release();
		    _integralRenderTexture = null;
		    
		    _resultRenderTexture.Release();
		    _resultRenderTexture = null;
		    
		    _imageBuffer32.Release();
		    _imageBuffer32 = null;

		    _inSATBuffer32.Release();
		    _inSATBuffer32 = null;
		    
		    _outSATBuffer32.Release();
		    _outSATBuffer32 = null;
		    
		    _imageBuffer64.Release();
		    _imageBuffer64 = null;

		    _inSATBuffer64.Release();
		    _inSATBuffer64 = null;
		    
		    _outSATBuffer64.Release();
		    _outSATBuffer64 = null;
		    
		    _statsBuffer.Release();
		    _statsBuffer = null;
	    }
	    
		Resolution = resolution;
		InitialBias = initialBias;
		SATSampleSize = satSampleSize;
		NumIterations = numIterations;
		_numIterations = 0;
		
		Start();
    }
    
    public void GetReport(out double posDiff, out double valDiff)
    {
	    double[] stats = new double[NumIterations * 2];
	    _statsBuffer.GetData( stats );

	    posDiff = 0.0;
	    valDiff = 0.0;
	    for (int i = 0; i < stats.Length / 2; i++)
	    {
		    posDiff += stats[i * 2];
		    valDiff += stats[i * 2 + 1];
	    }
	    posDiff /= NumIterations;
	    valDiff /= NumIterations;
    }
    
    private static void Swap(ref ComputeBuffer cbRef0, ref ComputeBuffer cbRef1)
    {
	    ComputeBuffer temp = cbRef0;
	    cbRef0 = cbRef1;
	    cbRef1 = temp;
    }

    private void Start()
    {
	    BatchResolution.text = _batchResolution.ToString();
	    BatchSampleSize.text = _batchSampleSize.ToString();
	    BatchExponent.text = _batchValueExponent.ToString();
	    BatchResolution.gameObject.SetActive( BatchMode );
		BatchSampleSize.gameObject.SetActive( BatchMode );
		BatchExponent.gameObject.SetActive( BatchMode );

		if (_image == null)
		{
			_image = GetComponent<Image>();
		}

		if (_material == null)
		{
			_material = new Material(Shader.Find("Unlit/Texture"));
			_image.material = _material;
			_image.SetMaterialDirty();
		}

		if (_stepRenderTexture == null)
		{
			_stepRenderTexture = new RenderTexture(Resolution, Resolution, 0, RenderTextureFormat.ARGBHalf);
			_stepRenderTexture.enableRandomWrite = true;
			_stepRenderTexture.filterMode = FilterMode.Point;
			_stepRenderTexture.Create();
		}

		if (_integralRenderTexture == null)
		{
			_integralRenderTexture = new RenderTexture(Resolution, Resolution, 0, RenderTextureFormat.ARGBHalf);
			_integralRenderTexture.enableRandomWrite = true;
			_integralRenderTexture.filterMode = FilterMode.Point;
			_integralRenderTexture.Create();
		}

		if (_resultRenderTexture == null)
		{
			_resultRenderTexture = new RenderTexture(Resolution, Resolution, 0, RenderTextureFormat.ARGBHalf);
			_resultRenderTexture.enableRandomWrite = true;
			_resultRenderTexture.filterMode = FilterMode.Bilinear;
			_resultRenderTexture.Create();
			_material.mainTexture = _resultRenderTexture;
			_image.SetMaterialDirty();
		}

		if (_imageBuffer32 == null)
		{
			_imageBuffer32 = new ComputeBuffer(Resolution * Resolution, sizeof(float));
		}
		if (_inSATBuffer32 == null)
		{
			_inSATBuffer32 = new ComputeBuffer(Resolution * Resolution, sizeof(float) * 3);
		}
		if (_outSATBuffer32 == null)
		{
			_outSATBuffer32 = new ComputeBuffer(Resolution * Resolution, sizeof(float) * 3);
		}
		if (_imageBuffer64 == null)
		{
			_imageBuffer64 = new ComputeBuffer(Resolution * Resolution, sizeof(double));
		}
		if (_inSATBuffer64 == null)
		{
			_inSATBuffer64 = new ComputeBuffer(Resolution * Resolution, sizeof(double) * 3);
		}
		if (_outSATBuffer64 == null)
		{
			_outSATBuffer64 = new ComputeBuffer(Resolution * Resolution, sizeof(double) * 3);
		}
		if (_statsBuffer == null)
		{
			_statsBuffer = new ComputeBuffer(NumIterations * 2, sizeof(double));
		}
		if (_computeShader == null)
		{
			_computeShader = Resources.Load<ComputeShader>("NumericalStability");
		}

		int numberOfGroups = Mathf.CeilToInt((float) (Resolution*Resolution) / GPUGroupSize);
		int resetRenderTexture = _computeShader.FindKernel("ResetRenderTexture");
		_computeShader.SetInt("width", Resolution);
		_computeShader.SetInt("height", Resolution);
		_computeShader.SetTexture(resetRenderTexture, "outRenderTexture", _integralRenderTexture);
		_computeShader.Dispatch(resetRenderTexture, numberOfGroups, 1, 1);

		if (_image32.Length == 0)
		{
			_image32 = new float[Resolution * Resolution];
		}
		if (_image64.Length == 0)
		{
			_image64 = new double[Resolution * Resolution];
		}
	}

    void Update()
    {
	    if (_numIterations >= NumIterations && !BatchMode)
	    {
		    return;
	    }

	    bool processBatchMode = BatchMode && ( _numIterations + 1 == NumIterations );

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

		_computeShader.SetBuffer(drawSATDifference, "statsBuffer", _statsBuffer);
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

		if (processBatchMode)
		{
			ProcessBatchMode();
		}
    }
    
    private int _batchResolution = 256;
    private int _batchValueExponent = 1;
    private int _batchSampleSize = 2;
    private int _batchStep = 0;

    void ProcessBatchMode()
    {
	    if (_batchStep > 0)
	    {
		    string path = "NumericalStability.txt";
		    if (!File.Exists(path)) 
		    {
			    // Create a file to write to.
			    using (StreamWriter sw = File.CreateText(path)) 
			    {
				    sw.WriteLine("\n");
			    }	
		    }

		    double posDiff = 0.0;
		    double valDiff = 0.0;
		    GetReport( out posDiff, out valDiff );

		    string s = _batchResolution.ToString() + " " + 
		               _batchValueExponent.ToString() + " " +
		               _batchSampleSize.ToString() + " " + 
		               posDiff.ToString("F7") + " " + 
		               valDiff.ToString("F7");
		    
		    using (StreamWriter sw = File.AppendText(path)) 
		    {
			    sw.WriteLine(s);
		    }	
	    }

	    _batchSampleSize *= 2;
	    if (_batchSampleSize > _batchResolution / 4)
	    {
		    _batchSampleSize = 2;
		    
		    _batchValueExponent++;
		    if (_batchValueExponent > 3)
		    {
			    _batchValueExponent = 1;

			    _batchResolution *= 2;
			    if (_batchResolution > 1024)
			    {
				    BatchMode = false;
				    return;
			    }
		    }
	    }
	    
	    Restart(_batchResolution, new Vector2(0.0f, Mathf.Pow(1.0f, _batchValueExponent)), _batchSampleSize,NumIterations);

	    _batchStep++;
    }
}
