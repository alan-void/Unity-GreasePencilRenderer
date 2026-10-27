using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Lineart.Pipeline;
using UnityEngine;
using GreasePencil;

namespace Lineart.Silhouette
{
    /// <summary>
    /// Owns every GPU buffer used by a silhouette edge calculator: the optional source mesh buffers, the
    /// per-element stroke chain buffers (ping-pong pairs for pointer jumping) and the dense Grease Pencil
    /// point arrays the renderer draws from.
    /// </summary>
    public sealed class SilhouetteBufferContext : IDisposable
    {
        /// <summary>Number of stroke elements (faces, corners or half-edges depending on the edge finder).</summary>
        public int StrokeCount { get; private set; }
        public int FaceCount { get; private set; }
        public int DenseCapacity { get; private set; }

        // Source mesh (only for mesh based finders)
        public ComputeBuffer Vertices { get; private set; }
        public ComputeBuffer VertexRadiusVar { get; private set; }
        public ComputeBuffer Indices { get; private set; }
        public ComputeBuffer AdjIndices { get; private set; }

        // Per stroke element
        public ComputeBuffer Strokes { get; private set; }
        public ComputeBuffer StrokeAttribs { get; private set; }
        public ComputeBuffer NextPointerSrc { get; private set; }
        public ComputeBuffer NextPointerDst { get; private set; }
        public ComputeBuffer RankSrc { get; private set; }
        public ComputeBuffer RankDst { get; private set; }

        // Dense Grease Pencil layout
        public ComputeBuffer DenseNextPointerSrc { get; private set; }
        public ComputeBuffer DenseNextPointerDst { get; private set; }
        public ComputeBuffer DenseUStrokeSrc { get; private set; }
        public ComputeBuffer DenseUStrokeDst { get; private set; }

        public ComputeBuffer NumStrokesCounter { get; private set; }
        public ComputeBuffer NumStrokePointsCounter { get; private set; }

        public GraphicsBuffer DenseStrokes { get; private set; }
        public GraphicsBuffer DenseAttribs { get; private set; }
        public GraphicsBuffer Color { get; private set; }

        private SilhouetteBufferContext() { }

        /// <summary>Buffers for a finder that produces one stroke element per triangle corner.</summary>
        public static SilhouetteBufferContext CreateForSharp(Mesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));

            int[] indices = mesh.triangles;
            int cornerCount = indices.Length;

