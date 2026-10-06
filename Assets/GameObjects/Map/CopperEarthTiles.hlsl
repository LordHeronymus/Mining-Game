#ifndef MINING_COPPER_EARTH_TILES
#define MINING_COPPER_EARTH_TILES

TEXTURE2D(_CopperEarthTiles);
SAMPLER(sampler_CopperEarthTiles);
TEXTURE2D(_CopperEarthTileLayout);
SAMPLER(sampler_CopperEarthTileLayout);

// Sample the material at the actual cell centre. Filtered transition pixels at
// its edge must not switch a single ore cell between two rendering styles.
bool CopperEarthTileHost(float2 worldPosition)
{
    if (_CopperEarthTilesEnabled < .5 || _UniformStone.x <= 0) return false;
    float2 cell = floor((worldPosition - _UniformStone.zw) / _UniformStone.x) + .5;
    float2 maskUV = (cell - _TestBounds.xy) / _TestBounds.zw;
    half4 material = SAMPLE_TEXTURE2D_LOD(_TestOccupancy, sampler_TestOccupancy, maskUV, 0);
    return material.r > .5 && material.g > .5;
}

half4 CopperEarthTileSample(float2 p, float richness)
{
    float2 cell = floor(p);
    float variant = floor(VeinHash(cell + 91.37) * 4);
    // Atlas rows, counted from the top: small, medium, rich A, rich B.
    float row = richness < 1.5 ? 0 : richness < 2.5 ? 1 :
        2 + step(.5, VeinHash(cell + 42.19));
    float4 layout = SAMPLE_TEXTURE2D_LOD(_CopperEarthTileLayout, sampler_CopperEarthTileLayout,
        (float2(variant,3-row)+.5)*.25,0);
    // The importer measures a wholly opaque copper disc in each painted sprite.
    // Centre that disc on the cell and limit jitter so the 30%-diameter core
    // remains copper at every size; no synthetic circle is painted over the art.
    float minimumScale = .156 / max(layout.z,.001);
    float scale = clamp(_OreScale * lerp(.93,1.0,VeinHash(cell+18.47)),
        minimumScale,layout.w);
    float jitter = min(.045,max(0,layout.z*scale-.156));
    float2 shift = (float2(VeinHash(cell + 7.41), VeinHash(cell + 63.18)) - .5) * (jitter*1.414);
    float2 local = (frac(p) - .5 - shift) / scale + layout.xy;
    float inside = step(0, min(min(local.x, local.y), min(1-local.x, 1-local.y)));
    // Inset by half a texel and keep every sample in its own transparent slot.
    float2 uv = (float2(variant, 3-row) + clamp(local, .002, .998)) * .25;
    half4 art = SAMPLE_TEXTURE2D(_CopperEarthTiles, sampler_CopperEarthTiles, uv);
    art.a = smoothstep(.12h, .80h, art.a) * inside;
    return art;
}

half4 CopperEarthTileFragment(Varyings i, float2 p)
{
    half4 art = CopperEarthTileSample(p, VeinCell(floor(p)).y);
    clip(art.a - .003h);
    SurfaceData2D surfaceData = (SurfaceData2D)0;
    InputData2D inputData = (InputData2D)0;
    // Reflections, contact shadow and the small soil overlaps are painted into
    // each tile. No extra fissure web, carpet, glow or procedural earth cover.
    InitializeSurfaceData(art.rgb, art.a, half4(1,1,1,1), surfaceData);
    InitializeInputData(i.uv, i.lightingUV, inputData);
    return CombinedShapeLightShared(surfaceData, inputData);
}
#endif
