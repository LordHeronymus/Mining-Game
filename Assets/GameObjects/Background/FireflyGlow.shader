Shader "Mining/Firefly Glow"
{
    Properties { _Brightness ("Brightness", Float) = 2 }
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
                float _Brightness;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.color = input.color; output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float r = length(input.uv * 2 - 1);
                float halo = exp(-r * r * 6) * (1 - smoothstep(.7, 1, r));
                float core = 1 - smoothstep(.06, .22, r);
                half3 color = lerp(input.color.rgb, half3(1, 1, .85), core);
                return half4(color * _Brightness, (core * .75 + halo * .25) * input.color.a);
            }
            ENDHLSL
        }
    }
}
