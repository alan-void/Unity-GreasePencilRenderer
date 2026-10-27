using UnityEngine;

namespace Lineart.Pipeline
{
    /// <summary>
    /// Camera state needed by the line art compute passes. Uploads the uniforms declared in lineart_camera.hh.
    /// </summary>
    public readonly struct StrokeCameraParams
    {
        static readonly int CameraPosWS = Shader.PropertyToID("_LA_CameraPosWS");
        static readonly int CameraFwdWS = Shader.PropertyToID("_LA_CameraFwdWS");
        static readonly int IsOrtho = Shader.PropertyToID("_LA_IsOrtho");

        public readonly Vector3 Position;
        public readonly Vector3 Forward;
        public readonly bool Orthographic;
        public readonly Matrix4x4 GpuProjection;
        public readonly Matrix4x4 ViewProjection;
        public readonly Matrix4x4 InverseViewProjection;
        public readonly Vector2 PixelSize;

        StrokeCameraParams(Camera camera)
        {
            Position = camera.transform.position;
            Forward = camera.transform.forward;
            Orthographic = camera.orthographic;
            GpuProjection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            ViewProjection = GpuProjection * camera.worldToCameraMatrix;
            InverseViewProjection = ViewProjection.inverse;
            PixelSize = new Vector2(camera.pixelWidth, camera.pixelHeight);
        }

        public static StrokeCameraParams From(Camera camera) => new(camera);

        /// <summary>Screen pixels per view-space unit (ortho) or per unit of x/z (perspective).</summary>
        public float PixelsPerViewUnit(Camera camera) => 0.5f * PixelSize.y * camera.projectionMatrix.m11;

        public void Apply(ComputeShader shader, Matrix4x4 objectToWorld)
        {
            shader.SetVector(CameraPosWS, Position);
            shader.SetVector(CameraFwdWS, Forward);
            shader.SetInt(IsOrtho, Orthographic ? 1 : 0);

            shader.SetMatrix(SilhouetteShaderIDs.GpObjectToWorld, objectToWorld);
            shader.SetMatrix(SilhouetteShaderIDs.GpWorldToObject, objectToWorld.inverse);
            shader.SetMatrix(SilhouetteShaderIDs.GpProj, GpuProjection);
            shader.SetMatrix(SilhouetteShaderIDs.GpViewProj, ViewProjection);
            shader.SetMatrix(SilhouetteShaderIDs.GpInvViewProj, InverseViewProjection);
            shader.SetVector(SilhouetteShaderIDs.GpScreenParams,
                new Vector4(PixelSize.x, PixelSize.y, 1.0f + 1.0f / PixelSize.x, 1.0f + 1.0f / PixelSize.y));
        }
    }
}
