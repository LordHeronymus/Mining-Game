#ifndef MINING_COPPER_EARTH_TILES
#define MINING_COPPER_EARTH_TILES

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

TEXTURE2D_ARRAY(_CopperRockCombined);
SAMPLER(sampler_CopperRockCombined);
TEXTURE2D(_CopperL1Sprite);
TEXTURE2D(_CopperL1Large);
TEXTURE2D(_CopperL1Small);
TEXTURE2D(_CopperL2Small);
TEXTURE2D(_CopperL2Medium);
TEXTURE2D(_CopperL2Large);


// The complete cell follows exactly the terrain's existing mined-edge contour.
#define TERRAIN_FRAYED_TERRAIN 1
#include "TerrainCellSurface.hlsl"

// Sample the material at the actual cell centre. Filtered transition pixels at
// its edge must not switch a single ore cell between two rendering styles.
half4 CopperTileMaterial(float2 worldPosition)
{
    float2 cell = floor((worldPosition - _UniformStone.zw) / _UniformStone.x) + .5;
    float2 maskUV = (cell - _TestBounds.xy) / _TestBounds.zw;
    return SAMPLE_TEXTURE2D_LOD(_TestOccupancy, sampler_TestOccupancy, maskUV, 0);
}

bool CopperEarthTileHost(float2 worldPosition)
{
    if (_UniformStone.x <= 0) return false;
    half4 material = CopperTileMaterial(worldPosition);
    return material.r > .5 && (material.g > .5 ? _CopperEarthTilesEnabled > .5 : _CopperRockTilesEnabled > .5);
}

float CopperEarthMotif(float2 cell, float richness)
{
    float variant = step(.5, VeinHash(cell + 91.37));
    return (richness < 1.5 ? 0 : richness < 2.5 ? 2 : 4) + variant;
}

half4 CopperRockTileSample(float2 p, float richness, float2 worldPosition)
{
    float2 cell = floor(p);
    // Positive modulo is essential for underground/negative map coordinates.
    float2 phase = cell - floor(cell / 3) * 3;
    float phaseIndex = phase.y * 3 + phase.x;
    float motif = CopperEarthMotif(cell,richness);
    half4 hostMaterial = CopperTileMaterial(worldPosition);
    // Same centre-cell material encoding as UniformStoneAppearance: clay,
    // neutral grey, warm rock, deep slate. Never infer host from ore depth.
    float rock = hostMaterial.b > .5 ? 0 : hostMaterial.a > .75 ? 3 : hostMaterial.a > .25 ? 2 : 1;
    uint width=0,height=0,slices=0;
    _CopperRockCombined.GetDimensions(width,height,slices);
    // GetDimensions reports the uploaded mip size, which changes with the
    // user's texture quality. Atlas positions must remain normalized instead.
    float padding=8.0/224.0;
    float inner=1-2*padding;
    float2 dimensions=float2(width,height);
    float2 uv = (float2(phaseIndex,motif) + padding + frac(p)*inner) / float2(9,6);
    // Different atlas slots must not inflate the screen derivatives at cell
    // boundaries. Sixteen padding pixels support mip levels through four.
    float2 dx=ddx(p)*inner*dimensions/float2(9,6),dy=ddy(p)*inner*dimensions/float2(9,6);
    float mipFootprint=max(length(dx),length(dy));
    float limit=min(1,(width/9.0)*padding/max(mipFootprint,.001));
    return SAMPLE_TEXTURE2D_ARRAY_GRAD(_CopperRockCombined, sampler_CopperRockCombined, uv, rock,
        dx*limit/dimensions,dy*limit/dimensions);
}

half4 CopperSpriteArtSample(float2 uv, float2 dx, float2 dy, float richness, bool clay)
{
    if(clay)
    {
        if(richness < 1.5) return SAMPLE_TEXTURE2D_GRAD(_CopperL2Small,sampler_CopperRockCombined,uv,dx,dy);
        if(richness > 2.5) return SAMPLE_TEXTURE2D_GRAD(_CopperL2Large,sampler_CopperRockCombined,uv,dx,dy);
        return SAMPLE_TEXTURE2D_GRAD(_CopperL2Medium,sampler_CopperRockCombined,uv,dx,dy);
    }
    if(richness < 1.5) return SAMPLE_TEXTURE2D_GRAD(_CopperL1Small,sampler_CopperRockCombined,uv,dx,dy);
    if(richness > 2.5) return SAMPLE_TEXTURE2D_GRAD(_CopperL1Large,sampler_CopperRockCombined,uv,dx,dy);
    return SAMPLE_TEXTURE2D_GRAD(_CopperL1Sprite,sampler_CopperRockCombined,uv,dx,dy);
}

half4 CopperSpriteSample(float2 p, float richness, bool clay)
{
    // S/M/L art uses the same canvas mapping and orientation.
    // Small has more transparent margin; gameplay quantities are unchanged.
    const float size=.90;
    float2 uv=(frac(p)-.5)/size+.5;
    clip(min(min(uv.x,uv.y),min(1-uv.x,1-uv.y)));
    // World derivatives avoid false mip levels where the cell UV wraps.
    return CopperSpriteArtSample(uv,ddx(p)/size,ddy(p)/size,richness,clay);
}

