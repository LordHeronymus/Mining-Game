Shader "UI/Artifact Discovery Backdrop"
{
    Properties { _MainTex ("Game", 2D) = "black" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode] Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            sampler2D _MainTex; float4 _MainTex_TexelSize;
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                float3 col=0; float weight=0;
                for(int x=-2;x<=2;x++) for(int y=-2;y<=2;y++)
                {
                    float w=exp(-(x*x+y*y)*.38);
                    col+=tex2D(_MainTex,i.uv+float2(x,y)*_MainTex_TexelSize.xy*1.8).rgb*w;
                    weight+=w;
                }
                float2 p=(i.uv-.5)*2;
                float vignette=1-smoothstep(.15,1.3,length(p))*.68;
                col/=weight;
                float luminance=dot(col,float3(.2126,.7152,.0722));
                col*=.22/(.25+luminance)*vignette;
                return fixed4(col,i.color.a);
            }
            ENDCG
        }
    }
}
