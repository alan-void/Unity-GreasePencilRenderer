using UnityEngine;
using DefaultNamespace;
using Lineart.Pipeline;

/// <summary>
/// Smooth (interpolated normal) silhouette: one contour segment per triangle where N.V changes sign,
/// chained across adjacent triangles into Grease Pencil strokes.
/// </summary>
public class SilhouetteEdgeCalculator : MonoBehaviour, IGreasePencilEdgeCalculator
{
    const string FinderShaderPath = "Lineart/ComputeShaders/SilhouetteEdge";

    static readonly int NumFacesId = Shader.PropertyToID("_NumFaces");
    static readonly int SkipConcaveId = Shader.PropertyToID("_SkipConcave");
    static readonly int ConcaveToleranceId = Shader.PropertyToID("_ConcaveTolerance");

    public Camera viewCamera;
    public float radiusMultiplier = 1.0f;
    public float lateralShiftFactor = 1.0f;
    public float lateralShiftConstant;

    [Tooltip("Drop contour segments on concave folds (Blender: Skip Concave Edges).")]
    public bool skipConcaveEdges;
    [Tooltip("Fold tolerance (Blender: Concave Tolerance).")]
    public float concaveTolerance = 0.001f;

    private ComputeShader _silhouetteEdgeFinder;
    private int _findSilhouetteEdgeKernel;
    private StrokeChainRanker _ranker;
    private DenseStrokeBuilder _denseBuilder;

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

        _silhouetteEdgeFinder = LineartCompute.LoadInstance(FinderShaderPath);
        _findSilhouetteEdgeKernel = _silhouetteEdgeFinder.FindKernel("FindSilhouetteEdge");
        _ranker = new StrokeChainRanker();
        _denseBuilder = new DenseStrokeBuilder();

        _buffers = SilhouetteBufferContext.CreateForSmooth(_sourceMesh);
        BindFinderBuffers();
    }

    private void BindFinderBuffers()
    {
        int k = _findSilhouetteEdgeKernel;
        _silhouetteEdgeFinder.SetInt(NumFacesId, _buffers.FaceCount);
        _silhouetteEdgeFinder.SetBuffer(k, "_Vertices", _buffers.Vertices);
        _silhouetteEdgeFinder.SetBuffer(k, "_VertexRadiusVar", _buffers.VertexRadiusVar);
        _silhouetteEdgeFinder.SetBuffer(k, "_Indices", _buffers.Indices);
        _silhouetteEdgeFinder.SetBuffer(k, "_AdjIndices", _buffers.AdjIndices);
        _silhouetteEdgeFinder.SetBuffer(k, "_outStrokes", _buffers.Strokes);
        _silhouetteEdgeFinder.SetBuffer(k, "_outAttribs", _buffers.StrokeAttribs);
    }

    public void CalculateEdges()
    {
        if (!IsSetupValid()) return;

        var cameraParams = StrokeCameraParams.From(viewCamera);
        Matrix4x4 objectToWorld = transform.localToWorldMatrix;

        cameraParams.Apply(_silhouetteEdgeFinder, objectToWorld);
        _silhouetteEdgeFinder.SetMatrix(SilhouetteShaderIDs.ObjectToWorld, objectToWorld);
        _silhouetteEdgeFinder.SetMatrix(SilhouetteShaderIDs.ObjectToWorldIt, objectToWorld.inverse.transpose);
        _silhouetteEdgeFinder.SetInt(SkipConcaveId, skipConcaveEdges ? 1 : 0);
        _silhouetteEdgeFinder.SetFloat(ConcaveToleranceId, concaveTolerance);

        int elementCount = _buffers.StrokeCount;
        _silhouetteEdgeFinder.Dispatch(_findSilhouetteEdgeKernel, LineartCompute.ThreadGroups(elementCount), 1, 1);

        _ranker.Run(_buffers, elementCount, LineartCompute.PointerJumpIterations(elementCount));

        _denseBuilder.Build(_buffers, elementCount, cameraParams, objectToWorld, new DenseStrokeBuilder.Settings
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

    void OnDestroy()
    {
        _buffers?.Dispose();
        _buffers = null;
        _ranker?.Dispose();
        _denseBuilder?.Dispose();
        LineartCompute.Release(_silhouetteEdgeFinder);
    }

    public int GetMaximumBufferLength() => _buffers?.DenseCapacity ?? 0;
    public GraphicsBuffer GetStrokeBuffer() => _buffers?.DenseStrokes;
    public GraphicsBuffer GetColorBuffer() => _buffers?.Color;
    public GraphicsBuffer GetStrokeAttributeBuffer() => _buffers?.DenseAttribs;
}
