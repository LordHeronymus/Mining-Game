Shader "Mining Game/Fixed Underground"
{
    Properties
    {
        _Layer0Tex("Layer 1",2D)="white"{}
        _Layer1Tex("Layer 2",2D)="white"{}
        _Layer2Tex("Layer 3",2D)="white"{}
        _Layer3Tex("Layer 4",2D)="white"{}
        _CapTex("Upper soil lip",2D)="black"{}
        _TopY("Top Y",Float)=1
        _RepeatSize("Repeat size",Vector)=(16.5,11,0,0)
        _SurfaceY("Surface Y",Float)=1
        _LayerStarts("Layer starts",Vector)=(33,220,660,0)
        _LayerFadeWorld("Layer fade",Float)=11
        _CapSize("Soil lip size",Vector)=(9.6,2.4,0,0)
        _CapTopOffset("Soil lip top offset",Float)=1.15
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"}
        Pass
        {
            Tags {"LightMode"="Universal2D"}
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Layer0Tex); SAMPLER(sampler_Layer0Tex);
            TEXTURE2D(_Layer1Tex); SAMPLER(sampler_Layer1Tex);
            TEXTURE2D(_Layer2Tex); SAMPLER(sampler_Layer2Tex);
            TEXTURE2D(_Layer3Tex); SAMPLER(sampler_Layer3Tex);
            TEXTURE2D(_CapTex); SAMPLER(sampler_CapTex);
            CBUFFER_START(UnityPerMaterial)
            float _TopY;
            float4 _RepeatSize;
            float _SurfaceY;
            float4 _LayerStarts;
            float _LayerFadeWorld;
            float4 _CapSize;
            float _CapTopOffset;
            CBUFFER_END
            struct Input {float3 positionOS:POSITION;};
            struct Output {float4 positionCS:SV_POSITION;float2 world:TEXCOORD0;};
            Output Vert(Input input)
            {
                Output o;
                float3 world=TransformObjectToWorld(input.positionOS);
                o.positionCS=TransformWorldToHClip(world);o.world=world.xy;
                return o;
            }
            half4 Frag(Output input):SV_Target
            {
                // Mirror the non-seamless paintings at their boundaries so a
                // repeated layer has no hard vertical or horizontal cut.
                float2 phase=(input.world-float2(0,_TopY))/max(_RepeatSize.xy,.001);
                phase.x+=0.5;
                float2 uv=1.0-abs(frac(phase*0.5)*2.0-1.0);
                float depth=_SurfaceY-input.world.y;
                half3 underground=SAMPLE_TEXTURE2D(_Layer0Tex,sampler_Layer0Tex,uv).rgb;
                float fade=max(_LayerFadeWorld,.001);
                if(depth>_LayerStarts.x-fade)
                {
                    half3 next=SAMPLE_TEXTURE2D(_Layer1Tex,sampler_Layer1Tex,uv).rgb;
                    float upperAlpha=1.0-smoothstep(_LayerStarts.x-fade,_LayerStarts.x,depth);
                    underground=lerp(next,underground,upperAlpha);
                }
                if(depth>_LayerStarts.y-fade)
                {
                    half3 next=SAMPLE_TEXTURE2D(_Layer2Tex,sampler_Layer2Tex,uv).rgb;
                    float upperAlpha=1.0-smoothstep(_LayerStarts.y-fade,_LayerStarts.y,depth);
                    underground=lerp(next,underground,upperAlpha);
                }
                if(depth>_LayerStarts.z-fade)
                {
                    half3 next=SAMPLE_TEXTURE2D(_Layer3Tex,sampler_Layer3Tex,uv).rgb;
                    float upperAlpha=1.0-smoothstep(_LayerStarts.z-fade,_LayerStarts.z,depth);
                    underground=lerp(next,underground,upperAlpha);
                }
                half baseAlpha=step(input.world.y,_TopY);
                float2 capUV=float2(input.world.x/_CapSize.x,
                    (input.world.y-(_TopY+_CapTopOffset-_CapSize.y))/_CapSize.y);
                half4 cap=half4(0,0,0,0);
                if (input.world.y >= _TopY - 1.0 &&
                    capUV.y >= 0.0 && capUV.y <= 1.0)
                    cap=SAMPLE_TEXTURE2D(_CapTex,sampler_CapTex,capUV);
                half alpha=saturate(baseAlpha+cap.a*(1-baseAlpha));
                clip(alpha-.01h);
                return half4(lerp(underground,cap.rgb,cap.a),alpha);
            }
            ENDHLSL
        }
    }
}
