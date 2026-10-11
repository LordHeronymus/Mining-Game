#ifndef MINING_COPPER_EARTH_TILES
#define MINING_COPPER_EARTH_TILES

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

TEXTURE2D(_CopperEarthCombined);
TEXTURE2D_ARRAY(_CopperRockCombined);
SAMPLER(sampler_CopperEarthCombined);

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

half4 CopperEarthTileSample(float2 p, float richness, float2 worldPosition)
{
    float2 cell = floor(p);
    // Positive modulo is essential for underground/negative map coordinates.
    float2 phase = cell - floor(cell / 3) * 3;
    float phaseIndex = phase.y * 3 + phase.x;
    float motif = CopperEarthMotif(cell,richness);
    half4 hostMaterial = CopperTileMaterial(worldPosition);
    bool earth = hostMaterial.g > .5;
    // Same centre-cell material encoding as UniformStoneAppearance: clay,
    // neutral grey, warm rock, deep slate. Never infer host from ore depth.
    float rock = hostMaterial.b > .5 ? 0 : hostMaterial.a > .75 ? 3 : hostMaterial.a > .25 ? 2 : 1;
    uint width,height;
    if (earth) _CopperEarthCombined.GetDimensions(width,height);
    else {uint slices; _CopperRockCombined.GetDimensions(width,height,slices);}
    // GetDimensions reports the uploaded mip size, which changes with the
    // user's texture quality. Atlas positions must remain normalized instead.
    float padding=earth ? 16.0/450.0 : 8.0/224.0;
    float inner=1-2*padding;
    float2 dimensions=float2(width,height);
    float2 uv = (float2(phaseIndex,motif) + padding + frac(p)*inner) / float2(9,6);
    // Different atlas slots must not inflate the screen derivatives at cell
    // boundaries. Sixteen padding pixels support mip levels through four.
    float2 dx=ddx(p)*inner*dimensions/float2(9,6),dy=ddy(p)*inner*dimensions/float2(9,6);
    float mipFootprint=max(length(dx),length(dy));
    float limit=min(1,(width/9.0)*padding/max(mipFootprint,.001));
    if (earth) return SAMPLE_TEXTURE2D_GRAD(_CopperEarthCombined, sampler_CopperEarthCombined, uv,
        dx*limit/dimensions,dy*limit/dimensions);
    return SAMPLE_TEXTURE2D_ARRAY_GRAD(_CopperRockCombined, sampler_CopperEarthCombined, uv, rock,
        dx*limit/dimensions,dy*limit/dimensions);
}

half4 CopperEarthTileFragment(Varyings i, float2 p)
{
    half4 art = CopperEarthTileSample(p, VeinCell(floor(p)).y, i.positionWS.xy);
    // The approved subtle colour grade affects red-orange copper, not the
    // yellow-brown soil. Grade before lighting so coloured lights cannot select soil.
    half3 pigment = LinearToSRGB(art.rgb);
    half chroma = max(pigment.r - pigment.b, .001h);
    half copperMask = smoothstep(.62h, .72h, (pigment.r - pigment.g) / chroma)
        * smoothstep(.06h, .16h, pigment.r - pigment.b);
    half luminance = dot(pigment, half3(.2126h,.7152h,.0722h));
    pigment = saturate(luminance + (pigment-luminance) * (1 + .15h*copperMask));
    // L1 already grades the extracted copper during baking. Leave its exact
    // original dirt unchanged; rock families retain their existing grade.
    if (CopperTileMaterial(i.positionWS.xy).g <= .5) art.rgb = SRGBToLinear(pigment);
    half4 host = TestStone(i.positionWS.xy,frac(p));
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
