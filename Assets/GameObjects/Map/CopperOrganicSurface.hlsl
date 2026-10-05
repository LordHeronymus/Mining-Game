#ifndef MINING_COPPER_ORGANIC_SURFACE
#define MINING_COPPER_ORGANIC_SURFACE
TEXTURE2D(_CopperArt); SAMPLER(sampler_CopperArt);
TEXTURE2D(_CopperIds); SAMPLER(sampler_CopperIds);
TEXTURE2D(_CopperLayouts); SAMPLER(sampler_CopperLayouts);
TEXTURE2D(_CopperLedges); SAMPLER(sampler_CopperLedges);
float4 _CopperLayouts_TexelSize;
float4 CopperRecord(float row,float column)
{return SAMPLE_TEXTURE2D_LOD(_CopperLayouts,sampler_CopperLayouts,float2((column+.5)/128,(row+.5)*_CopperLayouts_TexelSize.y),0);}
half4 CopperSpecimen(float2 uv,float variant)
{
    float inside=step(0,min(min(uv.x,uv.y),min(1-uv.x,1-uv.y)));
    half4 value=SAMPLE_TEXTURE2D_LOD(_CopperArt,sampler_CopperArt,(float2(fmod(variant,3),floor(variant/3))+clamp(uv,.002,.998))/float2(3,2),0);
    value.a=smoothstep(.06,.94,value.a)*inside;return value;
}
float CopperFaultNoise(float2 p)
{
    float2 c=floor(p),f=frac(p);
    float a=VeinHash(c),b=VeinHash(c+float2(1,0)),d=VeinHash(c+float2(0,1)),e=VeinHash(c+1);
    return f.x+f.y<1?a+(b-a)*f.x+(d-a)*f.y:e+(d-e)*(1-f.x)+(b-e)*(1-f.y);
}
half4 CopperDirtFragment(Varyings i,float2 p,float2 data)
{
    float row=SAMPLE_TEXTURE2D_LOD(_CopperIds,sampler_CopperIds,(floor(p)-_VeinBounds.xy+.5)/_VeinBounds.zw,0).r-1;
    float4 header=CopperRecord(row,0);
    float distance=100,width=.04,along=0,signedDistance=0,branch=0;float2 tangent=float2(0,1);
    // Every cell samples the same complete deposit, independent of tile centres.
    [loop]for(int n=0;n<48;n++)
    {
        if(n>=header.x)break;
        float4 segment=CopperRecord(row,1+n*2),size=CopperRecord(row,2+n*2);
        float2 d=segment.zw-segment.xy;
        float t=saturate(dot(p-segment.xy,d)/max(.0001,dot(d,d)));
        float dist=length(p-lerp(segment.xy,segment.zw,t)),r=lerp(size.x,size.y,t);
        if(dist/max(.001,r)<distance/max(.001,width))
        {distance=dist;width=r;branch=size.z;tangent=normalize(d);signedDistance=dot(p-lerp(segment.xy,segment.zw,t),float2(-tangent.y,tangent.x));along=dot(p,tangent);}
    }
    half3 host=TerrainMaterialSample(i.positionWS.xy).rgb;
    float noise=VeinNoise(p*5.1+header.z),largeNoise=VeinNoise(p*1.18+header.z);
    width*=lerp(.30,1.50,CopperFaultNoise(p*5.7+header.z));
    float aa=max(fwidth(p.x),fwidth(p.y));
    float open=1-smoothstep(width-aa,width+aa,distance);
    float edge=1-smoothstep(width+.012,width+.11,distance);
    float buried=smoothstep(.70,.79,largeNoise)*.94*_EmbeddingStrength;
    buried=max(buried,branch*smoothstep(.39,.53,VeinNoise(p*1.85+17))*_EmbeddingStrength);
    open*=1-buried;edge*=1-buried;
    float2 normal=float2(-tangent.y,tangent.x);
    half3 rimRock=TerrainMaterialSample(i.positionWS.xy-normal*sign(signedDistance)*width*.28*_VeinGrid.xy).rgb;
    half3 colour=lerp(rimRock*lerp(.65,1.12,step(0,signedDistance)),host*.20,open);
    half4 veinMetal=CopperSpecimen(float2(frac(along*.71+noise*.16),saturate(.5+signedDistance/max(.04,width)*.24)),0);
    float trace=(1-smoothstep(width*.23,width*.81,distance))*open*smoothstep(.24,.60,noise);
    colour=lerp(colour,veinMetal.rgb*.75,trace*veinMetal.a);
    float alpha=edge;
    float plate=smoothstep(.055,.17,dot(host,half3(.25,.55,.20)));
    // Undisturbed host plates cover the unified body after its shape has been planned.
    [loop]for(int b=0;b<12;b++)
    {
        if(b>=header.y)break;
        float4 body=CopperRecord(row,97+b*2),detail=CopperRecord(row,98+b*2);
        float2 q=(p-body.xy)/max(.08,body.z*_OreScale);
        float cs=cos(detail.x),sn=sin(detail.x);q=float2(q.x*cs-q.y*sn,q.x*sn+q.y*cs);
        half4 metal=CopperSpecimen(q+.5,body.w);
        half4 expanded=CopperSpecimen(q*.93+.5,body.w);
        float foot=-q.y+((b&1)==0?q.x:-q.x)*.55+noise*.075;
        float cover=max(smoothstep(.65,.69,largeNoise),smoothstep(.26,.29,foot))*_EmbeddingStrength;
        float visible=metal.a*(1-cover),pocket=expanded.a*(1-cover*.85);
        colour=lerp(colour,host*.25,pocket);
        half3 copper=metal.rgb*half3(.91,.94,1.02);
        copper+=copper*smoothstep(.18,.65,max(copper.r,max(copper.g,copper.b)))*_ReflectionStrength*.12;
        colour=lerp(colour,copper,visible);
        float ledge=expanded.a*cover;colour=lerp(colour,host,ledge);
        alpha=max(alpha,max(pocket,ledge));
    }
    [loop]for(int coverIndex=0;coverIndex<7;coverIndex++)
    {
        if(coverIndex>=header.w)break;
        float4 slab=CopperRecord(row,121+coverIndex);
        float angle=((coverIndex&1)==0?-.83:.76)+sin(header.z+coverIndex*2.31)*.24;
        float2 local=(p-slab.xy)/(slab.z*_OreScale);
        float2 uv=float2(local.x*cos(angle)-local.y*sin(angle),local.x*sin(angle)+local.y*cos(angle))+.5;
        if((coverIndex&1)==1)uv.x=1-uv.x;
        float inside=step(0,min(min(uv.x,uv.y),min(1-uv.x,1-uv.y)));
        float2 tile=float2(fmod(slab.w,3),floor(slab.w/3));
        half4 rock=SAMPLE_TEXTURE2D_LOD(_CopperLedges,sampler_CopperLedges,(tile+clamp(uv,.002,.998))/float2(3,2),0);
        float opacity=smoothstep(.18,.88,rock.a)*inside*_EmbeddingStrength;
        half3 rockColour=lerp(rock.rgb,host*(.65+dot(rock.rgb,half3(.3,.5,.2))*4),.45);
        rockColour=lerp(host,rockColour,smoothstep(.03,.26,uv.x));
        colour=lerp(colour,rockColour,opacity);alpha=max(alpha,opacity);
    }
    SurfaceData2D surface=(SurfaceData2D)0;InputData2D input=(InputData2D)0;
    InitializeSurfaceData(colour*i.color.rgb,alpha*i.color.a,half4(1,1,1,1),surface);
    InitializeInputData(i.uv,i.lightingUV,input);
    half4 lit=CombinedShapeLightShared(surface,input);
    half peak=max(lit.r,max(lit.g,lit.b));half compressed=peak<=.7h?peak:.7h+.3h*(1-exp(-(peak-.7h)/.3h));
    lit.rgb*=compressed/max(.0001h,peak);return lit;
}
#endif
