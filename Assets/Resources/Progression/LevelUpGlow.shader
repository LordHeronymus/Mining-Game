Shader "UI/Tiefenhall/LevelUpGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture",2D)="white" {}
        _Color ("Tint",Color)=(1,1,1,1)
        _SealTex ("Seal silhouette",2D)="white" {}
        _SealRect ("Seal UV rect",Vector)=(0,0,1,1)
        _SealSize ("Seal size",Float)=360
        _StencilComp ("Stencil Comparison",Float)=8
        _Stencil ("Stencil ID",Float)=0
        _StencilOp ("Stencil Operation",Float)=0
        _StencilWriteMask ("Stencil Write Mask",Float)=255
        _StencilReadMask ("Stencil Read Mask",Float)=255
        _ColorMask ("Color Mask",Float)=15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip",Float)=0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float2 mode:TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 mask:TEXCOORD1; float mode:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            float4 _Color,_ClipRect;
            sampler2D _SealTex;
            float4 _SealRect;
            float _SealSize;
            float _UIMaskSoftnessX,_UIMaskSoftnessY;
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex);o.color=v.color*_Color;o.uv=v.uv;o.mode=v.mode.x;
                float2 pixelSize=o.vertex.w/abs(mul((float2x2)UNITY_MATRIX_P,_ScreenParams.xy));
                float4 rect=clamp(_ClipRect,-2e10,2e10);
                o.mask=float4(v.vertex.xy*2-rect.xy-rect.zw,.25/(.25*float2(_UIMaskSoftnessX,_UIMaskSoftnessY)+abs(pixelSize)));
                return o;
            }
            half SealAlpha(float2 uv)
            {
                half valid=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
                return tex2D(_SealTex,_SealRect.xy+uv*_SealRect.zw).a*valid;
            }
            half SealHalo(float2 uv)
            {
                half tight=0,wide=0;
                [unroll] for(int y=-2;y<=2;y++)
                [unroll] for(int x=-2;x<=2;x++)
                {
                    float2 d=float2(x,y);half weight=exp(-.5*dot(d,d));
                    tight+=SealAlpha(uv+d*2.5/_SealSize)*weight;
                    wide+=SealAlpha(uv+d*9/_SealSize)*weight;
                }
                // A broad continuous aura, with the original silhouette maintaining contact
                // around the pointed crystal ornaments. The outer tail reaches zero before
                // the quad boundary, so the soft light never ends in a rectangular edge.
                float radius=length((uv-.5)*_SealSize);
                float distance=max(0,radius-127);
                half aura=.49*exp(-distance*distance/(58*58));
                aura*=1-smoothstep(245,285,radius);
                return ((tight*.65+wide*.35)/6.168924+aura)*(1-SealAlpha(uv));
            }
            half4 frag(v2f i):SV_Target
            {
                half alpha=i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                half2 mask=saturate((_ClipRect.zw-_ClipRect.xy-abs(i.mask.xy))*i.mask.zw);alpha*=mask.x*mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha-.001);
                #endif
                half feather=i.mode>.5?SealHalo(i.uv):exp(-i.uv.y*i.uv.y*4)*(1-smoothstep(.75,1,abs(i.uv.y)));
                // The wide haze gently tints the background gold instead of turning blue
                // sky pink. Orbit/rays keep alpha zero and therefore remain purely additive.
                return half4(i.color.rgb*alpha*feather*2.8,i.mode>.5?alpha*feather*.65:0);
            }
            ENDCG
        }
    }
}
