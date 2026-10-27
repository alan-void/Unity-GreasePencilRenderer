using System.Runtime.InteropServices;
using Lineart.Pipeline;
using UnityEngine;
using Lineart.Silhouette;

namespace Lineart.EdgeGraph
{
    /// <summary>
    /// Edge based outlines for faceted meshes: silhouette edges plus "silhouetteness" falloff lines on edges
    /// close to grazing, joined into strokes through branch vertices.
    /// Port of Blender's GN "Silhouette Edges" (as used by "Silhouetteness Transfer").
    /// Point weights / miter / line normal are passed on to a StrokeStylizer.
    /// </summary>
    public class EdgeSilhouetteCalculator : MonoBehaviour, IGreasePencilEdgeCalculator
    {
        const string ShaderPath = "GreasePencilRenderer/Lineart/ComputeShaders/EdgeSilhouette";

        static class Ids
        {
            public static readonly int NumVertices = Shader.PropertyToID("_EgNumVertices");
            public static readonly int NumFaces = Shader.PropertyToID("_EgNumFaces");
            public static readonly int NumEdges = Shader.PropertyToID("_EgNumEdges");
            public static readonly int Vertices = Shader.PropertyToID("_EgVertices");
            public static readonly int Triangles = Shader.PropertyToID("_EgTriangles");
            public static readonly int FacesLocal = Shader.PropertyToID("_EgFacesLocal");
            public static readonly int Edges = Shader.PropertyToID("_EgEdges");
            public static readonly int VertEdgeOffsets = Shader.PropertyToID("_EgVertEdgeOffsets");
            public static readonly int VertEdges = Shader.PropertyToID("_EgVertEdges");
            public static readonly int WorldVertices = Shader.PropertyToID("_EgWorldVertices");
            public static readonly int Faces = Shader.PropertyToID("_EgFaces");
            public static readonly int EdgeStates = Shader.PropertyToID("_EgEdgeStates");
            public static readonly int VertexStates = Shader.PropertyToID("_EgVertexStates");
            public static readonly int OutStrokes = Shader.PropertyToID("_outStrokes");
            public static readonly int OutAttribs = Shader.PropertyToID("_outAttribs");

            public static readonly int IncludeBoundary = Shader.PropertyToID("_IncludeBoundary");
            public static readonly int SkipConcave = Shader.PropertyToID("_SkipConcave");
            public static readonly int ConcaveTolerance = Shader.PropertyToID("_ConcaveTolerance");
            public static readonly int OutlineFalloff = Shader.PropertyToID("_OutlineFalloff");
            public static readonly int UndersideFalloff = Shader.PropertyToID("_UndersideFalloff");
            public static readonly int ScaleBySpan = Shader.PropertyToID("_ScaleBySpan");
            public static readonly int FullSpan = Shader.PropertyToID("_FullSpan");
            public static readonly int ScaleByPolar = Shader.PropertyToID("_ScaleByPolar");
            public static readonly int JoinBranches = Shader.PropertyToID("_JoinBranches");
            public static readonly int StraightnessWeight = Shader.PropertyToID("_StraightnessWeight");
        }

        public Camera viewCamera;

        [Header("Detection")]
        public bool includeBoundary = true;
        public bool skipConcaveEdges = true;
        [Tooltip("Radians")] public float concaveTolerance = 0.1745f;

        [Header("Silhouetteness falloff")]
        [Tooltip("Elevation angle (radians) over which edge weight fades to 0 next to the silhouette. 0 disables.")]
        public float outlineFalloff = 0.349f;
        [Tooltip("Also fade in edges whose faces both point away from the camera.")]
        public bool undersideFalloff = true;
        [Tooltip("Scale the falloff by dihedral angle / Full Span (flat edges fade faster).")]
        public bool scaleBySpan = true;
        [Tooltip("Radians")] public float fullSpan = 0.7854f;
        [Tooltip("Scale the falloff by sin^2 of the edge to view angle.")]
        public bool scaleByPolarAngle = true;

        [Header("Branch join")]
        public bool joinBranches = true;
        public float straightnessWeight = 0.1f;

