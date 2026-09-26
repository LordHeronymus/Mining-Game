Shader "UI/Artifact Discovery Shimmer"
{
    Properties { [PerRendererData] _MainTex ("Sprite", 2D) = "white" {} _Theme ("Theme", Color)=(.4,.9,.5,1) _RevealTime ("Time", Float)=0 _SpriteRect("Sprite UV",Vector)=(0,0,1,1) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode] Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            sampler2D _MainTex; float4 _MainTex_TexelSize, _Theme, _SpriteRect; float _RevealTime;
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 col=tex2D(_MainTex,i.uv)*i.color;
                float2 local=(i.uv-_SpriteRect.xy)/_SpriteRect.zw;
                float sweep=exp(-pow((local.x+local.y*.35-(_RevealTime*.72-.3))/.095,2));
                col.rgb+=sweep*.075*lerp(_Theme.rgb,1,.7);
                return col;
            }
            ENDCG
        }
    }
}
