Shader "Mining Game/Map Darkness"
{
    Properties
    {
        _MainTex ("Daylight mask", 2D) = "black" {}
        [HideInInspector] _TerrainOcclusionTex ("Terrain occlusion", 2D) = "black" {}
        [HideInInspector] _TerrainOcclusionRect ("Terrain occlusion rect", Vector) = (0,0,0,0)
        [HideInInspector] _TerrainOcclusionSize ("Terrain occlusion size", Vector) = (0,0,0,0)
        [HideInInspector] _UltroniumCount ("Ultronium count", Int) = 0
        [HideInInspector] _AltarSource ("Altar light", Vector) = (0,0,0,0)
        [HideInInspector] _AltarMask ("Altar visibility", 2D) = "black" {}
        [HideInInspector] _AltarRect ("Altar bounds", Vector) = (0,0,0,0)
        [HideInInspector] _AltarMaskSize ("Altar mask size", Vector) = (0,0,0,0)
        [HideInInspector] _SrcBlend ("Source blend", Float) = 5
        [HideInInspector] _DstBlend ("Destination blend", Float) = 10
        [HideInInspector] _GlowColor ("Surface glow colour", Color) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            // Surface glow: brightens the unlit surface background with the torch/headlamp field.
            #pragma multi_compile_local _ _SURFACE_GLOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MapLightVisibility.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _GlowColor;
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; float2 fade : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 worldPos : TEXCOORD1; float fade : TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.worldPos = TransformObjectToWorld(input.positionOS).xy;
                output.fade = input.fade.y;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                #if _SURFACE_GLOW
                float light = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a * saturate(input.fade);
                return half4(_GlowColor.rgb * light, 1);
                #endif
                float darkness = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                darkness = min(darkness, 1 - MapLocalLight(input.worldPos));
                darkness *= MapSurfaceDarknessFade(input.uv);
                return half4(0, 0, 0, darkness);
            }
            ENDHLSL
        }
    }
}
