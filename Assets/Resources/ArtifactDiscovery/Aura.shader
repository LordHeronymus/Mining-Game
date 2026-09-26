Shader "UI/Artifact Discovery Aura"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} _Theme ("Theme", Color) = (0.4,0.9,0.5,1) _RevealTime ("Time", Float) = 0 }
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
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            float4 _Theme; float _RevealTime;
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
            float4 frag(v2f i):SV_Target
            {
                float2 p=(i.uv-.5)*float2(1.26,1);
                float r=length(p); float a=atan2(p.y,p.x);
                float pulse=1+.055*sin(_RevealTime*2.2);
                float haze=exp(-r*r*19)*.43*pulse;
                float beams=pow(saturate(sin(a*23+sin(a*13)*2+_RevealTime*.11)),4);
                beams+=pow(saturate(sin(a*53-_RevealTime*.07)),12)*.32;
                float rays=beams*exp(-r*7)*smoothstep(.12,.25,r)*.18;
                float edge=1-smoothstep(.24,.40,r);
                float alpha=(haze+rays)*edge;
                fixed3 col=lerp(_Theme.rgb, float3(.86,1,.70), saturate(rays*1.8)*.4);
                float noise=frac(52.9829189*frac(dot(i.vertex.xy,float2(.06711056,.00583715))))-.5;
                alpha*=i.color.a;
                alpha=max(0,alpha+noise/255.0)*step(.00001,alpha);
                return float4(col,alpha);
            }
            ENDCG
        }
    }
}
