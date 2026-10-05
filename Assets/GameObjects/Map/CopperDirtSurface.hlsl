#ifndef MINING_COPPER_DIRT_SURFACE
#define MINING_COPPER_DIRT_SURFACE
TEXTURE2D(_CopperArt); SAMPLER(sampler_CopperArt);
TEXTURE2D(_CopperEarth); SAMPLER(sampler_CopperEarth);

float2 CopperAnchor(float2 c) { return c+.5+(VeinAnchor(c)-c-.5)*.65; }

half4 CopperAtlas(float2 uv,float variant,bool earth)
{
    float inside=step(0,min(min(uv.x,uv.y),min(1-uv.x,1-uv.y)));
    float2 tile=float2(fmod(variant,3),floor(variant/3));
    float2 atlas=(tile+clamp(uv,.002,.998))/float2(3,2);
    half4 art=earth?SAMPLE_TEXTURE2D(_CopperEarth,sampler_CopperEarth,atlas):
        SAMPLE_TEXTURE2D(_CopperArt,sampler_CopperArt,atlas);
    art.a=smoothstep(.06,.94,art.a)*inside;
    return art;
}

float2 CopperLink(float2 p,float2 c,float2 other)
{
    float2 lo=min(c,other),hi=max(c,other),a=CopperAnchor(lo),b=CopperAnchor(hi);
    float2 n=normalize(float2(-(b-a).y,(b-a).x));
    float2 previous=a;float2 nearest=100;
    [unroll]for(int k=1;k<=5;k++)
    {
        float t=k*.2;
        float bend=(VeinHash(lo+hi*2.31+k*17.3)-.5)*.32*sin(t*PI);
        float2 next=lerp(a,b,t)+n*bend;
        nearest=NearerVein(nearest,VeinSegment(p,previous,next));previous=next;
    }
    return nearest;
}