        ComputeShader _shader;
        int _transformVertices;
        int _computeFaces;
        int _classifyEdges;
        int _resolveVertices;
        int _buildHalfEdges;
        int _dedupeChains;

        StrokeChainRanker _ranker;
        DenseStrokeBuilder _denseBuilder;

        EdgeTopology _topology;
        ComputeBuffer _vertices;
        ComputeBuffer _triangles;
        ComputeBuffer _facesLocal;
        ComputeBuffer _edges;
        ComputeBuffer _vertEdgeOffsets;
        ComputeBuffer _vertEdges;
        ComputeBuffer _worldVertices;
        ComputeBuffer _faces;
        ComputeBuffer _edgeStates;
        ComputeBuffer _vertexStates;
        SilhouetteBufferContext _buffers;

        public Camera ViewCamera => viewCamera;
        int ElementCount => 4 * _topology.EdgeCount;

        void Awake()
        {
            viewCamera = ViewCameraResolver.Resolve(viewCamera);
            Mesh mesh = GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null || !SystemInfo.supportsComputeShaders) return;
            if (!mesh.isReadable)
            {
                Debug.LogError($"{name}: mesh '{mesh.name}' must be Read/Write enabled for edge outlines.", this);
                return;
            }

            _shader = LineartCompute.LoadInstance(ShaderPath);
            _transformVertices = _shader.FindKernel("TransformVertices");
            _computeFaces = _shader.FindKernel("ComputeFaces");
            _classifyEdges = _shader.FindKernel("ClassifyEdges");
            _resolveVertices = _shader.FindKernel("ResolveVertices");
            _buildHalfEdges = _shader.FindKernel("BuildHalfEdges");
            _dedupeChains = _shader.FindKernel("DedupeChains");
            _ranker = new StrokeChainRanker();
            _denseBuilder = new DenseStrokeBuilder();

            _topology = EdgeTopology.Build(mesh);
            AllocateBuffers();
        }

        void AllocateBuffers()
        {
            int v = Mathf.Max(1, _topology.VertexCount);
            int f = Mathf.Max(1, _topology.FaceCount);
            int e = Mathf.Max(1, _topology.EdgeCount);

            _vertices = Upload(_topology.Vertices, Marshal.SizeOf<EdgeTopology.Vertex>());
            _triangles = Upload(_topology.Triangles, 3 * sizeof(int), 3);
            _facesLocal = Upload(_topology.Faces, Marshal.SizeOf<EdgeTopology.Face>());
            _edges = Upload(_topology.Edges, 4 * sizeof(int), 4);
            _vertEdgeOffsets = Upload(_topology.VertexEdgeOffsets, sizeof(int));
            _vertEdges = Upload(_topology.VertexEdges, sizeof(int));

            _worldVertices = new ComputeBuffer(v, Marshal.SizeOf<EdgeTopology.Vertex>());
            _faces = new ComputeBuffer(f, 6 * sizeof(float));
            _edgeStates = new ComputeBuffer(e, 5 * sizeof(float));
            _vertexStates = new ComputeBuffer(v, 3 * sizeof(int));

            // half-edges + terminal points; each stroke adds up to 3 padding slots
            _buffers = SilhouetteBufferContext.CreateForElements(ElementCount, ElementCount + 64);

            _shader.SetInt(Ids.NumVertices, _topology.VertexCount);
            _shader.SetInt(Ids.NumFaces, _topology.FaceCount);
            _shader.SetInt(Ids.NumEdges, _topology.EdgeCount);
        }

        static ComputeBuffer Upload<T>(T[] data, int stride, int elementsPerItem = 1) where T : struct
        {
            int count = Mathf.Max(1, data.Length / elementsPerItem);
            var buffer = new ComputeBuffer(count, stride);
            if (data.Length > 0) buffer.SetData(data);
            return buffer;
        }

