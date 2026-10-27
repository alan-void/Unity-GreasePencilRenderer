using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpikePod.Editor
{
    /// <summary>
    /// Typed view of Art/spikepod_manifest.json written by Tools/Blender/spikepod_export.py.
    /// Colours are linear (scene referred) values straight from Blender.
    /// </summary>
    [Serializable]
    public class SpikePodManifest
    {
        public string source_blend;
        public string export_blend;
        public List<Part> parts = new();
        public List<ToonMaterial> materials = new();
        public List<GpMaterial> gpMaterials = new();
        public List<Outline> outlines = new();
        public List<Curve> curves = new();
        public List<LightInfo> lights = new();
        public CameraInfo camera = new();
        public RenderInfo render = new();

        [Serializable]
        public class Part
        {
            public string name;
            public string source;
            public string material;
            public bool flatShaded;
            public bool hasAo;
        }

        [Serializable]
        public class ToonMaterial
        {
            public string name;
            public float[] baseColor;
            public float[] shadowTint;
            public float bandThreshold;
            public float bandSoftness;
            public float receiveShadows;
            public float shadowThreshold;
            public float rimStrength;
            public float[] rimTint;
            public float rimWidth;
            public float rimSoftness;
            public float rimFacing;
            public bool hasAoMask;
            public float aoThreshold;
            public float[] aoDarkColor;
        }

        [Serializable]
        public class GpMaterial
        {
            public string name;
            public float[] color;
        }

        [Serializable]
        public class Outline
        {
            public string name;
            public string target;
            public string method; // "edge" | "smooth"
            public string material;
            public float strokeRadius;
            public string cornerType;
            public float miterAngle;
            public bool includeBoundary;
            public bool skipConcave;
            public float concaveTolerance;
            public float outlineFalloff;
            public bool undersideFalloff;
            public bool scaleBySpan;
            public float fullSpan;
            public bool scaleByPolar;
            public bool joinBranches;
            public float straightnessWeight;
            public bool useRadiusVariation;
            public bool profileEnabled;
            public string taperStartCurve;
            public string taperEndCurve;
            public float taperStartDistance;
            public float taperEndDistance;
            public float profileStrength;
            public float pixelsPerUnit;
            public bool profileApplyToRadius;
            public bool profileApplyToPush;
            public int profileResolution;
            public bool pushEnabled;
            public float push;
            public float pushOffset;
            public float maxCornerPush;
            public bool spikeThinning;
            public float maxSpikeLength;
            public List<string> warnings = new();
        }

        [Serializable]
        public class Curve
        {
            public string name;
            public float[] samples;
        }

        [Serializable]
        public class LightInfo
        {
            public string name;
            public string role; // "key" | "rim"
            public float[] color;
            public float energy;
            public float[] directionBlender;
        }

        [Serializable]
        public class CameraInfo
        {
            public string name;
            public bool orthographic;
            public float orthoScale;
            public float lens;
            public float sensorWidth;
            public string sensorFit;
            public float clipStart;
            public float clipEnd;
        }

        [Serializable]
        public class RenderInfo
        {
            public int[] resolution;
            public float[] background;
            public string viewTransform;
        }

        public static Color ToColor(float[] c) =>
            c == null || c.Length < 3 ? Color.black : new Color(c[0], c[1], c[2], c.Length > 3 ? c[3] : 1f);

        public ToonMaterial FindMaterial(string name) => materials.Find(m => m.name == name);
        public GpMaterial FindGpMaterial(string name) => gpMaterials.Find(m => m.name == name);
        public Curve FindCurve(string name) => curves.Find(c => c.name == name);
        public LightInfo FindLight(string role) => lights.Find(l => l.role == role);
        public Outline FindOutline(string target) => outlines.Find(o => o.target == target);
    }
}
