using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lineart.Pipeline
{
    /// <summary>
    /// Vertex stream convention for meshes carrying baked line art attributes
    /// (written by Tools/Blender exporters + the import postprocessor):
    ///   uv1.xyz = line normal (smooth vertex normal, mesh local space)
    ///   uv2.x   = radius variation factor
    ///   uv3.x   = source polygon index (optional; tells real polygon edges from triangulation diagonals)
    ///   color.r = ambient occlusion (shading only)
    /// </summary>
    public static class MeshStreams
    {
        public const int LineNormalChannel = 1;
        public const int RadiusVariationChannel = 2;
        public const int PolygonIdChannel = 3;

        /// <summary>Source polygon index per vertex, or null when the mesh carries none.</summary>
        public static int[] ReadPolygonIds(Mesh mesh)
        {
            if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord3)) return null;
            var list = new List<Vector2>(mesh.vertexCount);
            mesh.GetUVs(PolygonIdChannel, list);
            var ids = new int[list.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = Mathf.RoundToInt(list[i].x);
            return ids;
        }

        public static bool HasLineNormals(Mesh mesh) => mesh.HasVertexAttribute(VertexAttribute.TexCoord1) &&
                                                         mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord1) >= 3;

        /// <summary>Baked line normals, or the mesh normals when the mesh carries none.</summary>
        public static Vector3[] ReadLineNormals(Mesh mesh)
        {
            if (!HasLineNormals(mesh)) return mesh.normals;
            var list = new List<Vector3>(mesh.vertexCount);
            mesh.GetUVs(LineNormalChannel, list);
            return list.ToArray();
        }
    }
}
