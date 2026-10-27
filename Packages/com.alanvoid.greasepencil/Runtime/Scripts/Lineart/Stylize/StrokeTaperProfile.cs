using UnityEngine;

namespace Lineart.Stylize
{
    /// <summary>
    /// Start / end taper curves of Blender's "Stroke Profile" (Start and End Tapers mode), sampled on X in [0,1].
    /// Start curve: X = 0 at the stroke start tip, 1 after the taper distance.
    /// End curve: X = 0 where the end taper begins, 1 at the stroke end tip.
    /// </summary>
    [CreateAssetMenu(menuName = "Lineart/Stroke Taper Profile", fileName = "StrokeTaperProfile")]
    public class StrokeTaperProfile : ScriptableObject
    {
        public float[] startCurve = { 0f, 1f };
        public float[] endCurve = { 1f, 0f };

        /// <summary>Both curves resampled to `samples` points, packed [start][end].</summary>
        public float[] Pack(int samples)
        {
            var packed = new float[samples * 2];
            for (int i = 0; i < samples; i++)
            {
                float x = samples > 1 ? i / (float)(samples - 1) : 0f;
                packed[i] = Evaluate(startCurve, x);
                packed[samples + i] = Evaluate(endCurve, x);
            }
            return packed;
        }

        static float Evaluate(float[] curve, float x)
        {
            if (curve == null || curve.Length == 0) return 1f;
            if (curve.Length == 1) return curve[0];
            float f = Mathf.Clamp01(x) * (curve.Length - 1);
            int i0 = Mathf.Min((int)f, curve.Length - 2);
            return Mathf.Lerp(curve[i0], curve[i0 + 1], f - i0);
        }
    }
}
