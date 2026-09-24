Shader "Mining Game/Fixed Underground"
{
    Properties
    {
        _UpperTex("Upper clay",2D)="white"{}
        _LowerTex("Lower sandstone",2D)="white"{}
        _CapTex("Upper soil lip",2D)="black"{}
        _TopY("Top Y",Float)=1
        _RepeatSize("Repeat size",Vector)=(16.5,11,0,0)
        _FadeDepth("Fade depth",Vector)=(110,220,0,0)
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
            TEXTURE2D(_UpperTex); SAMPLER(sampler_UpperTex);
            TEXTURE2D(_LowerTex); SAMPLER(sampler_LowerTex);
            TEXTURE2D(_CapTex); SAMPLER(sampler_CapTex);
            CBUFFER_START(UnityPerMaterial)
            float _TopY;
            float4 _RepeatSize;
            float4 _FadeDepth;
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
                float2 uv=(input.world-float2(0,_TopY))/max(_RepeatSize.xy,.001);
                half3 upper=SAMPLE_TEXTURE2D(_UpperTex,sampler_UpperTex,uv).rgb;
                half3 lower=SAMPLE_TEXTURE2D(_LowerTex,sampler_LowerTex,uv).rgb;
                float t=smoothstep(_FadeDepth.x,max(_FadeDepth.x+.001,_FadeDepth.y),_TopY-input.world.y);
                half baseAlpha=step(input.world.y,_TopY);
                float2 capUV=float2(input.world.x/_CapSize.x,
                    (input.world.y-(_TopY+_CapTopOffset-_CapSize.y))/_CapSize.y);
                half4 cap=SAMPLE_TEXTURE2D(_CapTex,sampler_CapTex,capUV);
                cap.a*=step(0,capUV.y)*step(capUV.y,1);
                half alpha=saturate(baseAlpha+cap.a*(1-baseAlpha));
                clip(alpha-.01h);
                return half4(lerp(lerp(upper,lower,t),cap.rgb,cap.a),alpha);
            }
            ENDHLSL
        }
    }
}
