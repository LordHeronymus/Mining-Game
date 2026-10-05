#ifndef COPPER_CARPET_SURFACE_INCLUDED
#define COPPER_CARPET_SURFACE_INCLUDED

TEXTURE2D(_CopperCarpet);
SAMPLER(sampler_CopperCarpet);
TEXTURE2D(_CopperOutlineField);
SAMPLER(sampler_CopperOutlineField);
// Smoothly joined bodies and curved branches share one world-space contour.
float CopperSmoothUnion(float a,float b,float width)
{
    float h=saturate(.5+.5*(a-b)/width);
    return lerp(b,a,h)+width*h*(1-h);
}

float CopperSegmentDistance(float2 p,float2 a,float2 b)
{
    float2 d=b-a;
    return length(p-a-d*saturate(dot(p-a,d)/max(dot(d,d),.00001)));
}

float CopperCoverOpening(float2 p)
{
    float2 cell=floor(p),local=p-cell;
    if(abs(VeinCell(cell).x-3)>.1)return -.5;
    bool occupied[25];
    float edge=2;
    [unroll]for(int y=-2;y<=2;y++)
    [unroll]for(int x=-2;x<=2;x++)
    {
        bool copper=abs(VeinCell(cell+float2(x,y)).x-3)<.1;
        occupied[(y+2)*5+x+2]=copper;
        if(!copper)edge=min(edge,length(max(abs(local-(float2(x,y)+.5))-.5,0)));
    }
    float spine=length(local-.5);
    int degree=0;float2 first=0,second=0;
    [unroll]for(int arm=0;arm<4;arm++)
    {
        int index=arm==0?13:arm==1?17:arm==2?11:7;
        if(occupied[index])
        {
            float2 direction=arm==0?float2(1,0):arm==1?float2(0,1):arm==2?float2(-1,0):float2(0,-1);
            if(degree==0)first=direction;else second=direction;
            degree++;
            spine=min(spine,CopperSegmentDistance(local,.5,.5+direction*.5));
        }
    }
    // Replace an L-shaped centreline with a quadratic bend tangent to both exits.
    if(degree==2 && dot(first,second)==0)
    {
        float2 a=.5+first*.5,c=.5+second*.5,previous=a;
        spine=2;
        [unroll]for(int n=1;n<=8;n++)
        {
            float t=n/8.0;
            float2 next=(1-t)*(1-t)*a+2*t*(1-t)*.5+t*t*c;
            spine=min(spine,CopperSegmentDistance(local,previous,next));
            previous=next;
        }
    }
    float opening=SAMPLE_TEXTURE2D_LOD(_CopperOutlineField,sampler_CopperOutlineField,
        (p-_VeinBounds.xy)/_VeinBounds.zw,0).r;
    // Smooth the combined deposit first, then vary its border in world space.
    float2 flow=float2(p.x*.87-p.y*.49,p.x*.49+p.y*.87);
    float broad=VeinNoise(flow*.74+float2(19,63));
    float detail=VeinNoise(flow*2.4+float2(71,-11))-.5;
    float wave=.5+.5*sin(flow.x*2.65+sin(flow.y*1.7+_VeinSeed*.11)*1.4+_VeinSeed*.17);
    float inset=.025+.34*wave+.02*(broad-.5)+detail*.01;
    opening-=inset*(1-smoothstep(.55,1.1,edge));
    float protectedShape=max(.155-length(local-.5),.12-spine);
    opening=CopperSmoothUnion(opening,protectedShape,.20);
    return min(opening,edge-.06);
}
// L1 is surface Dirt, L2 is brown clay (layerOneTile). The grey stone
// and deeper layers deliberately retain their existing ore appearance.
bool CopperCarpetHost(float2 world)
{
    float2 cell=floor((world-_UniformStone.zw)/max(.0001,_UniformStone.x));
    half4 host=SAMPLE_TEXTURE2D_LOD(_TestOccupancy,sampler_TestOccupancy,
        (cell+.5-_TestBounds.xy)/_TestBounds.zw,0);
    return host.r>.5 && max(host.g,host.b)>.65;
}

