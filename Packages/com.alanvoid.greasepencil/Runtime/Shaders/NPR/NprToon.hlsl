#ifndef NPR_TOON_INCLUDED
#define NPR_TOON_INCLUDED

// Port of Blender's "NPR • Toon" / "NPR • Cast Shadow" / "NPR • Light Rig" node groups.
// The light rig globals are set by NPR.NprLightRig.

float3 _NprKeyDirection;   // towards the key light, world space
float3 _NprKeyColor;       // colour * strength (linear)
float3 _NprRimDirection;   // towards the rim light, world space
float3 _NprRimColor;       // colour * strength (linear)

// Blender: Map Range (smoothstep) between edge -/+ s, falling back to a hard (value > edge) step when s == 0
float NprBand(float edge, float softness, float value)
{
    return softness > 0.0 ? smoothstep(edge - softness, edge + softness, value) : (value > edge ? 1.0 : 0.0);
}

struct NprToonParams
{
    float3 baseColor;
    float3 shadowTint;
    float bandThreshold;
    float bandSoftness;
    float receiveShadows;
    float shadowThreshold;
    float rimStrength;
    float3 rimTint;
    float rimWidth;
    float rimSoftness;
    float rimFacing;
};

// normalWS / viewDirWS normalized; viewDirWS points towards the camera; shadowAttenuation in [0,1]
float3 NprToonShade(NprToonParams p, float3 normalWS, float3 viewDirWS, float shadowAttenuation)
{
    // key band
    float nDotL = dot(normalWS, _NprKeyDirection);
    float band = NprBand(p.bandThreshold, p.bandSoftness, min(nDotL, 1.0));

    // cast shadow (lit fraction thresholded like NPR • Cast Shadow)
    float unshadowed = NprBand(min(p.shadowThreshold, 0.99), p.bandSoftness, shadowAttenuation);
    unshadowed = 1.0 - p.receiveShadows + p.receiveShadows * unshadowed;

    float3 lit = p.baseColor * _NprKeyColor;
    float3 fill = p.baseColor * p.shadowTint;
    float3 color = lerp(fill, lit, min(band, unshadowed));

    // rim: grazing band whose reach grows where the surface faces the rim light
    float facing = saturate((dot(normalWS, _NprRimDirection) - p.rimFacing) / max(1.0 - p.rimFacing, 1e-6));
    float reach = p.rimWidth * facing;
    float rim = NprBand(0.0, p.rimSoftness, reach - dot(normalWS, viewDirWS)) * p.rimStrength;
    color += rim * _NprRimColor * p.rimTint;

    return color;
}

#endif
