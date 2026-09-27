Shader "Mining Game/Ultronium Chamber"
{
    Properties { _MainTex("Texture",2D)="white"{} _Charge("Charge",Range(0,1))=0 _Power("Power",Float)=0 _Mode("Mode",Float)=0 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float _Charge, _Power, _Mode; float4 _AltarOrigin;
            struct A { float3 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float2 world:TEXCOORD1; half4 color:COLOR; };
            V Vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS);o.uv=i.uv;o.world=TransformObjectToWorld(i.positionOS).xy;o.color=i.color;return o; }
            half4 Frag(V i):SV_Target
            {
                float2 d=(i.world-_AltarOrigin.xy)/max(.01,_AltarOrigin.z);
                float influence=exp(-length(d)*lerp(.4,.095,_Charge));
                if(_Mode>4.5)
                {
                    float y=i.uv.y, x=i.uv.x-.5;
                    float height=lerp(.08,1,_Charge);
                    float fade=saturate(1-y/max(height,.001))*smoothstep(0,.08,y);
                    float wave=sin(y*13-_Time.y*2.1)*(.045+y*.12);
                    float wave2=sin(y*17+_Time.y*1.5)*(.07+y*.1);
                    float threads=exp(-abs(x-wave)*65)+.7*exp(-abs(x+wave2)*80);
                    float mist=exp(-x*x*20)*(.16+.08*sin(y*23-_Time.y*2));
                    float a=saturate((threads+mist)*fade*_Charge*.65);
                    return half4(1.4,.12,3.1,a);
                }
                if(_Mode>2.5)
                {
                    float2 p=i.uv*2-1;
                    float a=pow(saturate(1-dot(p,p)),_Mode>3.5?2:3);
                    if(_Mode>3.5) return half4(1.6,.28,2.6,a*i.color.a);
                    return half4(1.3,.13,2.8,a*min(.85,_Power*.5));
                }
                half4 c=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);
                float purple=saturate((min(c.r,c.b)-c.g)*12);
                float gray=dot(c.rgb,float3(.3,.5,.2));
                c.rgb=lerp(c.rgb,gray.xxx,purple*(1-_Charge)*.9);
                if(_Mode<.5) c.rgb=lerp(c.rgb,gray.xxx,.85*(1-_Charge));
                float brightness=_Mode<.5 ? .34 : (_Mode<1.5 ? .48 : .8);
                c.rgb*=brightness;
                c.rgb+=c.rgb*float3(.8,.12,1.9)*_Power*influence*(_Mode<.5 ? .6 : 1);
                c.rgb+=purple*float3(.7,.025,1.6)*_Power*(_Mode<.5?0:(_Mode>1.5?1.5:_Charge));
                return c;
            }
            ENDHLSL
        }
    }
}
