// Grease Pencil strokes composited like Blender does: premultiplied alpha blending of the anti-aliased
// stroke mask without writing depth. Strokes are depth tested against the scene by default (3D depth
// order); set Depth Test to Always to draw them over every mesh.
Shader "Lineart/GreasePencilBlended"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white"
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Depth Test", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest [_ZTest]
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "./gpencil_vert.hlsl"
            #include "gpencil_frag.hlsl"

            ENDHLSL
        }
    }
}
