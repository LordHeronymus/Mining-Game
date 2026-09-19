Shader "Mining/Ore Fragment"
{
    Properties { _CoreBrightness ("Core Brightness", Range(1,4)) = 2 }
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
                float _CoreBrightness;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = abs(input.uv * 2 - 1);
                float edge = p.x * .8 + p.y * .65;
                float alpha = saturate((.85 - edge) * 20) * input.color.a;
                float core = 1 - smoothstep(.25, .65, edge);
                return half4(lerp(input.color.rgb, _CoreBrightness.xxx, core), alpha);
            }
            ENDHLSL
        }
    }
}
