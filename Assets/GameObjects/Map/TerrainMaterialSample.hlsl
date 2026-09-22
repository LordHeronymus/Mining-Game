#ifndef MINING_TERRAIN_MATERIAL_SAMPLE
#define MINING_TERRAIN_MATERIAL_SAMPLE
TEXTURE2D(_TestStoneTex);
SAMPLER(sampler_TestStoneTex);
TEXTURE2D(_TestOccupancy);
SAMPLER(sampler_TestOccupancy);
TEXTURE2D(_SurfaceDirtTex);
SAMPLER(sampler_SurfaceDirtTex);
TEXTURE2D(_LayerOneTex);
SAMPLER(sampler_LayerOneTex);
TEXTURE2D(_LayerThreeTex);
SAMPLER(sampler_LayerThreeTex);

// Shared world-space material lookup keeps ore lips continuous with their host rock.
half4 TerrainMaterialSample(float2 position)
{
    float2 cell = (position - _UniformStone.zw) / max(_UniformStone.x,.0001);
    float2 uv = cell / _UniformStone.y;
    half4 stone = SAMPLE_TEXTURE2D(_TestStoneTex,sampler_TestStoneTex,uv);
    float2 maskUV = (cell - _TestBounds.xy) / _TestBounds.zw;
    float4 materials = SAMPLE_TEXTURE2D(_TestOccupancy,sampler_TestOccupancy,maskUV);
    stone = lerp(stone,SAMPLE_TEXTURE2D(_LayerThreeTex,sampler_LayerThreeTex,uv),smoothstep(.1,.9,materials.a));
    stone = lerp(stone,SAMPLE_TEXTURE2D(_LayerOneTex,sampler_LayerOneTex,uv),smoothstep(.1,.9,materials.b/max(.001,1-materials.g)));
    return lerp(stone,SAMPLE_TEXTURE2D(_SurfaceDirtTex,sampler_SurfaceDirtTex,uv),smoothstep(.1,.9,materials.g));
}
#endif
