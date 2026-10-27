using UnityEngine;
using DefaultNamespace;
using Lineart.Pipeline;

/// <summary>
/// Flat-shaded (split normal) silhouette: one stroke element per triangle corner whose edge separates a
/// front-facing and a back-facing face.
/// </summary>
public class SharpSilhouetteEdgeCalculator : MonoBehaviour, IGreasePencilEdgeCalculator
{
    const string FinderShaderPath = "Lineart/ComputeShaders/SharpSilhouetteEdge";
    const string ChainShaderPath = "Lineart/ComputeShaders/SharpEdgesToStrokes";

    static readonly int NumVertsId = Shader.PropertyToID("_NumVerts");
    static readonly int StrokesId = Shader.PropertyToID("_strokes");

    public Camera viewCamera;
    public float radiusMultiplier = 1.0f;
    public float lateralShiftFactor = 1.0f;
    public float lateralShiftConstant;

    private ComputeShader _sharpSilhouetteEdgeFinder;
    private ComputeShader _sharpEdgesToStrokes;
    private DenseStrokeBuilder _denseBuilder;

    private int _findSharpSilhouetteEdgeKernel;
    private int _initializeKernel;
    private int _reduceKernel;
    private int _findStrokeTailKernel;
    private int _resetNextKernel;
    private int _initRanksKernel;
    private int _listRankKernel;
    private int _finalizeRanksKernel;

    private Mesh _sourceMesh;
    private SilhouetteBufferContext _buffers;

    public Camera ViewCamera => viewCamera;

    private void Awake()
    {
        _sourceMesh = GetComponent<MeshFilter>()?.sharedMesh;
        viewCamera = ViewCameraResolver.Resolve(viewCamera);

        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.LogError("Compute shaders are not supported on this platform.");
            return;
        }

        if (_sourceMesh == null)
        {
            return;
        }

        _sharpSilhouetteEdgeFinder = LineartCompute.LoadInstance(FinderShaderPath);
        _sharpEdgesToStrokes = LineartCompute.LoadInstance(ChainShaderPath);
        _denseBuilder = new DenseStrokeBuilder();
        InitializeKernels();

