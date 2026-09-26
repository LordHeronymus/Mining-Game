// Based on the installed URP Sprite-Lit-Default shader. Zoom UVs, never tile geometry.
Shader "Mining Game/Ore Overlay Lit"
{
    Properties
    {
        _OreScale("Ore Size", Range(0.5, 3)) = 1
        _ReflectionStrength("Reflection Strength", Range(0, 6)) = 2.5
        _ShimmerStrength("Shimmer Strength", Range(0, 3)) = 0.12
        _EmbeddingStrength("Rock Embedding", Range(0, 1)) = 1
        _ShimmerRadius("Shimmer Radius", Range(0.005, 0.15)) = 0.045
        _UltroniumGlow("Ultronium Glow", Range(0, 3)) = 1.15
        [HDR] _UltroniumGlowColor("Ultronium Glow Color", Color) = (0.24,0.12,1.2,1)
        _MainTex("Diffuse", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}
        [MaterialToggle] _ZWrite("ZWrite", Float) = 0

        // Legacy properties. They're here so that materials using this shader can gracefully fallback to the legacy sprite shader.
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex CombinedShapeLightVertex
            #pragma fragment CombinedShapeLightFragment

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            struct Attributes
            {
                float3 positionOS   : POSITION;
                float4 color        : COLOR;
                float2 uv           : TEXCOORD0;
                UNITY_SKINNED_VERTEX_INPUTS
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4  positionCS  : SV_POSITION;
                half4   color       : COLOR;
                float2  uv          : TEXCOORD0;
                half2   lightingUV  : TEXCOORD1;
                float3  positionWS  : TEXCOORD2;
                half    ultronium   : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            UNITY_TEXTURE_STREAMING_DEBUG_VARS_FOR_TEX(_MainTex);

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _OreScale;
                float _ReflectionStrength;
                float _ShimmerStrength;
                float _ShimmerRadius;
                float _EmbeddingStrength;
                float _UltroniumGlow;
                half4 _UltroniumGlowColor;
                float4 _UniformStone;
                float4 _TestBounds;
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

            Varyings CombinedShapeLightVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                UNITY_SKINNED_VERTEX_COMPUTE(v);

                SetUpSpriteInstanceProperties();
                v.positionOS = UnityFlipSprite(v.positionOS, unity_SpriteProps.xy);
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.positionWS = TransformObjectToWorld(v.positionOS);
                o.uv = (v.uv - 0.5) / max(_OreScale, 0.01) + 0.5;
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);

                o.ultronium = step(v.color.a, .99h);
                o.color = v.color * _Color * unity_SpriteColor;
                if (o.ultronium > .5h) o.color.a = _Color.a * unity_SpriteColor.a;
                return o;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            #include "TerrainMaterialSample.hlsl"

            half3 RichColor(half3 color)
            {
                half peak = max(max(color.r, color.g), color.b);
                return pow(saturate(color / max(peak, 0.0001h)), 1.6h) * peak;
            }

            half4 ShimmerSample(float2 uv, half ultronium)
            {
                half4 sample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(uv));
                sample.a = lerp(sample.a, smoothstep(.04h, .7h, sample.a), ultronium);
                sample.a *= step(0, min(min(uv.x, uv.y), min(1-uv.x, 1-uv.y)));
                return half4(RichColor(sample.rgb) * sample.a, sample.a);
            }

            half4 CombinedShapeLightFragment(Varyings i) : SV_Target
            {
                clip(min(min(i.uv.x, i.uv.y), min(1 - i.uv.x, 1 - i.uv.y)));
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                // Solid crystal interiors, with antialiasing retained at the silhouette.
                texel.a = lerp(texel.a, smoothstep(.04h, .7h, texel.a), i.ultronium);
                const half4 main = i.color * texel;
                const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv);
                SurfaceData2D surfaceData;
                InputData2D inputData;

                // Alpha neighbourhood supplies contact depth and irregular stone lips.
                float2 cell = (i.positionWS.xy - _UniformStone.zw) / max(_UniformStone.x,.001);
                float chips = .5 + .25*sin(cell.x*37 + sin(cell.y*29)) + .25*sin(cell.y*43 + cell.x*17);
                float radius = lerp(.018,.052,smoothstep(.35,.8,chips)) / max(_OreScale,.5);
                radius *= lerp(1, .23, i.ultronium);
                half inner = main.a, outer = main.a;
                half4 shimmer = 0;
                [unroll] for (int n = 0; n < 8; n++)
                {
                    float angle = n * (TWO_PI / 8);
                    float2 direction = float2(cos(angle),sin(angle));
                    half4 nearby = ShimmerSample(i.uv + direction * radius, i.ultronium);
                    inner = min(inner,nearby.a*i.color.a);
                    outer = max(outer,nearby.a*i.color.a);
                    shimmer += ShimmerSample(i.uv + direction * _ShimmerRadius, i.ultronium) * .125h;
                }
                half embedded = _UniformStone.x > 0 ? _EmbeddingStrength : 0;
                half lip = (main.a-inner) * smoothstep(.28,.7,chips) * embedded;
                lip *= lerp(1, .4h, i.ultronium);
                half crystalContact = 0;
                if (i.ultronium > .5h && embedded > 0)
                {
                    // Separate, solid pockets of host rock. A narrow transition only
                    // antialiases their edge; it never fades the crystal body.
                    float pockets = .5 + .28*sin(cell.x*14 + cell.y*9)
                        + .22*sin(cell.y*19 - cell.x*11);
                    float pocket = smoothstep(.56,.62,pockets);
                    float reach = lerp(.012,.034,pocket) / max(_OreScale,.5);
                    half rim = 1, shadowRim = 1;
                    [unroll] for (int k = 0; k < 8; k++)
                    {
                        float angle = k * (TWO_PI / 8);
                        float2 direction = float2(cos(angle),sin(angle));
                        // Slightly deeper sockets on downward facing edges.
                        direction.y *= direction.y < 0 ? 1.25 : .35;
                        rim = min(rim, ShimmerSample(i.uv + direction*reach, 1).a);
                        shadowRim = min(shadowRim,
                            ShimmerSample(i.uv + direction*(reach+.004/max(_OreScale,.5)), 1).a);
                    }
                    lip = main.a * (1-smoothstep(.38,.62,rim)) * pocket * embedded;
                    crystalContact = main.a * (1-smoothstep(.3,.7,shadowRim))
                        * pocket * (1-lip) * embedded;
                }
                half contact = (outer-main.a) * .65h * embedded;
                contact *= 1-i.ultronium;
                half pulse = .88h + .12h * sin(_Time.y * 1.7h + i.positionWS.x * 3.1h + i.positionWS.y * 2.3h);
                half haloAlpha = saturate(shimmer.a * _ShimmerStrength) * i.color.a * (1-i.ultronium);
                half outerAlpha = max(contact,haloAlpha);
                half alpha = main.a + outerAlpha * (1-main.a);
                half peak = max(main.r,max(main.g,main.b));
                half facets = pow(smoothstep(.15h,.85h,peak),3.0h);
                half3 reflectiveSurface = main.rgb + RichColor(main.rgb)*facets*_ReflectionStrength;
                reflectiveSurface *= 1-(main.a-inner)*embedded*.42h;
                reflectiveSurface *= 1-crystalContact*.65h;
                half3 rock = 0;
                if(embedded>0) rock=TerrainMaterialSample(i.positionWS.xy).rgb*i.color.rgb;
                reflectiveSurface=lerp(reflectiveSurface,rock,lip);
                half3 haloColor=shimmer.rgb/max(shimmer.a,.0001h)*i.color.rgb;
                half3 outside=lerp(haloColor,rock*.34h,saturate(contact/max(outerAlpha,.0001h)));
                half3 combined=(reflectiveSurface*main.a+outside*outerAlpha*(1-main.a))/max(alpha,.0001h);
                InitializeSurfaceData(combined, alpha, mask, surfaceData);
                InitializeInputData(i.uv, i.lightingUV, inputData);

                SETUP_DEBUG_TEXTURE_DATA_2D_NO_TS(inputData, i.positionWS, i.positionCS, _MainTex);

                half4 lit = CombinedShapeLightShared(surfaceData, inputData);
                // Compress brightness uniformly across RGB to prevent white clipping
                // while retaining the ore hue, including on non-HDR cameras.
                half litPeak = max(lit.r, max(lit.g, lit.b));
                half compressed = litPeak <= 0.7h ? litPeak :
                    0.7h + 0.3h * (1 - exp(-(litPeak - 0.7h) / 0.3h));
                lit.rgb *= compressed / max(litPeak, 0.0001h);
                // Retain dark facets and source saturation instead of a flat violet wash.
                half3 crystalEmission = RichColor(main.rgb) * .48h +
                    _UltroniumGlowColor.rgb * facets * .12h;
                lit.rgb += crystalEmission * _UltroniumGlow * pulse * main.a *
                    (1-lip) * (1-crystalContact*.75h) * i.ultronium;
                return lit;
            }
            ENDHLSL
        }

        Pass
        {
            ZWrite Off

            Tags { "LightMode" = "NormalsRendering"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex NormalsRenderingVertex
            #pragma fragment NormalsRenderingFragment

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                float3 positionOS   : POSITION;
                float4 color        : COLOR;
                float2 uv           : TEXCOORD0;
                float4 tangent      : TANGENT;
                UNITY_SKINNED_VERTEX_INPUTS
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4  positionCS      : SV_POSITION;
                half4   color           : COLOR;
                float2  uv              : TEXCOORD0;
                half3   normalWS        : TEXCOORD1;
                half3   tangentWS       : TEXCOORD2;
                half3   bitangentWS     : TEXCOORD3;
                half    ultronium       : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START( UnityPerMaterial )
                half4 _Color;
                float _OreScale;
                float _ReflectionStrength;
                float _ShimmerStrength;
                float _ShimmerRadius;
                float _EmbeddingStrength;
                float _UltroniumGlow;
                half4 _UltroniumGlowColor;
                float4 _UniformStone;
                float4 _TestBounds;
            CBUFFER_END

            Varyings NormalsRenderingVertex(Attributes attributes)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(attributes);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                UNITY_SKINNED_VERTEX_COMPUTE(attributes);

                SetUpSpriteInstanceProperties();
                attributes.positionOS = UnityFlipSprite(attributes.positionOS, unity_SpriteProps.xy);
                o.positionCS = TransformObjectToHClip(attributes.positionOS);
                o.uv = (attributes.uv - 0.5) / max(_OreScale, 0.01) + 0.5;
                half ultronium = step(attributes.color.a, .99h);
                o.ultronium = ultronium;
                o.color = attributes.color * _Color * unity_SpriteColor;
                if (ultronium > .5h) o.color.a = _Color.a * unity_SpriteColor.a;
                o.normalWS = -GetViewForwardDir();
                o.tangentWS = TransformObjectToWorldDir(attributes.tangent.xyz);
                o.bitangentWS = cross(o.normalWS, o.tangentWS) * attributes.tangent.w;
                return o;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/NormalsRenderingShared.hlsl"

            half4 NormalsRenderingFragment(Varyings i) : SV_Target
            {
                clip(min(min(i.uv.x, i.uv.y), min(1 - i.uv.x, 1 - i.uv.y)));
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                texel.a = lerp(texel.a, smoothstep(.04h, .7h, texel.a), i.ultronium);
                const half4 mainTex = i.color * texel;
                const half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv));

                return NormalsRenderingShared(mainTex, normalTS, i.tangentWS.xyz, i.bitangentWS.xyz, i.normalWS.xyz);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" "Queue"="Transparent" "RenderType"="Transparent"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
            #if defined(DEBUG_DISPLAY)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging2D.hlsl"
            #endif

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            struct Attributes
            {
                float3 positionOS   : POSITION;
                float4 color        : COLOR;
                float2 uv           : TEXCOORD0;
                UNITY_SKINNED_VERTEX_INPUTS
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4  positionCS      : SV_POSITION;
                float4  color           : COLOR;
                float2  uv              : TEXCOORD0;
                half    ultronium       : TEXCOORD1;
                #if defined(DEBUG_DISPLAY)
                float3  positionWS  : TEXCOORD2;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            UNITY_TEXTURE_STREAMING_DEBUG_VARS_FOR_TEX(_MainTex);

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START( UnityPerMaterial )
                half4 _Color;
                float _OreScale;
                float _ReflectionStrength;
                float _ShimmerStrength;
                float _ShimmerRadius;
                float _EmbeddingStrength;
                float _UltroniumGlow;
                half4 _UltroniumGlowColor;
                float4 _UniformStone;
                float4 _TestBounds;
            CBUFFER_END

            Varyings UnlitVertex(Attributes attributes)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(attributes);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                UNITY_SKINNED_VERTEX_COMPUTE(attributes);

                SetUpSpriteInstanceProperties();
                attributes.positionOS = UnityFlipSprite( attributes.positionOS, unity_SpriteProps.xy);
                o.positionCS = TransformObjectToHClip(attributes.positionOS);
                #if defined(DEBUG_DISPLAY)
                o.positionWS = TransformObjectToWorld(attributes.positionOS);
                #endif
                o.uv = (attributes.uv - 0.5) / max(_OreScale, 0.01) + 0.5;
                o.ultronium = step(attributes.color.a, .99h);
                o.color = attributes.color * _Color * unity_SpriteColor;
                if (o.ultronium > .5h) o.color.a = _Color.a * unity_SpriteColor.a;
                return o;
            }

            float4 UnlitFragment(Varyings i) : SV_Target
            {
                clip(min(min(i.uv.x, i.uv.y), min(1 - i.uv.x, 1 - i.uv.y)));
                float4 mainTex = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                mainTex.a = lerp(mainTex.a, smoothstep(.04h, .7h, mainTex.a), i.ultronium);
                mainTex.rgb += _UltroniumGlowColor.rgb * _UltroniumGlow * .32h *
                    mainTex.a * i.ultronium;

                #if defined(DEBUG_DISPLAY)
                SurfaceData2D surfaceData;
                InputData2D inputData;
                half4 debugColor = 0;

                InitializeSurfaceData(mainTex.rgb, mainTex.a, surfaceData);
                InitializeInputData(i.uv, inputData);
                SETUP_DEBUG_TEXTURE_DATA_2D_NO_TS(inputData, i.positionWS, i.positionCS, _MainTex);

                if(CanDebugOverrideOutputColor(surfaceData, inputData, debugColor))
                {
                    return debugColor;
                }
                #endif

                return mainTex;
            }
            ENDHLSL
        }
    }
}
