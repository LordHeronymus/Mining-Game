Shader "Mining/Shop Window Glow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Intensity ("Intensity", Float) = 1.5
        _WindowRect ("Window Rect", Vector) = (0.555, 0.34, 0.723, 0.625)
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
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float4 _WindowRect;
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
                half4 sprite = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float2 edges = smoothstep(_WindowRect.xy, _WindowRect.xy + 0.012, input.uv)
                    * (1 - smoothstep(_WindowRect.zw - 0.012, _WindowRect.zw, input.uv));
                float warm = saturate((sprite.r - sprite.b) * 3) * smoothstep(0.18, 0.7, sprite.r);
                return half4(sprite.rgb * _Intensity, sprite.a * edges.x * edges.y * warm * input.color.a);
            }
            ENDHLSL
        }
    }
}
