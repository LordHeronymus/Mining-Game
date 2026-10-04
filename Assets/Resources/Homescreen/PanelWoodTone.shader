Shader "UI/Tiefenhall/PanelWoodTone"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _WoodTone ("Wood Tone", Color) = (.72,.67,.62,1)
        _WoodUvRect ("Wood UV Rect", Vector) = (0,0,1,1)
        _BrownOnly ("Protect Decorative Details", Float) = 0
        _UseRegions ("Use Regions", Float) = 0
        _Region0 ("Wood Region 0", Vector) = (0,0,0,0)
        _Region1 ("Wood Region 1", Vector) = (0,0,0,0)
        _Region2 ("Wood Region 2", Vector) = (0,0,0,0)
        _Region3 ("Wood Region 3", Vector) = (0,0,0,0)
        _GoldArt ("Illustrated Gold", 2D) = "white" {}
        _GoldArtRect ("Gold Art UV", Vector) = (0,0,1,1)
        _UseGoldArt ("Use Illustrated Gold", Float) = 0
        _GoldOnly ("Gold Interior Only", Float) = 0
        _GoldFill ("Gold Selection", Float) = 0
        _GoldUvRect ("Gold Sprite UV", Vector) = (0,0,1,1)
        _GoldInsets ("Gold Insets", Vector) = (.087,.19,.087,.19)
        _GoldChamfer ("Gold Corner Cut", Float) = 0
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
        Blend SrcAlpha OneMinusSrcAlpha
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
            sampler2D _MainTex, _GoldArt; float4 _GoldArtRect; float _UseGoldArt;
            float4 _Color, _ClipRect, _WoodTone, _WoodUvRect;
            float4 _TextureSampleAdd;
            float4 _Region0, _Region1, _Region2, _Region3;
            float _UseRegions, _BrownOnly, _GoldFill, _GoldChamfer, _GoldOnly; float4 _GoldUvRect, _GoldInsets;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color*_Color; o.uv=v.uv;
                float2 pixelSize=o.vertex.w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 rect=clamp(_ClipRect,-2e10,2e10);
                o.mask=float4(v.vertex.xy*2-rect.xy-rect.zw, .25/(.25*float2(_UIMaskSoftnessX,_UIMaskSoftnessY)+abs(pixelSize)));
                return o;
            }
            float Region(float2 uv, float4 rect)
            {
                float2 edge=min(uv-rect.xy,rect.xy+rect.zw-uv);
                return smoothstep(0,.003,edge.x)*smoothstep(0,.003,edge.y);
            }
            half4 frag(v2f i):SV_Target
            {
                half4 color=(tex2D(_MainTex,i.uv)+_TextureSampleAdd)*i.color;
                float2 local=(i.uv-_WoodUvRect.xy)/_WoodUvRect.zw;
                // Fade only across the inner wooden lip, leaving the metal trim untouched.
                float2 edge=min(local,1-local);
                float wood=smoothstep(.043,.055,edge.x)*smoothstep(.063,.075,edge.y)*smoothstep(.27,.29,edge.x+edge.y);
                float regions=max(max(Region(local,_Region0),Region(local,_Region1)),max(Region(local,_Region2),Region(local,_Region3)));
                wood=lerp(wood,regions,_UseRegions);
                float warmth=color.g/max(color.r,.0001);
                float brown=smoothstep(.23,.33,warmth)*(1-smoothstep(.62,.76,warmth))*(1-smoothstep(.72,.9,max(color.r,max(color.g,color.b))));
                wood*=lerp(1,brown,_BrownOnly);
                color.rgb*=lerp(half3(1,1,1),_WoodTone.rgb,wood);
                float2 goldUv=(i.uv-_GoldUvRect.xy)/_GoldUvRect.zw;
                float2 goldEdge=min(goldUv-_GoldInsets.xy,1-_GoldInsets.zw-goldUv);
                float goldMask=smoothstep(0,.004,goldEdge.x)*smoothstep(0,.008,goldEdge.y)
                    *smoothstep(_GoldChamfer,_GoldChamfer+.008,goldEdge.x+goldEdge.y);
                float height=saturate((goldUv.y-_GoldInsets.y)/(1-_GoldInsets.y-_GoldInsets.w));
                float grain=dot(color.rgb,float3(.299,.587,.114));
                half3 gold=lerp(half3(.32,.045,.002),half3(.68,.21,.004),height)*(.58+1.25*grain);
                float rim=exp(-max(0,goldEdge.y)*45);
                gold=lerp(gold,color.rgb*half3(2.5,1.6,.3),.32);
                gold+=half3(.75,.39,.016)*rim;
                float2 artUv=saturate((goldUv-_GoldInsets.xy)/(1-_GoldInsets.xy-_GoldInsets.zw));
                half3 paintedGold=tex2D(_GoldArt,_GoldArtRect.xy+artUv*_GoldArtRect.zw).rgb*i.color.rgb;
                gold=lerp(gold,paintedGold,_UseGoldArt);
                color.rgb=lerp(color.rgb,gold,saturate(_GoldFill)*goldMask);
                color.a*=lerp(1,goldMask,_GoldOnly);
                #ifdef UNITY_UI_CLIP_RECT
                half2 mask=saturate((_ClipRect.zw-_ClipRect.xy-abs(i.mask.xy))*i.mask.zw);
                color.a*=mask.x*mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a-.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}