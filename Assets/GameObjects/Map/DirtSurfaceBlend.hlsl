#ifndef MINING_DIRT_SURFACE_BLEND
#define MINING_DIRT_SURFACE_BLEND
TEXTURE2D(_StoneTex);
TEXTURE2D(_DirtTex);
TEXTURE2D(_DirtMask);
TEXTURE2D(_DeepStoneTex);
TEXTURE2D(_DeepMask);
TEXTURE2D_ARRAY(_DirtVariants);
TEXTURE2D_ARRAY(_StoneVariants);
TEXTURE2D_ARRAY(_DeepVariants);
SAMPLER(sampler_DirtMask);
SAMPLER(sampler_DeepMask);
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
    if(_DirtSurface.x<=0)return original;
    float2 cell=data.xy/_DirtSurface.x;
    float depth=1-cell.y;
    float dirtInfluence=0,deepInfluence=0;
    if(_DirtMaskBounds.z>0)
    {
        float start=_DirtSurface.y,end=start+_DirtSurface.z;
        dirtInfluence=smoothstep(start-2,start,depth)*(1-smoothstep(end,end+2,depth));
    }
    if(_DeepSurface.x>0 && _DeepMaskBounds.z>0)
    {
        float start=_DeepSurface.y,end=start+_DeepSurface.z;
        deepInfluence=smoothstep(start-2,start,depth)*(1-smoothstep(end,end+2,depth));
    }
    if(dirtInfluence<=0 && deepInfluence<=0)return original;
    float2 jitter=float2(DirtNoise(cell*.8+_DirtSurface.w),DirtNoise(cell*.65+_DirtSurface.w+53))-.5;
    half4 transition;
    if(dirtInfluence>0)
    {
        float start=_DirtSurface.y,end=start+_DirtSurface.z;
        float2 maskUV=(float2(cell.x-_DirtMaskBounds.x,depth)+jitter*.3)*_DirtMaskBounds.zw;
        float soilWeight=smoothstep(.08,.92,SAMPLE_TEXTURE2D(_DirtMask,sampler_DirtMask,maskUV).r);
        soilWeight=lerp(1,soilWeight,smoothstep(start,start+1,depth));
        soilWeight*=1-smoothstep(end-1,end,depth);
        int2 pixel=int2(floor(cell.x-_DirtMaskBounds.x),floor(depth));
        pixel=clamp(pixel,int2(0,0),int2(round(1/_DirtMaskBounds.zw))-1);
        half4 variant=LOAD_TEXTURE2D(_DirtMask,pixel);
        half4 soil=_VariantCounts.x>0
            ? SAMPLE_TEXTURE2D_ARRAY(_DirtVariants,sampler_linear_mirror,uv,round(variant.g*255))
            : SAMPLE_TEXTURE2D(_DirtTex,sampler_linear_mirror,cell);
        transition=_VariantCounts.y>0
            ? SAMPLE_TEXTURE2D_ARRAY(_StoneVariants,sampler_linear_mirror,uv,round(variant.b*255))
            : SAMPLE_TEXTURE2D(_StoneTex,sampler_linear_mirror,cell);
        original=lerp(original,lerp(transition,soil,soilWeight),dirtInfluence);
    }
    if(deepInfluence>0)
    {
        float start=_DeepSurface.y,end=start+_DeepSurface.z;
        float2 maskUV=(float2(cell.x-_DeepMaskBounds.x,depth-_DeepMaskBounds.y)+jitter*.3)*_DeepMaskBounds.zw;
        float upperWeight=smoothstep(.08,.92,SAMPLE_TEXTURE2D(_DeepMask,sampler_DeepMask,maskUV).r);
        upperWeight=lerp(1,upperWeight,smoothstep(start,start+1,depth));
        upperWeight*=1-smoothstep(end-1,end,depth);
        int2 pixel=int2(floor(cell.x-_DeepMaskBounds.x),floor(depth-_DeepMaskBounds.y));
        pixel=clamp(pixel,int2(0,0),int2(round(1/_DeepMaskBounds.zw))-1);
        half4 variant=LOAD_TEXTURE2D(_DeepMask,pixel);
        transition=_VariantCounts.y>0
            ? SAMPLE_TEXTURE2D_ARRAY(_StoneVariants,sampler_linear_mirror,uv,round(variant.g*255))
            : SAMPLE_TEXTURE2D(_StoneTex,sampler_linear_mirror,cell);
        half4 rock=_VariantCounts.z>0
            ? SAMPLE_TEXTURE2D_ARRAY(_DeepVariants,sampler_linear_mirror,uv,round(variant.b*255))
            : SAMPLE_TEXTURE2D(_DeepStoneTex,sampler_linear_mirror,cell);
        original=lerp(original,lerp(rock,transition,upperWeight),deepInfluence);
    }
    return original;
}
#endif
