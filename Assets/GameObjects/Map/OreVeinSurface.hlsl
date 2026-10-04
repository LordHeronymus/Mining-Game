#ifndef MINING_ORE_VEIN_SURFACE
#define MINING_ORE_VEIN_SURFACE

TEXTURE2D(_VeinCells);
SAMPLER(sampler_VeinCells);
TEXTURE2D(_VeinRelief);
SAMPLER(sampler_VeinRelief);

float VeinHash(float2 p)
{
    float3 q = frac(float3(p.xyx) * .1031 + _VeinSeed * .0137);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float2 VeinAnchor(float2 cell)
{
    return cell + .5 + (float2(VeinHash(cell), VeinHash(cell + 37.7)) - .5) * .28;
}

float VeinNoise(float2 p)
{
    float2 c=floor(p),f=frac(p); f=f*f*(3-2*f);
    return lerp(lerp(VeinHash(c),VeinHash(c+float2(1,0)),f.x),
        lerp(VeinHash(c+float2(0,1)),VeinHash(c+1),f.x),f.y);
}

float2 VeinCell(float2 cell)
{
    float2 p = cell - _VeinBounds.xy;
    if (min(p.x, p.y) < 0 || p.x >= _VeinBounds.z || p.y >= _VeinBounds.w) return 0;
    return floor(SAMPLE_TEXTURE2D_LOD(_VeinCells, sampler_VeinCells,
        (p + .5) / _VeinBounds.zw, 0).rg * 255 + .5);
}

float2 VeinSegment(float2 p, float2 a, float2 b)
{
    float2 d = b - a;
    float2 delta = p - a - d * saturate(dot(p-a,d) / max(dot(d,d), .00001));
    return float2(length(delta), dot(delta,normalize(float2(-d.y,d.x))));
}

float2 NearerVein(float2 a,float2 b) { return a.x < b.x ? a : b; }

// Both tiles evaluate the SAME canonically ordered polyline across their shared edge.
// This avoids seams even with different richness, sprite variants, or host rock.
float2 VeinLink(float2 p, float2 c, float2 other)
{
    float2 lo = min(c,other), hi = max(c,other);
    float2 a = VeinAnchor(lo), b = VeinAnchor(hi), d = b-a;
    float2 n = normalize(float2(-d.y,d.x));
    float bend = (VeinHash(lo + hi * 3.17) - .5) * .23;
    float2 m = lerp(a,b,.34) + n*bend;
    float2 k = lerp(a,b,.68) - n*bend*.7;
    return NearerVein(VeinSegment(p,a,m), NearerVein(VeinSegment(p,m,k), VeinSegment(p,k,b)));
}

half3 VeinTint(int type)
{
    if(type==1) return half3(.32,.36,.40); // iron
    if(type==2) return half3(.68,.31,.12); // copper
    if(type==3) return half3(.90,.59,.16); // gold
    if(type==4) return half3(.65,.78,.84); // silver
    if(type==6) return half3(.77,.72,.89); // platinum
    if(type==7) return half3(.055,.065,.078); // coal band
    if(type==8) return half3(.69,.86,.94); // diamond gangue
    if(type==13) return half3(.38,.13,.78); // ultronium
    if(type==14) return half3(.43,.59,.67); // titanium
    if(type==15) return half3(.40,.42,.50); // tungsten
    if(type==16) return half3(.95,.23,.035); // garnet
    if(type==17) return half3(.22,.75,.82); // mythril
    if(type==18) return half3(.07,.52,.27); // emerald
    if(type==19) return half3(.71,.055,.12); // ruby
    return half3(.5,.5,.5);
}

half4 VeinOreSample(float2 uv)
{
    half4 c = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,saturate(uv));
    c.a *= step(0,min(min(uv.x,uv.y),min(1-uv.x,1-uv.y)));
    return c;
}

