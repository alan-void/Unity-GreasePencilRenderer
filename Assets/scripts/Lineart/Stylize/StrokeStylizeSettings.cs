using System;
using UnityEngine;

namespace Lineart.Stylize
{
    public enum StrokeCornerType
    {
        Round,
        Sharp,
        Flat,
    }

    [Serializable]
    public class StrokeRadiusSettings
    {
        [Tooltip("World space stroke radius (Blender: Stroke Radius).")]
        public float strokeRadius = 0.02f;
        [Tooltip("Scale the radius by min(mean weight, point weight) from the edge finder.")]
        public bool useWeights = true;
        [Tooltip("Scale the radius by the baked radius variation (Blender: Radius Variation noise).")]
        public bool useRadiusVariation = true;
        public StrokeCornerType cornerType = StrokeCornerType.Round;
        [Tooltip("Miter angle in radians for sharp corners.")]
        public float miterAngle = 0.0873f;

        /// <summary>Miter angle encoded the way the Grease Pencil shader expects (0 round, PI flat).</summary>
        public float EncodedMiterAngle => cornerType switch
        {
            StrokeCornerType.Round => 0f,
            StrokeCornerType.Flat => Mathf.PI,
            _ => Mathf.Clamp(miterAngle, 0.002f, Mathf.PI - 0.001f),
        };
    }

    [Serializable]
    public class StrokeProfileSettings
    {
        public bool enabled;
        public StrokeTaperProfile profile;
        [Tooltip("Blender 'pixels' over which the start taper runs.")]
        public float taperStartDistance = 40f;
        [Tooltip("Blender 'pixels' over which the end taper runs.")]
        public float taperEndDistance = 40f;
        public float strength = 1f;
        [Tooltip("Blender's Pixels per Unit: profile pixels = camera space units * PPU (resolution independent).")]
        public float pixelsPerUnit = 1000f;
        public bool applyToRadius = true;
        public bool applyToPushRadius = true;
        [Tooltip("Number of samples (Blender: Profile Resolution).")]
        public int resolution = 256;
    }

    [Serializable]
    public class StrokePushSettings
    {
        public bool enabled;
        [Tooltip("Offset along the point normal in units of the push radius (Blender: Push).")]
        public float push = 0.9f;
        [Tooltip("Constant world offset (Blender: Offset).")]
        public float offset;
        [Tooltip("Upper clamp of the miter factor at corners (Blender: Max Corner Push).")]
        public float maxCornerPush = 2.5f;
    }

    [Serializable]
    public class StrokeSpikeThinningSettings
    {
        [Tooltip("Thin strokes at sharp corners (Edge Lines Push). Only active for Sharp corners.")]
        public bool enabled;
        public float maxSpikeLength = 3f;
    }
}
