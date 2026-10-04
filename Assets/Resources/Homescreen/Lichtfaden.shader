Shader "UI/Tiefenhall/Lichtfaden"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _CaretHeight ("Caret Height", Float) = 36
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
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 mask:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            float4 _Color, _ClipRect;
            float _CaretHeight, _UIMaskSoftnessX, _UIMaskSoftnessY;
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color*_Color; o.uv=v.uv;
                float2 pixelSize=o.vertex.w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 rect=clamp(_ClipRect,-2e10,2e10);
                o.mask=float4(v.vertex.xy*2-rect.xy-rect.zw, .25/(.25*float2(_UIMaskSoftnessX,_UIMaskSoftnessY)+abs(pixelSize)));
                return o;
            }
            half4 frag(v2f i):SV_Target
            {
                float2 p=i.uv;
                float endDistance=max(max(-p.y,p.y-_CaretHeight),0);
                float lineDistance2=p.x*p.x+endDistance*endDistance;
                float width=clamp(_CaretHeight*.018,.48,.72);
                float core=exp(-lineDistance2/(width*width)) * .82;
                float inner=exp(-lineDistance2/2.8) * .28;
                float outer=exp(-lineDistance2/18) * .045;
                float2 head=p-float2(0,_CaretHeight);
                float r=length(head);
                float radius=clamp(_CaretHeight*.085,2.1,3.5);
                float bead=(1-smoothstep(radius*.45,radius,r))*.95;
                float beadInner=exp(-dot(head,head)/(radius*radius*1.5))*.28;
                float beadHalo=exp(-dot(head,head)/(radius*radius*5))*.10;
                float foot=exp(-dot(p,p)/1.8)*.25;
                half3 rgb=half3(1,.78,.32)*max(core,max(bead,foot))+half3(1,.38,.012)*(inner+outer+beadInner+beadHalo);
                half alpha=i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                half2 mask=saturate((_ClipRect.zw-_ClipRect.xy-abs(i.mask.xy))*i.mask.zw);
                alpha*=mask.x*mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha-.001);
                #endif
                rgb *= 1-smoothstep(5,10,sqrt(lineDistance2));
                return half4(rgb*i.color.rgb*alpha,0);
            }
            ENDCG
        }
    }
}