half3 CopperCarpetColour(float2 p)
{
    // Continuous world coordinates, never per-cell UVs. Overlapping patches
    // sample ONLY the source interior, so its opposite edges need not match.
    float2 q=float2(p.x*.93-p.y*.37,p.x*.37+p.y*.93)/(.85*max(.5,_OreScale));
    q+=float2(VeinNoise(p*.23+41),VeinNoise(p*.23-17))*.65;
    float2 base=floor(q),f=frac(q);
    float2 blend=f*f*(3-2*f);
    float2 dx=ddx(q),dy=ddy(q);
    half3 sum=0;float total=0;
    [unroll]for(int k=0;k<4;k++)
    {
        float2 corner=float2(k&1,k>>1),key=base+corner;
        float2 shift=float2(VeinHash(key+31.3),VeinHash(key-9.7))-.5;
        float angle=(VeinHash(key+73.1)-.5)*1.4;
        float cs=cos(angle),sn=sin(angle);
        float2x2 rotation=float2x2(cs,-sn,sn,cs);
        float scale=lerp(.105,.14,VeinHash(key-48.2));
        float2 uv=.5+mul(rotation,q-key)*scale+shift*.54;
        half3 copper=SAMPLE_TEXTURE2D_GRAD(_CopperCarpet,sampler_CopperCarpet,uv,
            mul(rotation,dx)*scale,mul(rotation,dy)*scale).rgb;
        float2 weights=lerp(1-blend,blend,corner);
        // Narrow, irregular transitions favour raised copper faces over dark
        // crevices. Zero boundary weights guarantee continuous patch joins.
        float weight=pow(weights.x*weights.y,4)*exp2(dot(copper,half3(.3,.5,.2))*5);
        sum+=copper*weight;total+=weight;
    }
    return sum/max(total,.000001);
}

half4 CopperCarpetFragment(Varyings i,float2 p)
{
    half3 copper=CopperCarpetColour(p);
    // Compress only copper highlights; the host must retain its exact material colour.
    copper*=.94/(1+.24*max(copper.r,max(copper.g,copper.b)));
    half3 colour=copper;
    if(_CopperCoverEnabled>.5)
    {
        float opening=CopperCoverOpening(p);
        float aa=max(.003,fwidth(opening)*.65);
        float exposed=smoothstep(-aa,aa,opening);
        // Radius .15 = diameter 30% of the cell width. The entire disk is fully
        // exposed; antialiasing transitions only OUTSIDE this protected disk.
        float coreDistance=length(frac(p)-.5)-.15;
        exposed=max(exposed,1-smoothstep(0,aa*2,coreDistance));
        half3 earth=TerrainMaterialSample(i.positionWS.xy).rgb;
        // A narrow contact shadow falls on the recessed copper, not a black
        // outline around every terrain tile. The outside remains exact host art.
        float underhang=CopperCoverOpening(p+float2(-.13,.18)*( .85+VeinNoise(p*2.1)*.45));
        float contact=(1-smoothstep(.005,.09,opening))*.52;
        float castShadow=(1-smoothstep(-.025,.095,underhang))*.68;
        copper*=1-max(contact,castShadow);
        float ledgeWidth=.08+VeinNoise(p*2.3+19)*.045;
        float rim=(1-smoothstep(.008,ledgeWidth,-opening))*(1-exposed);
        float2 gradient=float2(ddx(opening),ddy(opening));
        float2 lightSlope=float2(dot(ddx(p),float2(-.6,.8)),dot(ddy(p),float2(-.6,.8)));
        float facing=dot(gradient,lightSlope)*rsqrt(max(1e-16,dot(gradient,gradient)*dot(lightSlope,lightSlope)));
        // A short bevel on the opaque host, not a glow or a black uniform outline.
        earth*=1+rim*lerp(-.34,.28,saturate(facing*.5+.5));
        colour=lerp(earth,copper,exposed);
    }
    SurfaceData2D surface=(SurfaceData2D)0;InputData2D input=(InputData2D)0;
    InitializeSurfaceData(colour,1,half4(1,1,1,1),surface);
    InitializeInputData(i.uv,i.lightingUV,input);
    return CombinedShapeLightShared(surface,input);
}
#endif
