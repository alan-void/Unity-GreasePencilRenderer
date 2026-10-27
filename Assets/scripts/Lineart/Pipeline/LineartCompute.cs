using UnityEngine;

namespace Lineart.Pipeline
{
    /// <summary>Small helpers shared by the line art compute drivers.</summary>
    public static class LineartCompute
    {
        public const int ThreadGroupSize = 128;

        public static int ThreadGroups(int count) => Mathf.Max(0, (count + ThreadGroupSize - 1) / ThreadGroupSize);

        /// <summary>Pointer jumping iterations needed to fully resolve linked lists of up to `count` elements.</summary>
        public static int PointerJumpIterations(int count) => Mathf.Max(1, Mathf.CeilToInt(Mathf.Log(Mathf.Max(count, 2), 2)) + 1);

        public static ComputeShader LoadInstance(string resourcePath)
        {
            var shader = Resources.Load<ComputeShader>(resourcePath);
            if (shader == null)
            {
                Debug.LogError($"Compute shader not found at Resources/{resourcePath}");
                return null;
            }
            return Object.Instantiate(shader);
        }

        public static void Release(ComputeShader shader)
        {
            if (shader == null) return;
            if (Application.isPlaying) Object.Destroy(shader);
            else Object.DestroyImmediate(shader);
        }
    }
}
