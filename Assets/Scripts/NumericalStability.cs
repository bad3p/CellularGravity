using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class NumericalStability : MonoBehaviour
{
	const int GPUGroupSize = 128;
	
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
    private RenderTexture _renderTexture = null;
    
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

		_renderTexture = new RenderTexture( Resolution, Resolution, 0, RenderTextureFormat.ARGBHalf );		
		_renderTexture.enableRandomWrite = true;
		_renderTexture.filterMode = FilterMode.Point;
		_renderTexture.Create();
		
		_material.mainTexture = _renderTexture;

		_imageBuffer32 = new ComputeBuffer( Resolution * Resolution, sizeof(float) );
		_inSATBuffer32 = new ComputeBuffer( Resolution * Resolution, sizeof(float) * 3 );
		_outSATBuffer32 = new ComputeBuffer( Resolution * Resolution, sizeof(float) * 3 );
		_imageBuffer64 = new ComputeBuffer( Resolution * Resolution, sizeof(double) );
		_inSATBuffer64 = new ComputeBuffer( Resolution * Resolution, sizeof(double) * 3 );
		_outSATBuffer64 = new ComputeBuffer( Resolution * Resolution, sizeof(double) * 3 );
		_computeShader = Resources.Load<ComputeShader>( "NumericalStability" );

		float[] image32 = new float[Resolution * Resolution];
		double[] image64 = new double[Resolution * Resolution];
		for (int i = 0; i < Resolution * Resolution; i++)
		{
			float value = Random.Range( InitialBias.x, InitialBias.y );
			image32[i] = value;
			image64[i] = value;
		}
		
		_imageBuffer32.SetData(image32);
		_imageBuffer64.SetData(image64);
		
		int initSATBuffers = _computeShader.FindKernel("InitSATBuffers");
		int transposeSATBuffers = _computeShader.FindKernel("TransposeSATBuffers");
		int computeSAT = _computeShader.FindKernel("ComputeSAT");
		int drawSATDifference = _computeShader.FindKernel("DrawSATDifference");
		
		int numberOfGroups = Mathf.CeilToInt((float) (Resolution*Resolution) / GPUGroupSize);
        
		_computeShader.SetInt("width", Resolution);
		_computeShader.SetInt("height", Resolution);
		_computeShader.SetInt("satSampleSize", SATSampleSize);
		
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
		_computeShader.SetTexture(drawSATDifference, "outRenderTexture", _renderTexture);
		_computeShader.Dispatch(drawSATDifference, numberOfGroups, 1, 1);
	}
}
