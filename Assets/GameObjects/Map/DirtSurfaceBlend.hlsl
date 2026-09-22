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
#include "TerrainMaterialSample.hlsl"
#if defined(TERRAIN_FRAYED_EDGE)
TEXTURE2D(_FrayedTex);
SAMPLER(sampler_FrayedTex);
#endif
float TestSolid(float2 cell)
{
    int2 p = int2(cell - _TestBounds.xy);
    if(any(p < 0) || any(p >= int2(_TestBounds.zw))) return 0;
    return LOAD_TEXTURE2D(_TestOccupancy,p).r;
}
float TerrainEdgeHash(float2 p)
{
    return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);
}
float TerrainEdgeNoise(float coordinate,float seed)
{
    float lattice=floor(coordinate),blend=frac(coordinate);
    blend=blend*blend*(3-2*blend);
    return lerp(TerrainEdgeHash(float2(lattice,seed)),TerrainEdgeHash(float2(lattice+1,seed)),blend);
}
float TerrainEdgeCut(float coordinate,float edgeIndex)
{
    float broad=TerrainEdgeNoise(coordinate*.68,edgeIndex*13.1);
    float mid=TerrainEdgeNoise(coordinate*1.85,edgeIndex*29.7);
    float grain=TerrainEdgeNoise(coordinate*4.2,edgeIndex*41.9+7.3);
    return max(.012,.035+.14*broad+.12*(mid-.5)+.02*(grain-.5));
}
float3 TerrainRubblePosition(float3 positionOS,float4 edgeAnchor)
{
    if(_UniformStone.x<=0 || edgeAnchor.w<.5)return positionOS;
    float3 world=TransformObjectToWorld(positionOS);
    float2 cell=(world.xy-_UniformStone.zw)/_UniformStone.x;
    float along=abs(edgeAnchor.x)>.5?cell.y:cell.x;
    // Use precisely the terrain's contour, including its variation across a
    // pebble's width. The terrain is drawn afterwards over the buried half.
    world.xy-=edgeAnchor.xy*TerrainEdgeCut(along,edgeAnchor.z)*_UniformStone.x;
    return TransformWorldToObject(world);
}
#if defined(TERRAIN_FRAYED_TERRAIN)
TEXTURE2D_ARRAY(_TerrainEdgeMasks);
SAMPLER(sampler_TerrainEdgeMasks);
float TerrainRoundedMinimum(float a,float b,float radius)
{
    float h=max(radius-abs(a-b),0)/max(radius,.00001);
    return min(a,b)-h*h*radius*.25;
}
half4 MaskedTerrain(float2 position,float2 baseCell,float2 f,float4 open)
{
    half4 rock=TerrainMaterialSample(position);
    rock.a=1;
    uint width,height,slices;
    _TerrainEdgeMasks.GetDimensions(width,height,slices);
    // A missing/reimporting array must never make the entire terrain translucent.
    if(slices<192||width<128)return rock;
    uint cellHash=(uint)(int)baseCell.x*73856093u ^ (uint)(int)baseCell.y*19349663u;
    cellHash^=cellHash>>16;int variant=(int)(cellHash%12u);
    // Mask endpoints agree across cell boundaries, independently of variant.
    float2 maskUV=(f*127+.5)/128;
    float4 distance=float4(f.x,1-f.x,f.y,1-f.y);
    float4 sampled=float4(
        SAMPLE_TEXTURE2D_ARRAY(_TerrainEdgeMasks,sampler_TerrainEdgeMasks,maskUV,variant*16+1).r,
        SAMPLE_TEXTURE2D_ARRAY(_TerrainEdgeMasks,sampler_TerrainEdgeMasks,maskUV,variant*16+2).r,
        SAMPLE_TEXTURE2D_ARRAY(_TerrainEdgeMasks,sampler_TerrainEdgeMasks,maskUV,variant*16+4).r,
        SAMPLE_TEXTURE2D_ARRAY(_TerrainEdgeMasks,sampler_TerrainEdgeMasks,maskUV,variant*16+8).r);
    float depth=clamp(_TerrainEdgeTuning.x,0,2);
    if(depth<=0)return rock;
    float4 cut=min(.32,max(.005,.065+(distance-sampled-.065)*clamp(_TerrainEdgeTuning.y,0,2))*depth);
    float rounding=clamp(_TerrainEdgeTuning.z,0,2);
    float4 convex=float4(open.x*open.z,open.x*open.w,open.y*open.z,open.y*open.w);
    float4 cornerDistance=float4(length(f),length(float2(f.x,1-f.y)),
        length(float2(1-f.x,f.y)),length(1-f));
    float4 calm=convex*(1-smoothstep(.28,.65,cornerDistance))*saturate(rounding);
    float inset=min(.20,.12*depth);
    // Adjacent profiles no longer rise independently into a thin corner hook.
    cut=lerp(cut,inset,float4(max(calm.x,calm.y),max(calm.z,calm.w),
        max(calm.x,calm.z),max(calm.y,calm.w)));
    // The original surface row must meet the grass along a straight full-width
    // edge, including at shaft mouths. Only its side cuts taper out near the top.
    if(abs(baseCell.y)<.5)cut.xy*=1-smoothstep(.70,1,f.y);
    float4 margins=lerp(1,distance-cut,open);
    float radius=.24*clamp(_TerrainEdgeTuning.z,0,2)*depth;
    float edge=TerrainRoundedMinimum(TerrainRoundedMinimum(margins.x,margins.y,radius),
        TerrainRoundedMinimum(margins.z,margins.w,radius),radius);
    float cornerRadius=min(.30,.20*depth*rounding);
    float centre=inset+cornerRadius;
    float4 blunt=cornerRadius-float4(length(max(centre-f,0)),
        length(max(centre-float2(f.x,1-f.y),0)),length(max(centre-float2(1-f.x,f.y),0)),
        length(max(centre-(1-f),0)));
    // Restrict the circular cap to its corner; straight walls and the original
    // surface retain their existing silhouette and matching cell endpoints.
    float4 capWeight=convex*(1-smoothstep(.48,.70,cornerDistance))*saturate(rounding);
    blunt=lerp(1,blunt,capWeight);
    edge=min(edge,min(min(blunt.x,blunt.y),min(blunt.z,blunt.w)));
    // Diagonal openings round the inside corners without opening seams between
    // solid neighbours. Their radius matches the side masks' endpoint inset.
    float4 diagonal=1-float4(TestSolid(baseCell+float2(-1,-1)),TestSolid(baseCell+float2(-1,1)),
        TestSolid(baseCell+float2(1,-1)),TestSolid(baseCell+float2(1,1)));
    float4 closed=1-open;
    diagonal*=float4(closed.x*closed.z,closed.x*closed.w,closed.y*closed.z,closed.y*closed.w);
    if(abs(baseCell.y)<.5)diagonal.yw=0;
    float4 notch=float4(length(f),length(float2(f.x,1-f.y)),length(float2(1-f.x,f.y)),length(1-f))-.065*depth;
    notch=lerp(1,notch,diagonal);
    edge=min(edge,min(min(notch.x,notch.y),min(notch.z,notch.w)));
    float aa=max(fwidth(edge)*.65,.001);
    rock.a=smoothstep(-aa,aa,edge);
    float rim=1-smoothstep(.002,.065,edge);
    rock.rgb*=1-rim*.17;
    rock.rgb+=rim*(open.w*.045+open.x*.02)*half3(1,.74,.48);
    return rock;
}
#endif
half4 TestStone(float2 position,float2 uv)
{
    float2 cell = (position - _UniformStone.zw) / _UniformStone.x;
    // Keep the fragment assigned to its sprite tile at shared grid edges.
    float2 baseCell = floor(cell - uv + .5), f = saturate(cell-baseCell);
    float4 open = 1-float4(TestSolid(baseCell+float2(-1,0)),TestSolid(baseCell+float2(1,0)),
        TestSolid(baseCell+float2(0,-1)),TestSolid(baseCell+float2(0,1)));
    if(abs(baseCell.y)<.5)open.w=0;
#if defined(TERRAIN_FRAYED_TERRAIN)
    if(_UseTerrainMasks>.5)return MaskedTerrain(position,baseCell,f,open);
#endif
    float4 distance = float4(f.x,1-f.x,f.y,1-f.y);
    float leftCut=TerrainEdgeCut(cell.y,baseCell.x);
    float rightCut=TerrainEdgeCut(cell.y,baseCell.x+1);
    float bottomCut=TerrainEdgeCut(cell.x,baseCell.y);
    float topCut=TerrainEdgeCut(cell.x,baseCell.y+1);
#if defined(TERRAIN_FRAYED_TERRAIN)
    leftCut=max(leftCut,_FrayedInset);rightCut=max(rightCut,_FrayedInset);
    bottomCut=max(bottomCut,_FrayedInset);topCut=max(topCut,_FrayedInset);
#endif
    float4 margin=distance-float4(leftCut,rightCut,bottomCut,topCut);
    float edge=min(min(lerp(1,margin.x,open.x),lerp(1,margin.y,open.y)),
        min(lerp(1,margin.z,open.z),lerp(1,margin.w,open.w)));

    // Remove a little more material where two exposed sides meet. This rounds the
    // grid corner without changing the square collider behind the artwork.
    float corner=.205+.035*TerrainEdgeNoise((baseCell.x+baseCell.y)*2.1,17.3);
    float4 cornerOpen=float4(open.x*open.z,open.x*open.w,open.y*open.z,open.y*open.w);
    float4 cornerEdge=corner-float4(length(max(corner-float2(f.x,f.y),0)),length(max(corner-float2(f.x,1-f.y),0)),
        length(max(corner-float2(1-f.x,f.y),0)),length(max(corner-float2(1-f.x,1-f.y),0)));
    cornerEdge=lerp(1,cornerEdge,cornerOpen);
    edge=min(edge,min(min(cornerEdge.x,cornerEdge.y),min(cornerEdge.z,cornerEdge.w)));
    half4 stone = TerrainMaterialSample(position);
    stone.a=smoothstep(-.006,.014,edge);
    float bevel=1-smoothstep(.006,.16,edge);
    float fracture=TerrainEdgeNoise((cell.x+cell.y)*7.1,baseCell.x*5.7+baseCell.y*11.3);
    fracture=smoothstep(.77,.96,fracture)*(1-smoothstep(.035,.17,edge));
    float highlight=open.w*(1-smoothstep(.005,.12,margin.w))+
        open.x*(1-smoothstep(.005,.12,margin.x))*.35;
    stone.rgb*=1-bevel*.34-fracture*.13;
    stone.rgb+=highlight*half3(.14,.10,.058);
    return stone;
}
float DirtHash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
float DirtNoise(float2 p)
{
    float2 cell=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(DirtHash(cell),DirtHash(cell+float2(1,0)),f.x),
                lerp(DirtHash(cell+float2(0,1)),DirtHash(cell+1),f.x),f.y);
}
half4 DirtSurfaceBlend(half4 original,float2 uv,float3 data)
{
    if(_UniformStone.x>0)
    {
        if(abs(data.z-.25)<.01)
        {
            half4 rock=TerrainMaterialSample(data.xy);
#if defined(TERRAIN_FRAYED_EDGE)
            half4 fringe=SAMPLE_TEXTURE2D(_FrayedTex,sampler_FrayedTex,uv);
            fringe.rgb=lerp(rock.rgb,fringe.rgb,smoothstep(.03,.32,uv.y));
            fringe.a*=smoothstep(.015,.26,uv.y);
            return fringe;
#else
            rock.a=1;return rock;
#endif
        }
        return TestStone(data.xy,uv);
    }
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
    half4 transition=original;
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