            var ctx = new SilhouetteBufferContext { FaceCount = cornerCount / 3 };
            ctx.UploadMesh(mesh, indices, SilhouetteMeshUtility.CalculateCornerAdjacency(indices, mesh.vertices));
            ctx.AllocateStrokeBuffers(cornerCount, cornerCount);
            return ctx;
        }

        /// <summary>Buffers for a finder that produces one stroke element per triangle.</summary>
        public static SilhouetteBufferContext CreateForSmooth(Mesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));

            int[] indices = mesh.triangles;
            int faceCount = indices.Length / 3;

            var ctx = new SilhouetteBufferContext { FaceCount = faceCount };
            ctx.UploadMesh(mesh, indices, SilhouetteMeshUtility.CalculateAdjacency(indices, mesh.vertices));
            ctx.AllocateStrokeBuffers(faceCount, 2 * faceCount);
            return ctx;
        }

        /// <summary>Stroke chain + dense buffers only; the caller owns its own source buffers.</summary>
        public static SilhouetteBufferContext CreateForElements(int elementCount, int denseCapacity)
        {
            var ctx = new SilhouetteBufferContext();
            ctx.AllocateStrokeBuffers(elementCount, denseCapacity);
            return ctx;
        }

        private void UploadMesh(Mesh mesh, int[] indices, int[] adjacency)
        {
            Vector3[] positions = mesh.vertices;
            // contours follow the smooth line normals when the mesh carries them (shading normals may be split)
            Vector3[] normals = MeshStreams.ReadLineNormals(mesh);
            var vertexData = new SilhouetteSourceVertex[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                vertexData[i] = new SilhouetteSourceVertex { position = positions[i], normal = normals[i] };
            }

            Vertices = new ComputeBuffer(vertexData.Length, Marshal.SizeOf(typeof(SilhouetteSourceVertex)));
            Vertices.SetData(vertexData);

            VertexRadiusVar = new ComputeBuffer(Mathf.Max(1, positions.Length), sizeof(float));
            VertexRadiusVar.SetData(ReadRadiusVariation(mesh, positions.Length));

            Indices = new ComputeBuffer(indices.Length, sizeof(int));
            Indices.SetData(indices);

            AdjIndices = new ComputeBuffer(adjacency.Length, sizeof(int));
            AdjIndices.SetData(adjacency);
        }

        /// <summary>Baked radius variation lives in uv2.x (see MeshStreams); defaults to 1.</summary>
        private static float[] ReadRadiusVariation(Mesh mesh, int vertexCount)
        {
            var result = new float[Mathf.Max(1, vertexCount)];
            for (int i = 0; i < result.Length; i++) result[i] = 1.0f;

            if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord2)) return result;

            var uvs = new List<Vector2>(vertexCount);
            mesh.GetUVs(MeshStreams.RadiusVariationChannel, uvs);
            for (int i = 0; i < uvs.Count && i < result.Length; i++) result[i] = uvs[i].x;
            return result;
        }

        private void AllocateStrokeBuffers(int elementCount, int denseCapacity)
        {
            StrokeCount = elementCount;
            DenseCapacity = denseCapacity;

            int elements = Mathf.Max(1, elementCount);
            int dense = Mathf.Max(1, denseCapacity);

            Strokes = new ComputeBuffer(elements, SilhouetteStrokeEdge.SizeOf);
            StrokeAttribs = new ComputeBuffer(elements, StrokePointAttributes.SizeOf);
            StrokeAttribs.SetData(DefaultAttributes(elements));
            NextPointerSrc = new ComputeBuffer(elements, sizeof(int));
            NextPointerDst = new ComputeBuffer(elements, sizeof(int));
            RankSrc = new ComputeBuffer(elements, sizeof(uint));
            RankDst = new ComputeBuffer(elements, sizeof(uint));

            DenseStrokes = new GraphicsBuffer(GraphicsBuffer.Target.Structured, dense, GreasePencilRenderer.GreasePencilStrokeVert.SizeOf);
            DenseAttribs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, dense, StrokePointAttributes.SizeOf);
            Color = new GraphicsBuffer(GraphicsBuffer.Target.Structured, dense, GreasePencilRenderer.GreasePencilColorVert.SizeOf);

            DenseNextPointerSrc = new ComputeBuffer(dense, sizeof(int));
            DenseNextPointerDst = new ComputeBuffer(dense, sizeof(int));
            DenseUStrokeSrc = new ComputeBuffer(dense, sizeof(float));
            DenseUStrokeDst = new ComputeBuffer(dense, sizeof(float));

            NumStrokesCounter = new ComputeBuffer(1, sizeof(uint));
            NumStrokePointsCounter = new ComputeBuffer(1, sizeof(uint));
        }

        /// <summary>Finders that don't write attributes (e.g. the sharp finder) keep these neutral values.</summary>
        private static StrokePointAttributes[] DefaultAttributes(int count)
        {
            var attribs = new StrokePointAttributes[count];
            for (int i = 0; i < count; i++)
            {
                attribs[i] = new StrokePointAttributes { weight = 1f, miter = 1f, radiusVar = 1f, meanWeight = 1f };
            }
            return attribs;
        }

        public void SwapNextPointers()
        {
            (NextPointerSrc, NextPointerDst) = (NextPointerDst, NextPointerSrc);
        }

        public void SwapRanks()
        {
            (RankSrc, RankDst) = (RankDst, RankSrc);
        }

        public void SwapDenseNextPointers()
        {
            (DenseNextPointerSrc, DenseNextPointerDst) = (DenseNextPointerDst, DenseNextPointerSrc);
        }

        public void SwapDenseUStrokeBuffers()
        {
            (DenseUStrokeSrc, DenseUStrokeDst) = (DenseUStrokeDst, DenseUStrokeSrc);
        }

        public void Dispose()
        {
            Vertices?.Release();
            VertexRadiusVar?.Release();
            Indices?.Release();
            AdjIndices?.Release();
            Strokes?.Release();
            StrokeAttribs?.Release();
            NextPointerSrc?.Release();
            NextPointerDst?.Release();
            RankSrc?.Release();
            RankDst?.Release();
            DenseNextPointerSrc?.Release();
            DenseNextPointerDst?.Release();
            DenseUStrokeSrc?.Release();
            DenseUStrokeDst?.Release();
            NumStrokesCounter?.Release();
            NumStrokePointsCounter?.Release();
            DenseStrokes?.Release();
            DenseAttribs?.Release();
            Color?.Release();
        }
    }
}
