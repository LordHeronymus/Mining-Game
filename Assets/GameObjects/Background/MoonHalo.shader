Shader "Mining/Moon Halo"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.38,0.48,1,0.65)
        _Intensity ("Intensity", Float) = 1.4
        _DiscRadius ("Disc Radius", Float) = 0.263
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One
        Cull Off
        ZWrite Off
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Intensity;
                float _DiscRadius;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float r = length(input.uv * 2 - 1);
                // Leave the lunar disc clear so its crater details stay sharp.
                float outside = smoothstep(_DiscRadius * .96, _DiscRadius * 1.15, r);
                float falloff = exp(-max(0, r - _DiscRadius) * 5.5) * (1 - smoothstep(.72, 1, r));
                half3 tint = lerp(half3(.62,.76,1), _Tint.rgb, saturate((r - _DiscRadius) * 3));
                return half4(tint * _Intensity, outside * falloff * _Tint.a);
            }
            ENDHLSL
        }
    }
}
