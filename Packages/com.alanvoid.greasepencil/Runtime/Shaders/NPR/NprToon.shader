Shader "NPR/Toon"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.8, 0.8, 0.8, 1)
        _ShadowTint("Shadow Tint", Color) = (0.36, 0.34, 0.52, 1)
        _BandThreshold("Band Threshold", Float) = 0
        _BandSoftness("Band Softness", Float) = 0.03
        _ReceiveShadows("Receive Shadows", Range(0, 1)) = 1
        _ShadowThreshold("Shadow Threshold", Range(0, 1)) = 0.5

        [Header(Rim)]
        _RimStrength("Rim Strength", Float) = 1
        _RimTint("Rim Tint", Color) = (1, 1, 1, 1)
        _RimWidth("Rim Width", Float) = 0.5
        _RimSoftness("Rim Softness", Float) = 0.03
        _RimFacing("Rim Facing", Range(-1, 0.99)) = 0

        [Header(Ambient Occlusion Mask)]
        [Toggle(_NPR_AO_MASK)] _AoMask("Use Vertex AO Mask", Float) = 0
        _AoThreshold("AO Threshold (vertex colour r)", Range(0, 1)) = 0.5
        _AoDarkColor("AO Dark Colour", Color) = (0.067, 0.027, 0.011, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _ShadowTint;
            float _BandThreshold;
            float _BandSoftness;
            float _ReceiveShadows;
            float _ShadowThreshold;
            float _RimStrength;
            float4 _RimTint;
            float _RimWidth;
            float _RimSoftness;
            float _RimFacing;
            float _AoThreshold;
            float4 _AoDarkColor;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "NprToonForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _NPR_AO_MASK
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "NprToon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                float shadowAttenuation = MainLightRealtimeShadow(shadowCoord);

                NprToonParams p;
                p.baseColor = _BaseColor.rgb;
                p.shadowTint = _ShadowTint.rgb;
                p.bandThreshold = _BandThreshold;
                p.bandSoftness = _BandSoftness;
                p.receiveShadows = _ReceiveShadows;
                p.shadowThreshold = _ShadowThreshold;
                p.rimStrength = _RimStrength;
                p.rimTint = _RimTint.rgb;
                p.rimWidth = _RimWidth;
                p.rimSoftness = _RimSoftness;
                p.rimFacing = _RimFacing;

                float3 color = NprToonShade(p, normalWS, viewDirWS, shadowAttenuation);

            #if defined(_NPR_AO_MASK)
                color = input.color.r >= _AoThreshold ? color : _AoDarkColor.rgb;
            #endif

                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
