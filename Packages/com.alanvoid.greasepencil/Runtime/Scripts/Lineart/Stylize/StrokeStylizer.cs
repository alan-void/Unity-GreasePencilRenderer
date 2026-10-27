using Lineart.Pipeline;
using UnityEngine;

namespace Lineart.Stylize
{
    /// <summary>
    /// Stroke post-processing matching Blender's GN outline stack: radius (weights + variation, corner type),
    /// screen-space start/end tapers, push along the point normal and sharp-corner spike thinning.
    /// Add next to a SilhouetteRenderer; runs after the edge calculator every frame.
    /// </summary>
    public class StrokeStylizer : MonoBehaviour, IStrokePostProcessor
    {
        const string ShaderPath = "GreasePencilRenderer/Lineart/ComputeShaders/StrokeStylize";

        static class Ids
        {
            public static readonly int DenseArray = Shader.PropertyToID("_denseArray");
            public static readonly int DenseAttribs = Shader.PropertyToID("_denseAttribs");
            public static readonly int DenseCount = Shader.PropertyToID("_DenseCount");
            public static readonly int StrokeRadius = Shader.PropertyToID("_StrokeRadius");
            public static readonly int UseWeights = Shader.PropertyToID("_UseWeights");
            public static readonly int UseRadiusVariation = Shader.PropertyToID("_UseRadiusVariation");
            public static readonly int MiterAngle = Shader.PropertyToID("_MiterAngle");
            public static readonly int ProfileCurves = Shader.PropertyToID("_ProfileCurves");
            public static readonly int ProfileSamples = Shader.PropertyToID("_ProfileSamples");
            public static readonly int TaperStartDistance = Shader.PropertyToID("_TaperStartDistance");
            public static readonly int TaperEndDistance = Shader.PropertyToID("_TaperEndDistance");
            public static readonly int ProfileStrength = Shader.PropertyToID("_ProfileStrength");
            public static readonly int BlenderPixelsPerScreenPixel = Shader.PropertyToID("_BlenderPixelsPerScreenPixel");
            public static readonly int ProfileApplyToRadius = Shader.PropertyToID("_ProfileApplyToRadius");
            public static readonly int ProfileApplyToPush = Shader.PropertyToID("_ProfileApplyToPush");
            public static readonly int Push = Shader.PropertyToID("_Push");
            public static readonly int PushOffset = Shader.PropertyToID("_PushOffset");
            public static readonly int MaxCornerPush = Shader.PropertyToID("_MaxCornerPush");
            public static readonly int MaxSpikeLength = Shader.PropertyToID("_MaxSpikeLength");
        }

        public StrokeRadiusSettings radius = new();
        public StrokeProfileSettings profile = new();
        public StrokePushSettings push = new();
        public StrokeSpikeThinningSettings spikeThinning = new();

        ComputeShader _shader;
        int _applyRadius;
        int _applyProfile;
        int _applyPush;
        int _thinSpikes;

        ComputeBuffer _profileBuffer;
        StrokeTaperProfile _uploadedProfile;
        int _uploadedResolution;

        void OnEnable()
        {
            _shader = LineartCompute.LoadInstance(ShaderPath);
            if (_shader == null) return;
            _applyRadius = _shader.FindKernel("ApplyRadius");
            _applyProfile = _shader.FindKernel("ApplyProfile");
            _applyPush = _shader.FindKernel("ApplyPush");
            _thinSpikes = _shader.FindKernel("ThinSpikes");
        }

        void OnDisable()
        {
            LineartCompute.Release(_shader);
            _shader = null;
            _profileBuffer?.Release();
            _profileBuffer = null;
            _uploadedProfile = null;
        }

        void OnValidate()
        {
            // force a re-upload of the curves after inspector edits
            _uploadedProfile = null;
        }

        public void Process(IGreasePencilEdgeCalculator calculator)
        {
            if (_shader == null) return;
            GraphicsBuffer strokes = calculator.GetStrokeBuffer();
            GraphicsBuffer attribs = calculator.GetStrokeAttributeBuffer();
            Camera camera = calculator.ViewCamera;
            int count = calculator.GetMaximumBufferLength();
            if (strokes == null || attribs == null || camera == null || count == 0) return;

            int groups = LineartCompute.ThreadGroups(count);
            _shader.SetInt(Ids.DenseCount, count);
            var cameraParams = StrokeCameraParams.From(camera);
            cameraParams.Apply(_shader, Matrix4x4.identity);

            _shader.SetFloat(Ids.StrokeRadius, radius.strokeRadius);
            _shader.SetInt(Ids.UseWeights, radius.useWeights ? 1 : 0);
            _shader.SetInt(Ids.UseRadiusVariation, radius.useRadiusVariation ? 1 : 0);
            _shader.SetFloat(Ids.MiterAngle, radius.EncodedMiterAngle);
            Dispatch(_applyRadius, strokes, attribs, groups);

            if (profile.enabled && profile.profile != null)
            {
                UploadProfile();
                _shader.SetBuffer(_applyProfile, Ids.ProfileCurves, _profileBuffer);
                _shader.SetInt(Ids.ProfileSamples, _uploadedResolution);
                _shader.SetFloat(Ids.TaperStartDistance, profile.taperStartDistance);
                _shader.SetFloat(Ids.TaperEndDistance, profile.taperEndDistance);
                _shader.SetFloat(Ids.ProfileStrength, profile.strength);
                _shader.SetFloat(Ids.BlenderPixelsPerScreenPixel, profile.pixelsPerUnit / Mathf.Max(cameraParams.PixelsPerViewUnit(camera), 1e-6f));
                _shader.SetInt(Ids.ProfileApplyToRadius, profile.applyToRadius ? 1 : 0);
                _shader.SetInt(Ids.ProfileApplyToPush, profile.applyToPushRadius ? 1 : 0);
                Dispatch(_applyProfile, strokes, attribs, groups);
            }

            if (push.enabled)
            {
                _shader.SetFloat(Ids.Push, push.push);
                _shader.SetFloat(Ids.PushOffset, push.offset);
                _shader.SetFloat(Ids.MaxCornerPush, push.maxCornerPush);
                Dispatch(_applyPush, strokes, attribs, groups);
            }

            if (spikeThinning.enabled && radius.cornerType == StrokeCornerType.Sharp)
            {
                _shader.SetFloat(Ids.MaxSpikeLength, spikeThinning.maxSpikeLength);
                Dispatch(_thinSpikes, strokes, attribs, groups);
            }
        }

        void Dispatch(int kernel, GraphicsBuffer strokes, GraphicsBuffer attribs, int groups)
        {
            _shader.SetBuffer(kernel, Ids.DenseArray, strokes);
            _shader.SetBuffer(kernel, Ids.DenseAttribs, attribs);
            _shader.Dispatch(kernel, groups, 1, 1);
        }

        void UploadProfile()
        {
            int samples = Mathf.Max(2, profile.resolution);
            if (_profileBuffer != null && _uploadedProfile == profile.profile && _uploadedResolution == samples) return;

            _profileBuffer?.Release();
            _profileBuffer = new ComputeBuffer(samples * 2, sizeof(float));
            _profileBuffer.SetData(profile.profile.Pack(samples));
            _uploadedProfile = profile.profile;
            _uploadedResolution = samples;
        }
    }
}
