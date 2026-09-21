Shader "Mining Game/Parallax Lit Gradient"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [PerRendererData] _LightBottom ("Light Bottom", Float) = 1
        [PerRendererData] _LightTop ("Light Top", Float) = 1
        [PerRendererData] _LightBottomY ("Light Bottom Y", Float) = 0
        [PerRendererData] _LightTopY ("Light Top Y", Float) = 1
        [PerRendererData] _Contrast ("Contrast", Float) = 1
        [PerRendererData] _Saturation ("Saturation", Float) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Tags { "LightMode"="Universal2D" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _LightBottom;
                float _LightTop;
                float _LightBottomY;
                float _LightTopY;
                float _Contrast;
                float _Saturation;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_SKINNED_VERTEX_INPUTS
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float worldY : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                float3 worldPosition = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(worldPosition);
                output.color = input.color * _Color * unity_SpriteColor;
                output.uv = input.uv;
                output.worldY = worldPosition.y;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                float range = max(.0001, _LightTopY - _LightBottomY);
                float blend = saturate((input.worldY - _LightBottomY) / range);
                half luminance = dot(color.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                color.rgb = lerp(luminance.xxx, color.rgb, _Saturation);
                color.rgb = (color.rgb - 0.5h) * _Contrast + 0.5h;
                color.rgb *= lerp(_LightBottom, _LightTop, blend);
                return color;
            }
            ENDHLSL
        }
    }
}
