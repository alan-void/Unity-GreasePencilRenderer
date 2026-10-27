using System.Collections.Generic;
using System.Runtime.InteropServices;
using Lineart.Pipeline;
using UnityEngine;

namespace Lineart.EdgeGraph
{
    /// <summary>
    /// Welded edge graph of a mesh: unique vertices (by position), triangles, edges with their (up to two)
    /// faces and a vertex -> edge adjacency list in CSR form. View independent, built once.
    /// When the mesh carries source polygon ids (MeshStreams.PolygonIdChannel) the graph matches the
    /// authored polygon mesh: triangulation diagonals are not edges, and every triangle uses its polygon's
    /// normal and centre.
    /// </summary>
    public sealed class EdgeTopology
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Vertex
        {
            public Vector3 position;
            public Vector3 lineNormal;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Face
        {
            public Vector3 normal; // mesh space, unit length
            public Vector3 center; // mesh space
        }

        public Vertex[] Vertices { get; private set; }
        /// <summary>3 welded vertex indices per triangle.</summary>
        public int[] Triangles { get; private set; }
        /// <summary>Normal and centre of the polygon each triangle belongs to.</summary>
        public Face[] Faces { get; private set; }
        /// <summary>4 ints per edge: v0, v1, face0, face1 (-1 on boundary edges).</summary>
        public int[] Edges { get; private set; }
        public int[] VertexEdgeOffsets { get; private set; }
        public int[] VertexEdges { get; private set; }

        public int VertexCount => Vertices.Length;
        public int FaceCount => Triangles.Length / 3;
        public int EdgeCount => Edges.Length / 4;

        public static EdgeTopology Build(Mesh mesh)
        {
            Vector3[] positions = mesh.vertices;
            Vector3[] lineNormals = MeshStreams.ReadLineNormals(mesh);
            int[] polygonIds = MeshStreams.ReadPolygonIds(mesh);
            int[] sourceTriangles = mesh.triangles;

            Vertex[] vertices = WeldVertices(positions, lineNormals, out int[] remap);

            // triangles in welded indices, degenerate ones dropped
            var triangles = new List<int>(sourceTriangles.Length);
            var trianglePolygons = new List<int>(sourceTriangles.Length / 3);
            for (int t = 0; t < sourceTriangles.Length; t += 3)
            {
                int a = remap[sourceTriangles[t]];
                int b = remap[sourceTriangles[t + 1]];
                int c = remap[sourceTriangles[t + 2]];
                if (a == b || b == c || c == a) continue;
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                trianglePolygons.Add(polygonIds != null ? polygonIds[sourceTriangles[t]] : t / 3);
            }

            Face[] faces = PolygonFaces(vertices, triangles, trianglePolygons);
            int[] edges = BuildEdges(mesh.name, triangles, trianglePolygons);
            BuildVertexEdges(vertices.Length, edges, out int[] offsets, out int[] vertexEdges);

            return new EdgeTopology
            {
                Vertices = vertices,
                Triangles = triangles.ToArray(),
                Faces = faces,
                Edges = edges,
                VertexEdgeOffsets = offsets,
                VertexEdges = vertexEdges,
            };
        }

        /// <summary>Merges vertices split by normals / UV seams; line normals are averaged.</summary>
        static Vertex[] WeldVertices(Vector3[] positions, Vector3[] lineNormals, out int[] remap)
        {
            var weldMap = new Dictionary<Vector3, int>(positions.Length);
            var welded = new List<Vertex>(positions.Length);
            var normalSums = new List<Vector3>(positions.Length);
            remap = new int[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                if (!weldMap.TryGetValue(positions[i], out int w))
                {
                    w = welded.Count;
                    weldMap.Add(positions[i], w);
                    welded.Add(new Vertex { position = positions[i] });
                    normalSums.Add(Vector3.zero);
                }
                remap[i] = w;
                normalSums[w] += lineNormals[i];
            }

            var vertices = welded.ToArray();
            for (int i = 0; i < vertices.Length; i++) vertices[i].lineNormal = normalSums[i].normalized;
            return vertices;
        }

        /// <summary>Per triangle: area weighted normal and vertex mean centre of its source polygon.</summary>
        static Face[] PolygonFaces(Vertex[] vertices, List<int> triangles, List<int> trianglePolygons)
        {
            var normals = new Dictionary<int, Vector3>();
            var polygonVertices = new Dictionary<int, HashSet<int>>();
            for (int f = 0; f < trianglePolygons.Count; f++)
            {
                int poly = trianglePolygons[f];
                Vector3 p0 = vertices[triangles[3 * f]].position;
                Vector3 p1 = vertices[triangles[3 * f + 1]].position;
                Vector3 p2 = vertices[triangles[3 * f + 2]].position;
                normals[poly] = (normals.TryGetValue(poly, out var n) ? n : Vector3.zero) + Vector3.Cross(p1 - p0, p2 - p0);
                if (!polygonVertices.TryGetValue(poly, out var set)) polygonVertices[poly] = set = new HashSet<int>();
                set.Add(triangles[3 * f]);
                set.Add(triangles[3 * f + 1]);
                set.Add(triangles[3 * f + 2]);
            }

            var centers = new Dictionary<int, Vector3>(polygonVertices.Count);
            foreach (var kv in polygonVertices)
            {
                Vector3 sum = Vector3.zero;
                foreach (int v in kv.Value) sum += vertices[v].position;
                centers[kv.Key] = sum / kv.Value.Count;
            }

            var faces = new Face[trianglePolygons.Count];
            for (int f = 0; f < faces.Length; f++)
            {
                int poly = trianglePolygons[f];
                faces[f] = new Face { normal = normals[poly].normalized, center = centers[poly] };
            }
            return faces;
        }

        /// <summary>Unique edges between different polygons, as (v0, v1, face0, face1).</summary>
        static int[] BuildEdges(string meshName, List<int> triangles, List<int> trianglePolygons)
        {
            var edgeLookup = new Dictionary<long, int>(triangles.Count);
            var edges = new List<int>(triangles.Count * 2);
            var interior = new HashSet<int>();
            int nonManifold = 0;
            for (int t = 0; t < triangles.Count; t += 3)
            {
                int face = t / 3;
                for (int c = 0; c < 3; c++)
                {
                    int v0 = triangles[t + c];
                    int v1 = triangles[t + (c + 1) % 3];
                    long key = v0 < v1 ? ((long)v0 << 32) | (uint)v1 : ((long)v1 << 32) | (uint)v0;
                    if (edgeLookup.TryGetValue(key, out int e))
                    {
                        if (edges[e * 4 + 3] >= 0)
                        {
                            nonManifold++;
                            continue;
                        }
                        edges[e * 4 + 3] = face;
                        if (trianglePolygons[edges[e * 4 + 2]] == trianglePolygons[face]) interior.Add(e);
                    }
                    else
                    {
                        edgeLookup.Add(key, edges.Count / 4);
                        edges.Add(v0);
                        edges.Add(v1);
                        edges.Add(face);
                        edges.Add(-1);
                    }
                }
            }

            if (nonManifold > 0)
            {
                Debug.LogWarning($"EdgeTopology: {meshName} has {nonManifold} non-manifold edge uses; extra faces are ignored.");
            }

            if (interior.Count == 0) return edges.ToArray();

            // drop triangulation diagonals (both sides belong to the same polygon)
            var kept = new List<int>(edges.Count - 4 * interior.Count);
            for (int e = 0; e < edges.Count / 4; e++)
            {
                if (interior.Contains(e)) continue;
                kept.AddRange(edges.GetRange(e * 4, 4));
            }
            return kept.ToArray();
        }

        static void BuildVertexEdges(int vertexCount, int[] edges, out int[] offsets, out int[] vertexEdges)
        {
            int edgeCount = edges.Length / 4;
            offsets = new int[vertexCount + 1];
            for (int e = 0; e < edgeCount; e++)
            {
                offsets[edges[e * 4] + 1]++;
                offsets[edges[e * 4 + 1] + 1]++;
            }
            for (int v = 0; v < vertexCount; v++) offsets[v + 1] += offsets[v];

            var fill = (int[])offsets.Clone();
            vertexEdges = new int[edgeCount * 2];
            for (int e = 0; e < edgeCount; e++)
            {
                vertexEdges[fill[edges[e * 4]]++] = e;
                vertexEdges[fill[edges[e * 4 + 1]]++] = e;
            }
        }
    }
}
