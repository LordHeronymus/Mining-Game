Shader "UI/Workbench Item Glow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _AnimationTime ("Animation", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One One
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 world:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            float4 _Color, _ClipRect;
            float _AnimationTime;
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color*_Color; o.uv=v.uv; return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float2 p=(i.uv-.5)*2;
                float r=length(p), a=atan2(p.y,p.x), t=_AnimationTime;
                float rays=pow(.5+.5*sin(a*13+t*.06+sin(a*5)*.4),3);
                float pulse=.97+.03*sin(t*.65);
                // The faint rays stay near the core; only a smooth halo reaches the wood.
                float intensity=(exp(-r*r*6)*.28 + exp(-r*r*22)*.42
                    + rays*exp(-r*r*18)*.055)*pulse;
                intensity*=1-smoothstep(.72,.98,r);
                float opacity=intensity*i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                opacity*=UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(opacity-.001);
                #endif
                return float4(float3(1,.68,.22)*i.color.rgb*opacity,opacity);
            }
            ENDCG
        }
    }
}
