Shader "Tiefenhall/HomeCave"
{
    Properties { _MainTex("Cave", 2D) = "white" {} _SceneTime("Time", Float) = 0 _Parallax("Parallax", Vector) = (0,0,0,0) _CaveEffects("L3 Effects", Float) = 1 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode] Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            sampler2D _MainTex; float _SceneTime, _CaveEffects; float4 _Parallax;
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
            float zone(float2 uv, float2 center, float2 radius) { float2 p=(uv-center)/radius; return exp(-dot(p,p)*2); }
            float insideLake(float2 p)
            {
                float top=.432-.06*saturate((.47-p.x)/.22);
                float bottom=.11+.42*abs(p.x-.62);
                float water=smoothstep(bottom,bottom+.012,p.y)*(1-smoothstep(top-.008,top+.008,p.y));
                water*=smoothstep(.25,.28,p.x)*(1-smoothstep(.96,.99,p.x));
                water*=1-zone(p,float2(.465,.419),float2(.055,.035));
                water*=1-zone(p,float2(.801,.387),float2(.078,.027));
                water*=1-smoothstep(.77,.82,p.x)*(1-smoothstep(.25,.34,p.y));
                return saturate(water);
            }
            fixed4 frag(v2f i):SV_Target
            {
                float t=_SceneTime;
                float foreground=saturate(pow(abs(i.uv.x-.5)*2,3)+pow(saturate(.3-i.uv.y)*3,2));
                float2 uv=(i.uv-.5)*.985+.5+_Parallax.xy*lerp(.18,1,foreground);
                float water=insideLake(uv);
                // The approved deep lake stays flat; movement comes from light and occasional drips.
                fixed4 c=tex2D(_MainTex,uv)*i.color;
                if(_CaveEffects<.5) return c;
                float crystals=zone(uv,float2(.382,.576),float2(.06,.105))+zone(uv,float2(.73,.587),float2(.053,.075))+
                    zone(uv,float2(.95,.523),float2(.04,.071))+zone(uv,float2(.649,.475),float2(.032,.044));
                float blue=saturate(c.b-c.r-.09);
                c.rgb+=float3(.06,.18,.26)*crystals*blue*(.45+.35*sin(t*.48)+.12*sin(t*.77));
                float lantern=zone(uv,float2(.05,.8),float2(.073,.16))+zone(uv,float2(.923,.63),float2(.035,.061));
                float flame=(sin(t*4.9)+sin(t*7.31+2)+sin(t*2.17))*.014;
                c.rgb+=float3(1,.45,.11)*lantern*(.025+flame);
                // Sparse drips: every cycle includes a long quiet interval and three fading water rings.
                float cycle=floor(t/9.7), phase=frac(t/9.7)*9.7;
                float2 hit=float2(.61+.11*sin(cycle*2.37),.227+.027*sin(cycle*1.73));
                float age=phase-6.2;
                float2 delta=(uv-hit)/float2(.1,.028); float d=length(delta);
                float ripple=0;
                for(int n=0;n<3;n++)
                {
                    float a=age-n*.22;
                    float r=max(0,a)*.5;
                    ripple+=exp(-pow((d-r)*100,2))*saturate(1-a/2.8)*step(0,a)*step(a,2.8);
                }
                c.rgb+=float3(.3,.62,.77)*ripple*water*.35;
                float dropY=hit.y+.3*(1-saturate((phase-5.65)/.55));
                float drop=zone(uv,float2(hit.x,dropY),float2(.00065,.005))*step(5.65,phase)*step(phase,6.2);
                c.rgb+=float3(.3,.58,.7)*drop*.65;
                return c;
            }
            ENDCG
        }
    }
}
