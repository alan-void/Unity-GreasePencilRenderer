using System.Collections.Generic;
using Lineart.Pipeline;
using UnityEngine;

namespace Lineart.Editor
{
    /// <summary>
    /// Converts the line art streams written by the Blender exporters into the runtime layout (MeshStreams):
    ///   in : uv1 = (line_normal.x, line_normal.y), uv2 = (line_normal.z, radius_var)   [Blender object space]
    ///   out: uv1 = line_normal (xyz, Unity mesh space), uv2 = (radius_var, 0)
    /// UVs are not touched by the FBX axis conversion, so the Blender -> Unity basis change is recovered by
    /// matching the baked normals against the imported mesh normals (best of the 48 signed axis permutations).
    /// </summary>
    public static class BlenderLineStreamConverter
    {
        public struct Result
        {
            public bool converted;
            public Matrix4x4 basis;
            /// <summary>Mean dot(converted line normal, mesh normal); ~1 for smooth meshes, lower for faceted ones.</summary>
            public float agreement;
        }

        public static Result Convert(Mesh mesh)
        {
            var result = new Result { basis = Matrix4x4.identity };
            var uv1 = new List<Vector2>();
            var uv2 = new List<Vector2>();
            mesh.GetUVs(1, uv1);
            mesh.GetUVs(2, uv2);
            Vector3[] normals = mesh.normals;
            int n = mesh.vertexCount;
            if (uv1.Count != n || uv2.Count != n || normals.Length != n || n == 0) return result;

            var blenderNormals = new Vector3[n];
            for (int i = 0; i < n; i++) blenderNormals[i] = new Vector3(uv1[i].x, uv1[i].y, uv2[i].x);

            Matrix4x4 basis = FindBasis(blenderNormals, normals, out float agreement);

            var lineNormals = new List<Vector3>(n);
            var radiusVar = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                lineNormals.Add(basis.MultiplyVector(blenderNormals[i]).normalized);
                radiusVar.Add(new Vector2(uv2[i].y, 0f));
            }
            mesh.SetUVs(MeshStreams.LineNormalChannel, lineNormals);
            mesh.SetUVs(MeshStreams.RadiusVariationChannel, radiusVar);

            result.converted = true;
            result.basis = basis;
            result.agreement = agreement;
            return result;
        }

        static Matrix4x4 FindBasis(Vector3[] source, Vector3[] target, out float bestAgreement)
        {
            int[][] permutations =
            {
                new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
                new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 },
            };

            Matrix4x4 best = Matrix4x4.identity;
            bestAgreement = float.NegativeInfinity;
            foreach (int[] perm in permutations)
            {
                for (int signs = 0; signs < 8; signs++)
                {
                    var m = Matrix4x4.zero;
                    m[3, 3] = 1f;
                    for (int row = 0; row < 3; row++)
                    {
                        m[row, perm[row]] = ((signs >> row) & 1) == 0 ? 1f : -1f;
                    }

                    float sum = 0f;
                    for (int i = 0; i < source.Length; i++) sum += Vector3.Dot(m.MultiplyVector(source[i]), target[i]);
                    float agreement = sum / source.Length;
                    if (agreement > bestAgreement)
                    {
                        bestAgreement = agreement;
                        best = m;
                    }
                }
            }
            return best;
        }
    }
}