        public void CalculateEdges()
        {
            if (_buffers == null || viewCamera == null || _topology.EdgeCount == 0) return;

            var cameraParams = StrokeCameraParams.From(viewCamera);
            Matrix4x4 objectToWorld = transform.localToWorldMatrix;
            cameraParams.Apply(_shader, objectToWorld);
            _shader.SetMatrix(SilhouetteShaderIDs.ObjectToWorld, objectToWorld);
            _shader.SetMatrix(SilhouetteShaderIDs.ObjectToWorldIt, objectToWorld.inverse.transpose);
            SetDetectionUniforms();

            Dispatch(_transformVertices, _topology.VertexCount);
            Dispatch(_computeFaces, _topology.FaceCount);
            Dispatch(_classifyEdges, _topology.EdgeCount);
            Dispatch(_resolveVertices, _topology.VertexCount);
            Dispatch(_buildHalfEdges, 2 * _topology.EdgeCount);

            int iterations = LineartCompute.PointerJumpIterations(ElementCount);
            _ranker.FindTails(_buffers, ElementCount, iterations);
            Dispatch(_dedupeChains, ElementCount);
            _ranker.Rank(_buffers, ElementCount, iterations);

            _denseBuilder.Build(_buffers, ElementCount, cameraParams, objectToWorld, new DenseStrokeBuilder.Settings
            {
                radiusMultiplier = 1.0f,
                lateralShiftFactor = 0.0f,
                lateralShiftConstant = 0.0f,
            });
        }

        void SetDetectionUniforms()
        {
            _shader.SetInt(Ids.IncludeBoundary, includeBoundary ? 1 : 0);
            _shader.SetInt(Ids.SkipConcave, skipConcaveEdges ? 1 : 0);
            _shader.SetFloat(Ids.ConcaveTolerance, concaveTolerance);
            _shader.SetFloat(Ids.OutlineFalloff, outlineFalloff);
            _shader.SetInt(Ids.UndersideFalloff, undersideFalloff ? 1 : 0);
            _shader.SetInt(Ids.ScaleBySpan, scaleBySpan ? 1 : 0);
            _shader.SetFloat(Ids.FullSpan, fullSpan);
            _shader.SetInt(Ids.ScaleByPolar, scaleByPolarAngle ? 1 : 0);
            _shader.SetInt(Ids.JoinBranches, joinBranches ? 1 : 0);
            _shader.SetFloat(Ids.StraightnessWeight, straightnessWeight);
        }

        void Dispatch(int kernel, int count)
        {
            int groups = LineartCompute.ThreadGroups(count);
            if (groups == 0) return;
            _shader.SetBuffer(kernel, Ids.Vertices, _vertices);
            _shader.SetBuffer(kernel, Ids.Triangles, _triangles);
            _shader.SetBuffer(kernel, Ids.FacesLocal, _facesLocal);
            _shader.SetBuffer(kernel, Ids.Edges, _edges);
            _shader.SetBuffer(kernel, Ids.VertEdgeOffsets, _vertEdgeOffsets);
            _shader.SetBuffer(kernel, Ids.VertEdges, _vertEdges);
            _shader.SetBuffer(kernel, Ids.WorldVertices, _worldVertices);
            _shader.SetBuffer(kernel, Ids.Faces, _faces);
            _shader.SetBuffer(kernel, Ids.EdgeStates, _edgeStates);
            _shader.SetBuffer(kernel, Ids.VertexStates, _vertexStates);
            _shader.SetBuffer(kernel, Ids.OutStrokes, _buffers.Strokes);
            _shader.SetBuffer(kernel, Ids.OutAttribs, _buffers.StrokeAttribs);
            _shader.Dispatch(kernel, groups, 1, 1);
        }

        void OnDestroy()
        {
            _buffers?.Dispose();
            _buffers = null;
            foreach (var buffer in new[] { _vertices, _triangles, _facesLocal, _edges, _vertEdgeOffsets, _vertEdges, _worldVertices, _faces, _edgeStates, _vertexStates })
            {
                buffer?.Release();
            }
            _ranker?.Dispose();
            _denseBuilder?.Dispose();
            LineartCompute.Release(_shader);
        }

        public int GetMaximumBufferLength() => _buffers?.DenseCapacity ?? 0;
        public GraphicsBuffer GetStrokeBuffer() => _buffers?.DenseStrokes;
        public GraphicsBuffer GetColorBuffer() => _buffers?.Color;
        public GraphicsBuffer GetStrokeAttributeBuffer() => _buffers?.DenseAttribs;
    }
}
