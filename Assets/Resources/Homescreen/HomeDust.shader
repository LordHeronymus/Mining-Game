Shader "Tiefenhall/HomeDust"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode] Blend SrcAlpha One
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
            fixed4 frag(v2f i):SV_Target { float2 d=i.uv*2-1; i.color.a*=pow(saturate(1-dot(d,d)),2); return i.color; }
            ENDCG
        }
    }
}
