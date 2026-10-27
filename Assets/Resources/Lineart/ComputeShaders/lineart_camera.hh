#pragma once

// Camera uniforms shared by the line art compute passes (set by Lineart.Pipeline.StrokeCameraParams).
float3 _LA_CameraPosWS;
float3 _LA_CameraFwdWS;   // camera forward (-Z of the view) in world space
uint _LA_IsOrtho;

float4x4 _GP_ObjectToWorld;
float4x4 _GP_WorldToObject;
float4x4 _GP_Proj;
float4x4 _GP_ViewProj;
float4x4 _GP_InvViewProj;
float4 _GP_ScreenParams;

// Un-normalized view vector from P towards the camera, the same convention as Blender's GN outline
// tools: ortho ? -F : (C - P).
float3 ViewVector(float3 worldPos)
{
    return _LA_IsOrtho != 0 ? -_LA_CameraFwdWS : (_LA_CameraPosWS - worldPos);
}

float2 ProjectToScreen(float3 worldPos)
{
    float4 clip = mul(_GP_ViewProj, float4(worldPos, 1.0f));
    return ((clip.xy / clip.w) * 0.5f + 0.5f) * _GP_ScreenParams.xy;
}

float2 PixelToNdcXY(float2 pixel)
{
    return (pixel / _GP_ScreenParams.xy) * 2.0f - 1.0f;
}

float3 UnprojectWorldFromNdc(float2 ndcXY, float ndcZ)
{
    float4 worldH = mul(_GP_InvViewProj, float4(ndcXY, ndcZ, 1.0f));
    return worldH.xyz / max(worldH.w, 1e-8f);
}

float3 ScreenPixelOffsetToWorldDelta(float3 worldPos, float2 pixelOffset)
{
    float4 clip = mul(_GP_ViewProj, float4(worldPos, 1.0f));
    if (abs(clip.w) <= 1e-8f)
    {
        return float3(0.0f, 0.0f, 0.0f);
    }

    float2 basePixel = ((clip.xy / clip.w) * 0.5f + 0.5f) * _GP_ScreenParams.xy;
    float ndcZ = clip.z / clip.w;

    float3 world0 = UnprojectWorldFromNdc(PixelToNdcXY(basePixel), ndcZ);
    float3 world1 = UnprojectWorldFromNdc(PixelToNdcXY(basePixel + pixelOffset), ndcZ);
    return world1 - world0;
}