half4 CopperSpriteEmbedded(float2 p, half4 art, half3 ground, float richness, bool clay)
{
    if (_EmbeddingStrength <= 0) return art;
    float2 uv=(frac(p)-.5)/.90+.5;
    float2 dx=ddx(p)/.90, dy=ddy(p)/.90;
    // Read the actual earth at its world phase, not random dirt or sprite colour.
    // Bright broad faces approximate raised clods; dark native cracks stay open.
    float2 earthUV=p/_UniformStone.y;
    half3 earth=SAMPLE_TEXTURE2D_GRAD(_SurfaceDirtTex,sampler_SurfaceDirtTex,
        earthUV,ddx(earthUV)*2,ddy(earthUV)*2).rgb;
    if(clay) earth=SAMPLE_TEXTURE2D_GRAD(_LayerOneTex,sampler_LayerOneTex,
        earthUV,ddx(earthUV)*2,ddy(earthUV)*2).rgb;
    half height=dot(LinearToSRGB(earth),half3(.2126,.7152,.0722));
    // Claystone is darker than loose surface dirt; use its lower relief range.
    if(clay) height+=.08h;
    half3 pigment=LinearToSRGB(art.rgb);
    half metal=smoothstep(.57h,.67h,(pigment.r-pigment.g)/max(.001h,pigment.r-pigment.b));
    // The approved sprite now carries substantial foreground clods. Merge
    // their outer feet more firmly into native earth while protecting copper.
    half clod=smoothstep(lerp(.34h,.40h,metal),lerp(.53h,.57h,metal),height);
    float reach=lerp(lerp(.020,.045,clod),lerp(.014,.036,clod),metal);
    half inner=art.a, wider=art.a;
    // A bounded erosion of the existing silhouette protects all interior metal
    // and most of the sprite's own crumbs. Stronger overlap at the lower foot.
    [unroll] for(int n=0;n<8;n++)
    {
        float angle=n*(TWO_PI/8);
        float2 direction=float2(cos(angle),sin(angle));
        direction.y*=direction.y<0?1.0:.50;
        inner=min(inner,CopperSpriteArtSample(uv+direction*reach,dx,dy,richness,clay).a);
        wider=min(wider,CopperSpriteArtSample(uv+direction*(reach+.009),dx,dy,richness,clay).a);
    }
    half lip=saturate((art.a-inner)*2)*clod*lerp(.65h,1.0h,metal)*saturate(_EmbeddingStrength);
    half contact=saturate(art.a-wider)*clod*(1-lip)*.26h*saturate(_EmbeddingStrength);
    half3 inside=lerp(art.rgb*(1-contact),ground,lip);
    // Tight down-right ground shadow, consistent with the painted upper-left
    // light. It never makes a uniform ring and remains inside the logical cell.
    half cast=CopperSpriteArtSample(uv+float2(-.010,.016),dx,dy,richness,clay).a;
    half shadow=max(0,cast-art.a)*.28h*saturate(_EmbeddingStrength);
    half alpha=art.a+shadow;
    half3 color=(inside*art.a+ground*.72h*shadow)/max(alpha,.0001h);
    return half4(color,alpha);
}

half4 CopperEarthTileFragment(Varyings i, float2 p)
{
    half4 host = TestStone(i.positionWS.xy,frac(p));
    half4 material= CopperTileMaterial(i.positionWS.xy);
    bool clay=material.g<=.5 && material.b>.5 && _CopperL2SpritesEnabled>.5;
    if(material.g>.5 || clay)
    {
        float richness=VeinCell(floor(p)).y;
        half4 art=CopperSpriteSample(p,richness,clay);
        half3 ground=TerrainMaterialSample(i.positionWS.xy).rgb;
        art=CopperSpriteEmbedded(p,art,ground,richness,clay);
        clip(art.a-.003h);
        half3 color=art.rgb*host.rgb/max(ground,half3(.001,.001,.001));
        clip(host.a-.003h);
        SurfaceData2D surfaceData=(SurfaceData2D)0;
        InputData2D inputData=(InputData2D)0;
        InitializeSurfaceData(color,host.a*art.a,half4(1,1,1,1),surfaceData);
        InitializeInputData(i.uv,i.lightingUV,inputData);
        return CombinedShapeLightShared(surfaceData,inputData);
    }
    half4 art = CopperRockTileSample(p, VeinCell(floor(p)).y, i.positionWS.xy);
    // The approved subtle colour grade affects red-orange copper, not the
    // yellow-brown soil. Grade before lighting so coloured lights cannot select soil.
    half3 pigment = LinearToSRGB(art.rgb);
    half chroma = max(pigment.r - pigment.b, .001h);
    half copperMask = smoothstep(.62h, .72h, (pigment.r - pigment.g) / chroma)
        * smoothstep(.06h, .16h, pigment.r - pigment.b);
    half luminance = dot(pigment, half3(.2126h,.7152h,.0722h));
    pigment = saturate(luminance + (pigment-luminance) * (1 + .15h*copperMask));
    // Rock families retain their existing grade. L1 returned above and never
    // passes its original earth through this colour correction.
    art.rgb = SRGBToLinear(pigment);
    half3 baseGround = TerrainMaterialSample(i.positionWS.xy).rgb;
    // The complete painted cell reaches its boundary. Only real mined terrain
    // edges apply the common contour and relief; no host strip hides the artwork.
    art.rgb*=host.rgb/max(baseGround,half3(.001,.001,.001));
    art.a=host.a;
    clip(art.a-.003h);
    SurfaceData2D surfaceData = (SurfaceData2D)0;
    InputData2D inputData = (InputData2D)0;
    // Earth, copper, soil lips and contact shadows are one baked RGB image.
    InitializeSurfaceData(art.rgb, art.a, half4(1,1,1,1), surfaceData);
    InitializeInputData(i.uv, i.lightingUV, inputData);
    return CombinedShapeLightShared(surfaceData, inputData);
}
#endif