half4 ConnectedOreFragment(Varyings i, float2 cellPosition, float2 cellData)
{
    float2 cell = floor(cellPosition), anchor = VeinAnchor(cell);
    int type = (int)cellData.x - 1;
    bool coal = type == 7;
    bool gem = type == 8 || type == 16 || type == 18 || type == 19 || type == 13;
    half3 tint = VeinTint(type);
    float2 path = VeinSegment(cellPosition,anchor-float2(.12,.04),anchor+float2(.14,.03));
    float node = 1-smoothstep(.10,.37,length(cellPosition-anchor));
    float boundary = 1;
    [unroll] for(int side=0;side<4;side++)
    {
        float2 direction = side==0 ? float2(1,0) : side==1 ? float2(0,1) : side==2 ? float2(-1,0) : float2(0,-1);
        float2 neighbour = cell + direction;
        if(VeinCell(neighbour).x == cellData.x)
        {
            path = NearerVein(path,VeinLink(cellPosition,cell,neighbour));
            node = max(node,1-smoothstep(.10,.37,length(cellPosition-VeinAnchor(neighbour))));
        }
        else if(VeinHash(cell + side*4.7 + 8.1) > .63)
        {
            // Blind fissures end INSIDE the remaining block, never in mined-out space.
            float2 end = anchor + direction * (.20 + VeinHash(cell+side*7.1)*.13);
            float2 middle = lerp(anchor,end,.55) + float2(-direction.y,direction.x) *
                (VeinHash(cell+side+12.3)-.5)*.11;
            float taper = 1-smoothstep(.08,.34,length(cellPosition-anchor));
            float2 dist = NearerVein(VeinSegment(cellPosition,anchor,middle),VeinSegment(cellPosition,middle,end));
            dist.x += (1-taper)*.08;
            path = NearerVein(path,dist);
        }
        if(VeinCell(neighbour).x != cellData.x)
            boundary *= smoothstep(.025,.14,.5-dot(cellPosition-cell-.5,direction));
    }
    float aa = max(fwidth(cellPosition.x),fwidth(cellPosition.y)) * .8;
    float crackDistance=path.x;
    float grain = VeinNoise(cellPosition*11.7);
    float reliefWidth = 1.30 + node*.60;
    float2 reliefUV=float2(frac(dot(cellPosition,float2(.23,.17))),.5+path.y/reliefWidth);
    half4 relief=SAMPLE_TEXTURE2D(_VeinRelief,sampler_VeinRelief,reliefUV);
    float fracture=relief.a*boundary*(1-smoothstep(.28+node*.12,.46+node*.13,crackDistance));
    float cavity=(1-smoothstep(.20,.42,relief.r))*fracture;
    float width = (coal ? .060 : .008) + grain * (coal ? .060 : .017);
    float mineral = cavity*(1-smoothstep(width-aa,width+aa,crackDistance));
    mineral *= smoothstep(.28,.65,grain);
    float core = mineral*(1-smoothstep(width*.25,width*.55+aa,crackDistance));

    // Upright crystals, with small stable shifts/scales rather than a repeated grid of heaps.
    float scale = _OreScale * lerp(.83,1.03,VeinHash(cell+19.7));
    float2 local = (i.uv-.5)*_OreScale+.5;
    float2 uv = (local - (anchor-cell)) / max(scale,.01) + .5;
    half4 ore = VeinOreSample(uv);
    ore.a = smoothstep(.05,.80,ore.a);
    float edgeRadius = lerp(.028,.060,grain) / max(scale,.5);
    half inner = ore.a, outer = ore.a;
    half4 shimmer = 0;
    [unroll] for(int k=0;k<4;k++)
    {
        float2 d = k==0?float2(1,0):k==1?float2(-1,0):k==2?float2(0,1):float2(0,-1.3);
        half a = VeinOreSample(uv+d*edgeRadius).a;
        inner = min(inner,a); outer = max(outer,a);
        half4 nearby = VeinOreSample(uv+d*_ShimmerRadius);
        shimmer += half4(nearby.rgb*nearby.a,nearby.a)*.25h;
    }
    half3 rock = _UniformStone.x > 0 ? TerrainMaterialSample(i.positionWS.xy).rgb : half3(.25,.22,.18);
    float2 relative = cellPosition-anchor;
    float crags = .5 + .28*sin(cellPosition.x*17+cellPosition.y*9) + .22*sin(cellPosition.y*23-cellPosition.x*11);
    float rim = (ore.a-inner)*smoothstep(.20,.66,crags);
    float lowerLip = (1-smoothstep(-.27,.02,relative.y)) *
        smoothstep(.53,.61,crags) * (1-smoothstep(.21,.39,length(relative)));
    float paintedLip=smoothstep(.25,.49,relief.r)*fracture;
    float lip = saturate(max(max(rim,lowerLip),paintedLip)*_EmbeddingStrength);
    half crystalAlpha = ore.a*(1-lip);
    half socket = max(fracture,outer*.91);
    half alpha = max(socket,ore.a);
    half facet = pow(smoothstep(.2,.94,max(ore.r,max(ore.g,ore.b))),3);
    half3 crystal = ore.rgb * (gem ? .80 : .88);
    half peak = max(ore.r,max(ore.g,ore.b));
    half3 saturatedOre=pow(saturate(ore.rgb/max(.0001h,peak)),1.6h)*peak;
    crystal += saturatedOre * facet * _ReflectionStrength * (gem ? .45 : .35);
    crystal *= 1-(ore.a-inner)*.34;
    // Keep the host hue but let the painted fracture supply its own faceted relief.
    // Multiplying by the full host texture again would hide the new rock edges.
    half3 fissure = rock/max(.02h,max(rock.r,max(rock.g,rock.b))) * pow(max(0,relief.r),2.2h) * .65h;
    half3 seam = tint * lerp(.38,.80,grain);
    if(type==8) seam = lerp(seam,half3(.91,.92,.88),core*.6); // pale mineral matrix
    fissure = lerp(fissure,seam,mineral*.78);
    // Relief luminance gives the fractured lips depth while the colour comes from the host.
    half3 combined = lerp(fissure,crystal,crystalAlpha);
    combined = lerp(combined,rock,ore.a*lip);
    combined += shimmer.rgb * _ShimmerStrength * (1-crystalAlpha) * .25h;
    half4 mask = SAMPLE_TEXTURE2D(_MaskTex,sampler_MaskTex,saturate(uv));
    SurfaceData2D surfaceData;
    InputData2D inputData;
    InitializeSurfaceData(combined*i.color.rgb,alpha*i.color.a,mask,surfaceData);
    InitializeInputData(i.uv,i.lightingUV,inputData);
    half4 lit = CombinedShapeLightShared(surfaceData,inputData);
    // Ordinary minerals still disappear in total darkness. Only Ultronium emits.
    half3 incident = saturate(lit.rgb/max(half3(.005,.005,.005),combined*i.color.rgb));
    half litPeak=max(lit.r,max(lit.g,lit.b));
    half compressed=litPeak<=.7h ? litPeak : .7h+.3h*(1-exp(-(litPeak-.7h)/.3h));
    lit.rgb *= compressed/max(litPeak,.0001h);
    half glow = type==16 ? .36h : type==13 ? .55h : 0;
    half3 exposure = type==13 ? UltroniumPulse(cell,_Time.y,_UltroniumPulsesPerMinute).xxx : incident;
    lit.rgb += tint * glow * exposure * (core*(1-ore.a)*.8 + facet*crystalAlpha*.4);
    if(type==13) lit.rgb += (saturatedOre*.48h+_UltroniumGlowColor.rgb*facet*.12h) *
        _UltroniumGlow * exposure * crystalAlpha;
    return lit;
}
#endif