        _buffers = SilhouetteBufferContext.CreateForSharp(_sourceMesh);
        BindFinderBuffers();
    }

    void InitializeKernels()
    {
        _findSharpSilhouetteEdgeKernel = _sharpSilhouetteEdgeFinder.FindKernel("FindSilhouetteEdge");

        _initializeKernel = _sharpEdgesToStrokes.FindKernel("Initialize");
        _reduceKernel = _sharpEdgesToStrokes.FindKernel("Reduce");
        _findStrokeTailKernel = _sharpEdgesToStrokes.FindKernel("FindStrokeTail");
        _resetNextKernel = _sharpEdgesToStrokes.FindKernel("ResetNextPointer");
        _initRanksKernel = _sharpEdgesToStrokes.FindKernel("InitializeRanks");
        _listRankKernel = _sharpEdgesToStrokes.FindKernel("CalculateRanks");
        _finalizeRanksKernel = _sharpEdgesToStrokes.FindKernel("FinalizeRanks");
    }

    void BindFinderBuffers()
    {
        int k = _findSharpSilhouetteEdgeKernel;
        _sharpSilhouetteEdgeFinder.SetBuffer(k, "_Vertices", _buffers.Vertices);
        _sharpSilhouetteEdgeFinder.SetBuffer(k, "_Indices", _buffers.Indices);
        _sharpSilhouetteEdgeFinder.SetBuffer(k, "_AdjIndices", _buffers.AdjIndices);
        _sharpSilhouetteEdgeFinder.SetBuffer(k, "_outStrokes", _buffers.Strokes);
        _sharpSilhouetteEdgeFinder.SetInt(NumVertsId, _buffers.StrokeCount);
        _sharpEdgesToStrokes.SetInt(NumVertsId, _buffers.StrokeCount);
    }

    public void CalculateEdges()
    {
        if (!IsSetupValid()) return;

        Matrix4x4 objectToWorld = transform.localToWorldMatrix;
        _sharpSilhouetteEdgeFinder.SetVector(SilhouetteShaderIDs.WorldSpaceCameraPos, viewCamera.transform.position);
        _sharpSilhouetteEdgeFinder.SetMatrix(SilhouetteShaderIDs.ObjectToWorld, objectToWorld);
        _sharpSilhouetteEdgeFinder.SetMatrix(SilhouetteShaderIDs.ObjectToWorldIt, objectToWorld.inverse.transpose);

        int elementCount = _buffers.StrokeCount;
        int groups = LineartCompute.ThreadGroups(elementCount);
        if (groups == 0) return;

        _sharpSilhouetteEdgeFinder.Dispatch(_findSharpSilhouetteEdgeKernel, groups, 1, 1);
        RunEdgesToStrokePasses(groups, LineartCompute.PointerJumpIterations(elementCount));

        _denseBuilder.Build(_buffers, elementCount, StrokeCameraParams.From(viewCamera), objectToWorld, new DenseStrokeBuilder.Settings
        {
            radiusMultiplier = radiusMultiplier,
            lateralShiftFactor = lateralShiftFactor,
            lateralShiftConstant = lateralShiftConstant,
        });
    }

    private bool IsSetupValid()
    {
        return _sourceMesh != null && viewCamera != null && _buffers != null;
    }

    private void Dispatch(int kernel, int groups)
    {
        _sharpEdgesToStrokes.SetBuffer(kernel, StrokesId, _buffers.Strokes);
        _sharpEdgesToStrokes.SetBuffer(kernel, SilhouetteShaderIDs.NextPointerSrc, _buffers.NextPointerSrc);
        _sharpEdgesToStrokes.SetBuffer(kernel, SilhouetteShaderIDs.NextPointerDst, _buffers.NextPointerDst);
        _sharpEdgesToStrokes.SetBuffer(kernel, SilhouetteShaderIDs.RankSrc, _buffers.RankSrc);
        _sharpEdgesToStrokes.SetBuffer(kernel, SilhouetteShaderIDs.RankDst, _buffers.RankDst);
        _sharpEdgesToStrokes.Dispatch(kernel, groups, 1, 1);
    }

    private void RunEdgesToStrokePasses(int groups, int iterations)
    {
        Dispatch(_initializeKernel, groups);
        _buffers.SwapNextPointers();

        for (int i = 0; i < iterations; ++i)
        {
            Dispatch(_reduceKernel, groups);
            _buffers.SwapNextPointers();
        }

        Dispatch(_resetNextKernel, groups);
        _buffers.SwapNextPointers();

        for (int i = 0; i < iterations; ++i)
        {
            Dispatch(_findStrokeTailKernel, groups);
            _buffers.SwapNextPointers();
        }

        Dispatch(_resetNextKernel, groups);
        _buffers.SwapNextPointers();

        Dispatch(_initRanksKernel, groups);
        _buffers.SwapRanks();

        for (int i = 0; i < iterations; ++i)
        {
            Dispatch(_listRankKernel, groups);
            _buffers.SwapNextPointers();
            _buffers.SwapRanks();
        }

        Dispatch(_finalizeRanksKernel, groups);
    }

    void OnDestroy()
    {
        _buffers?.Dispose();
        _buffers = null;
        _denseBuilder?.Dispose();
        LineartCompute.Release(_sharpSilhouetteEdgeFinder);
        LineartCompute.Release(_sharpEdgesToStrokes);
    }

    public int GetMaximumBufferLength() => _buffers?.DenseCapacity ?? 0;
    public GraphicsBuffer GetStrokeBuffer() => _buffers?.DenseStrokes;
    public GraphicsBuffer GetColorBuffer() => _buffers?.Color;
    public GraphicsBuffer GetStrokeAttributeBuffer() => _buffers?.DenseAttribs;
}
