Shader "Mining/Night Star"
{
    Properties { _Brightness ("Brightness", Float) = 3 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
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
                float2 p = abs(input.uv * 2 - 1);
                float core = 1 - smoothstep(0.22, 0.58, length(p));
                float rays = (1 - smoothstep(0.025, 0.12, min(p.x, p.y))) * pow(saturate(1 - max(p.x, p.y)), 2);
                return half4(input.color.rgb * _Brightness, max(core, rays * 0.8) * input.color.a);
            }
            ENDHLSL
        }
    }
}
