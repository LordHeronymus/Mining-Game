#ifndef MINING_DIRT_SURFACE_BLEND
#define MINING_DIRT_SURFACE_BLEND
TEXTURE2D(_StoneTex);
TEXTURE2D(_DirtTex);
TEXTURE2D(_DirtMask);
SAMPLER(sampler_DirtMask);
SAMPLER(sampler_linear_mirror);
float DirtHash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
float DirtNoise(float2 p)
{
    float2 cell=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(DirtHash(cell),DirtHash(cell+float2(1,0)),f.x),
                lerp(DirtHash(cell+float2(0,1)),DirtHash(cell+1),f.x),f.y);
}
half4 DirtSurfaceBlend(half4 original,float2 uv,float3 data)
{
    // Renderer-specific activation keeps this terrain effect off ore renderers.
    if(_DirtSurface.x<=0 || _DirtMaskBounds.z<=0)return original;
    float2 cell=data.xy/_DirtSurface.x;
    float depth=1-cell.y;
    float start=_DirtSurface.y,end=start+_DirtSurface.z;
    float influence=smoothstep(start-2,start,depth)*(1-smoothstep(end,end+2,depth));
    if(influence<=0)return original;
    float2 jitter=float2(DirtNoise(cell*.8+_DirtSurface.w),DirtNoise(cell*.65+_DirtSurface.w+53))-.5;
    float2 maskUV=(float2(cell.x-_DirtMaskBounds.x,depth)+jitter*.3)*_DirtMaskBounds.zw;
    float soilWeight=smoothstep(.08,.92,SAMPLE_TEXTURE2D(_DirtMask,sampler_DirtMask,maskUV).r);
    soilWeight=lerp(1,soilWeight,smoothstep(start,start+1,depth));
    soilWeight*=1-smoothstep(end-1,end,depth);
    // Both block types sample the same continuous field and textures. No tile-type branch.
    half4 soil=SAMPLE_TEXTURE2D(_DirtTex,sampler_linear_mirror,cell);
    half4 rock=SAMPLE_TEXTURE2D(_StoneTex,sampler_linear_mirror,cell);
    return lerp(original,lerp(rock,soil,soilWeight),influence);
}
#endif