half4 CopperDirtFragment(Varyings i,float2 p,float2 data)
{
    float2 cell=floor(p),anchor=CopperAnchor(cell),relative=p-anchor;
    int links=(int)floor(SAMPLE_TEXTURE2D_LOD(_VeinCells,sampler_VeinCells,
        (cell-_VeinBounds.xy+.5)/_VeinBounds.zw,0).b*255+.5);
    float2 path=VeinSegment(p,anchor-float2(.035,.13),anchor+float2(.018,.15));
    float boundary=1;
    [unroll]for(int side=0;side<4;side++)
    {
        float2 d=side==0?float2(1,0):side==1?float2(0,1):side==2?float2(-1,0):float2(0,-1);
        if((links&(1<<side))!=0)path=NearerVein(path,CopperLink(p,cell,cell+d));
        else boundary*=smoothstep(.018,.11,.5-dot(p-cell-.5,d));
    }
    float aa=max(fwidth(p.x),fwidth(p.y))*.8;
    float noise=VeinNoise(p*7.3+13.1),cover=VeinNoise(p*2.7+8.4);
    float width=.028+noise*.050;
    // Buried sections interrupt the exposed fissure, while its branch layout remains continuous.
    float exposed=lerp(.55,1,smoothstep(.25,.70,cover))*boundary;
    float crack=(1-smoothstep(width-aa,width+aa,path.x))*exposed;
    float rim=(1-smoothstep(width+.012,width+.035,path.x))*exposed;
    half3 host=TerrainMaterialSample(i.positionWS.xy).rgb;
    half3 dirt=host*lerp(.72,1.13,saturate(.5+path.y*12));
    half3 colour=lerp(dirt,half3(.015,.007,.002),crack);
    float fleck=crack*smoothstep(.46,.74,noise)*(1-smoothstep(.006,.020,path.x));
    colour=lerp(colour,half3(.46,.16,.035),fleck*.75);
    float alpha=rim;

    // Compact authored openings overlap between specimens; no stretched rock strips.
    [unroll]for(int bank=0;bank<4;bank++)
    {
        if((links&(1<<bank))==0)continue;
        float2 d=bank==0?float2(1,0):bank==1?float2(0,1):bank==2?float2(-1,0):float2(0,-1);
        float2 lo=min(cell,cell+d),hi=max(cell,cell+d);
        float2 middle=(CopperAnchor(lo)+CopperAnchor(hi))*.5;
        float2 n=float2(-d.y,d.x);
        // Canonical direction gives both blocks the same opening at their border.
        n=abs(n);
        middle+=n*(VeinHash(lo+hi*2.31+34.6)-.5)*.08;
        float bridgeVariant=floor(VeinHash(lo+hi*3.7+42.1)*6);
        float2 bridgeSize=abs(d)*.80+(1-abs(d))*.50;
        half4 bridge=CopperAtlas((p-middle)/bridgeSize+.5,bridgeVariant,true);
        colour=lerp(colour,bridge.rgb,bridge.a);
        alpha=max(alpha,bridge.a);
        half4 chips=CopperAtlas((p-middle)/.29+.5,3+fmod(bridgeVariant,3),false);
        float bridgeCover=smoothstep(.020,.064,max(bridge.r,max(bridge.g,bridge.b)))*bridge.a;
        float chipAlpha=chips.a*(1-bridgeCover);
        colour=lerp(colour,chips.rgb*.80,chipAlpha);
        alpha=max(alpha,chipAlpha);
    }

    float choice=floor(VeinHash(cell+71.3)*3);
    float rich=saturate((data.y-1)*.5);
    float variant=choice+(rich>.45?0:3); // Upper PNG row is small, lower PNG row is large.
    float socketVariant=floor(VeinHash(cell+34.6)*3)+(rich>.45?0:3);
    float copperScale=_OreScale*lerp(.59,1.02,rich)*lerp(.90,1.06,VeinHash(cell+24));
    float earthScale=clamp(copperScale+.25,.82,1.12);
    half4 pocket=CopperAtlas(relative/earthScale+.5,socketVariant,true);
    half4 metal=CopperAtlas((relative-float2(0,.012))/max(.01,copperScale)+.5,variant,false);
    float brightness=max(pocket.r,max(pocket.g,pocket.b));
    float earthCover=smoothstep(.020,.064,brightness)*pocket.a*_EmbeddingStrength;
    float visibleMetal=metal.a*(1-earthCover);
    half4 edge=CopperAtlas((relative-float2(0,.012))/max(.01,copperScale)+.5+
        float2(0,.025/max(.5,_OreScale)),variant,false);
    float contact=(metal.a-edge.a)*(1-earthCover);
    half peak=max(metal.r,max(metal.g,metal.b));
    half3 saturated=pow(saturate(metal.rgb/max(.0001h,peak)),1.4h)*peak;
    half facets=pow(smoothstep(.12h,.85h,peak),3);
    half3 copper=metal.rgb+saturated*facets*_ReflectionStrength*.22;
    copper*=1-contact*.48;
    // Paint the socket first, insert metal, then keep the authored dirt ledges in front.
    colour=lerp(colour,pocket.rgb,pocket.a);
    colour=lerp(colour,copper,visibleMetal);
    alpha=max(alpha,max(pocket.a,visibleMetal));
    SurfaceData2D surface=(SurfaceData2D)0;InputData2D input=(InputData2D)0;
    InitializeSurfaceData(colour*i.color.rgb,alpha*i.color.a,half4(1,1,1,1),surface);
    InitializeInputData(i.uv,i.lightingUV,input);
    half4 lit=CombinedShapeLightShared(surface,input);
    half litPeak=max(lit.r,max(lit.g,lit.b));
    half compressed=litPeak<=.7h?litPeak:.7h+.3h*(1-exp(-(litPeak-.7h)/.3h));
    lit.rgb*=compressed/max(.0001h,litPeak);
    return lit;
}
#endif
