Shader "Mining Game/Tree Sway Lit"
{
    Properties
    {
        _MainTex("Diffuse", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        [MaterialToggle] _ZWrite("ZWrite", Float) = 0
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
        [HideInInspector] _SwayBaseY("Sway Base Y", Float) = 0
        [HideInInspector] _SwayHeight("Sway Height", Float) = 1
        [HideInInspector] _SwayPhase("Sway Phase", Float) = 0
        [HideInInspector] _SwayFrequency("Sway Frequency", Float) = 1
        [HideInInspector] _SwayImpact("Sway Impact", Float) = 0
        [HideInInspector] _SwayStrength("Sway Strength", Float) = 1
        [HideInInspector] _ReachGlow("Reach Glow", Float) = 0
        [HideInInspector] _ReachGlowColor("Reach Glow Color", Color) = (1,0.66,0.28,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            Tags { "LightMode"="Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"

            #pragma vertex TreeVertex
            #pragma fragment TreeFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

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
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                half2 lightingUV : TEXCOORD1;
                half treeHeight : TEXCOORD3;
                #if defined(DEBUG_DISPLAY)
                float3 positionWS : TEXCOORD2;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            UNITY_TEXTURE_STREAMING_DEBUG_VARS_FOR_TEX(_MainTex);
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _SwayBaseY;
                float _SwayHeight;
                float _SwayPhase;
                float _SwayFrequency;
                float _SwayImpact;
                float _SwayStrength;
                float _ReachGlow;
                half4 _ReachGlowColor;
            CBUFFER_END

            #if USE_SHAPE_LIGHT_TYPE_0
            SHAPE_LIGHT(0)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_1
            SHAPE_LIGHT(1)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_2
            SHAPE_LIGHT(2)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_3
            SHAPE_LIGHT(3)
            #endif

            float3 BendTree(float3 positionOS)
            {
                float height = saturate((positionOS.y - _SwayBaseY) / max(_SwayHeight, 0.001));
                float trunkFlex = height * height;
                float crownFlex = trunkFlex * height;
                float swayTime = _Time.y * _SwayFrequency;
                swayTime += sin(swayTime * 0.21 + _SwayPhase * 1.31) * 0.18;
                float slow = sin(swayTime * 1.1 + _SwayPhase) * 0.017
                    + sin(swayTime * 0.67 + _SwayPhase * 1.7) * 0.008;
                float crownLag = sin(swayTime * 1.8 + _SwayPhase * 0.8 + height * 3.2) * 0.004;
                positionOS.x += _SwayStrength * _SwayHeight
                    * (slow * trunkFlex + crownLag * crownFlex) + _SwayImpact * trunkFlex;
                return positionOS;
            }

            Varyings TreeVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                UNITY_SKINNED_VERTEX_COMPUTE(v);
                SetUpSpriteInstanceProperties();
                v.positionOS = UnityFlipSprite(v.positionOS, unity_SpriteProps.xy);
                o.treeHeight = saturate((v.positionOS.y - _SwayBaseY) / max(_SwayHeight, 0.001));
                v.positionOS = BendTree(v.positionOS);
                o.positionCS = TransformObjectToHClip(v.positionOS);
                #if defined(DEBUG_DISPLAY)
                o.positionWS = TransformObjectToWorld(v.positionOS);
                #endif
                o.uv = v.uv;
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);
                o.color = v.color * _Color * unity_SpriteColor;
                return o;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 TreeFragment(Varyings i) : SV_Target
            {
                half4 main = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half pulse = 0.82 + 0.18 * sin(_Time.y * 2.2 + _SwayPhase);
                half trunk = 1 - smoothstep(0.18, 0.42, i.treeHeight);
                half glow = saturate(_ReachGlow * 0.17 * pulse * trunk);
                main.rgb = lerp(main.rgb, _ReachGlowColor.rgb, glow);
                const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv);
                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, mask, surfaceData);
                InitializeInputData(i.uv, i.lightingUV, inputData);
                SETUP_DEBUG_TEXTURE_DATA_2D_NO_TS(inputData, i.positionWS, i.positionCS, _MainTex);
                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #pragma vertex ForwardVertex
            #pragma fragment ForwardFragment
            #pragma multi_compile_instancing

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
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                half treeHeight : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _SwayBaseY;
                float _SwayHeight;
                float _SwayPhase;
                float _SwayFrequency;
                float _SwayImpact;
                float _SwayStrength;
                float _ReachGlow;
                half4 _ReachGlowColor;
            CBUFFER_END

            float3 BendTree(float3 positionOS)
            {
                float height = saturate((positionOS.y - _SwayBaseY) / max(_SwayHeight, 0.001));
                float trunkFlex = height * height;
                float crownFlex = trunkFlex * height;
                float swayTime = _Time.y * _SwayFrequency;
                swayTime += sin(swayTime * 0.21 + _SwayPhase * 1.31) * 0.18;
                float slow = sin(swayTime * 1.1 + _SwayPhase) * 0.017
                    + sin(swayTime * 0.67 + _SwayPhase * 1.7) * 0.008;
                float crownLag = sin(swayTime * 1.8 + _SwayPhase * 0.8 + height * 3.2) * 0.004;
                positionOS.x += _SwayStrength * _SwayHeight
                    * (slow * trunkFlex + crownLag * crownFlex) + _SwayImpact * trunkFlex;
                return positionOS;
            }

            Varyings ForwardVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                UNITY_SKINNED_VERTEX_COMPUTE(v);
                SetUpSpriteInstanceProperties();
                v.positionOS = UnityFlipSprite(v.positionOS, unity_SpriteProps.xy);
                o.treeHeight = saturate((v.positionOS.y - _SwayBaseY) / max(_SwayHeight, 0.001));
                v.positionOS = BendTree(v.positionOS);
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv;
                o.color = v.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 ForwardFragment(Varyings i) : SV_Target
            {
                half4 main = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half pulse = 0.82 + 0.18 * sin(_Time.y * 2.2 + _SwayPhase);
                half trunk = 1 - smoothstep(0.18, 0.42, i.treeHeight);
                half glow = saturate(_ReachGlow * 0.17 * pulse * trunk);
                main.rgb = lerp(main.rgb, _ReachGlowColor.rgb, glow);
                return main;
            }
            ENDHLSL
        }
    }
}
