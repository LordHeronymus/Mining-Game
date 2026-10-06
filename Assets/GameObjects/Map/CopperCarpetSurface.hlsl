#ifndef COPPER_CARPET_SURFACE_INCLUDED
#define COPPER_CARPET_SURFACE_INCLUDED

TEXTURE2D(_CopperCarpet);
SAMPLER(sampler_CopperCarpet);
TEXTURE2D(_CopperOutlineField);
SAMPLER(sampler_CopperOutlineField);
TEXTURE2D(_CopperEarthWall);
SAMPLER(sampler_CopperEarthWall);
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

float CopperCoverOpening(float2 p,out float protectedOpening)
{
    float2 cell=floor(p),local=p-cell;
    protectedOpening=.155-length(local-.5);
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
    protectedOpening=protectedShape;
    opening=CopperSmoothUnion(opening,protectedShape,.20);
    return min(opening,edge-.06);
}
float CopperCoverOpening(float2 p)
{
    float protectedOpening;
    return CopperCoverOpening(p,protectedOpening);
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

float CopperProtectedOpening(float2 p)
{
    float2 cell=floor(p),local=p-cell;
    float spine=length(local-.5);
    int degree=0;float2 first=0,second=0;
    [unroll]for(int arm=0;arm<4;arm++)
    {
        float2 direction=arm==0?float2(1,0):arm==1?float2(0,1):arm==2?float2(-1,0):float2(0,-1);
        if(abs(VeinCell(cell+direction).x-3)<.1)
        {
            if(degree==0)first=direction;else second=direction;
            degree++;
            spine=min(spine,CopperSegmentDistance(local,.5,.5+direction*.5));
        }
    }
    if(degree==2 && dot(first,second)==0)
    {
        float2 a=.5+first*.5,c=.5+second*.5,previous=a;
        spine=2;
        [unroll]for(int n=1;n<=8;n++)
        {
            float t=n/8.0;
            float2 next=(1-t)*(1-t)*a+2*t*(1-t)*.5+t*t*c;
            spine=min(spine,CopperSegmentDistance(local,previous,next));previous=next;
        }
    }
    return max(.155-length(local-.5),.12-spine);
}

float CopperEarthClearance(float2 lip,float2 inward,float desired)
{
    // Conservative distance marching finds the FIRST protected core/branch
    // along a whole wall column. Do not subtract protected disks from its foot.
    float travel=0;
    [unroll]for(int n=0;n<5;n++)
    {
        float freeDistance=max(0,-CopperProtectedOpening(lip+inward*travel)-.055);
        travel=min(desired,travel+freeDistance*.90);
    }
    return travel;
}

struct CopperEarthFace
{
    float2 key;
    float2 offset;
    float seam;
};

// Irregular world-fixed stones. Their spacing is unrelated to the ore-cell grid.
CopperEarthFace CopperEarthFacet(float2 lip)
{
    float2 q=float2(lip.x*.94-lip.y*.34,lip.x*.34+lip.y*.94)*1.85;
    float2 base=floor(q);
    float first=20,second=20;CopperEarthFace face;
    face.key=base;face.offset=0;face.seam=0;
    [unroll]for(int y=-1;y<=1;y++)
    [unroll]for(int x=-1;x<=1;x++)
    {
        float2 key=base+float2(x,y);
        float2 seed=key+.5+(float2(VeinHash(key+61),VeinHash(key-23))-.5)*.72;
        float2 delta=q-seed;float d=dot(delta,delta);
        if(d<first){second=first;first=d;face.key=key;face.offset=delta;}
        else second=min(second,d);
    }
    face.seam=sqrt(second)-sqrt(first);
    return face;
}

// The higher mouth needs a continuous distance in surrounding earth as well.
// The authoritative copper-floor opening deliberately remains cell-bounded.
float CopperEarthMouthField(float2 p)
{
    float field=SAMPLE_TEXTURE2D_LOD(_CopperOutlineField,sampler_CopperOutlineField,
        (p-_VeinBounds.xy)/_VeinBounds.zw,0).r;
    float2 flow=float2(p.x*.87-p.y*.49,p.x*.49+p.y*.87);
    float wave=.5+.5*sin(flow.x*2.65+sin(flow.y*1.7+_VeinSeed*.11)*1.4+_VeinSeed*.17);
    float broad=VeinNoise(flow*.74+float2(19,63));
    return field-(.025+.12*wave+.01*(broad-.5))*(1-smoothstep(.55,1.1,abs(field)));
}

void CopperEarthMouthFrame(float2 p,out float signedDistance,out float2 inward)
{
    const float stepSize=.04;
    float field=CopperEarthMouthField(p);
    float2 gradient=float2(
        CopperEarthMouthField(p+float2(stepSize,0))-CopperEarthMouthField(p-float2(stepSize,0)),
        CopperEarthMouthField(p+float2(0,stepSize))-CopperEarthMouthField(p-float2(0,stepSize)))/(2*stepSize);
    float slope=length(gradient);
    inward=gradient/max(.0001,slope);
    signedDistance=field/max(.45,slope);
}

half3 CopperEarthMaterial(float2 p)
{
    // Continuous world-space patches; normals and facet IDs never distort art UVs.
    float2 q=float2(p.x*.62+p.y*.09,p.y*.48);
    float2 base=floor(q),f=frac(q),blend=f*f*(3-2*f);
    float2 dx=ddx(q)*.20,dy=ddy(q)*.20;
    half3 sum=0;float total=0;
    [unroll]for(int k=0;k<4;k++)
    {
        float2 corner=float2(k&1,k>>1),key=base+corner;
        float2 shift=float2(VeinHash(key+211),VeinHash(key-31))-.5;
        float2 uv=.5+(q-key)*.20+shift*.50;
        half3 art=SAMPLE_TEXTURE2D_GRAD(_CopperEarthWall,sampler_CopperEarthWall,uv,dx,dy).rgb;
        float2 weights=lerp(1-blend,blend,corner);
        float weight=pow(weights.x*weights.y,3);
        sum+=art*weight;total+=weight;
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
        bool copperCell=abs(VeinCell(floor(p)).x-3)<.1;
        float protectedOpening;
        float opening=CopperCoverOpening(p,protectedOpening);
        float aa=max(.003,fwidth(opening)*.65);
        float exposed=smoothstep(-aa,aa,opening);
        // Radius .15 = diameter 30% of the cell width. The entire disk is fully
        // exposed; antialiasing transitions only OUTSIDE this protected disk.
        float coreDistance=length(frac(p)-.5)-.15;
        exposed=copperCell?max(exposed,1-smoothstep(0,aa*2,coreDistance)):0;
        half3 earth=TerrainMaterialSample(i.positionWS.xy).rgb;
        if(_CopperRecessDepth<.5)
        {
            // Retained only for direct before/after rendering of the same deposit.
            float underhang=CopperCoverOpening(p+float2(-.13,.18)*(.85+VeinNoise(p*2.1)*.45));
            float contact=(1-smoothstep(.005,.09,opening))*.52;
            float castShadow=(1-smoothstep(-.025,.095,underhang))*.68;
            copper*=1-max(contact,castShadow);
            float ledgeWidth=.08+VeinNoise(p*2.3+19)*.045;
            float rim=(1-smoothstep(.008,ledgeWidth,-opening))*(1-exposed);
            float2 gradient=float2(ddx(opening),ddy(opening));
            float2 lightSlope=float2(dot(ddx(p),float2(-.6,.8)),dot(ddy(p),float2(-.6,.8)));
            float facing=dot(gradient,lightSlope)*rsqrt(max(1e-16,dot(gradient,gradient)*dot(lightSlope,lightSlope)));
            earth*=1+rim*lerp(-.34,.28,saturate(facing*.5+.5));
            colour=lerp(earth,copper,exposed);
        }
        else
        {
            // Convert screen derivatives back to a world-cell normal. Distance
            // normalization keeps the ledge equally thick on blended contours.
            float2 px=ddx(p),py=ddy(p);
            float ox=ddx(opening),oy=ddy(opening);
            float determinant=px.x*py.y-px.y*py.x;
            float2 gradient=float2(ox*py.y-oy*px.y,oy*px.x-ox*py.x)/
                (abs(determinant)>.00000001?determinant:.00000001);
            float slope=length(gradient);
            float2 inward=gradient/max(.0001,slope),outward=-inward;
            float distance=opening/max(.45,slope);
            float edgeAA=max(.003,fwidth(distance)*.7);
            float2 light=float2(-.6,.8);
            float variation=VeinNoise(p*2.3+19);
            // The upper lip exposes more of its descending face in this view.
            // It uses the host material at the lip, pulled down into the opening.
            if(_CopperRecessDepth<1.5)
            {
            float wallWidth=(.055+.10*saturate(outward.y))*(.8+variation*.4);
            float wallBand=(1-smoothstep(wallWidth*.72,wallWidth+edgeAA,distance))*exposed;
            float coreGuard=1-smoothstep(0,.045,coreDistance);
            coreGuard=max(coreGuard,smoothstep(-.015,.025,protectedOpening));
            wallBand*=1-coreGuard;
            float2 lipWorld=i.positionWS.xy-inward*(max(0,distance)+.035)*_VeinGrid.xy;
            half3 wallEarth=lerp(TerrainMaterialSample(lipWorld).rgb,earth,.45);
            float wallDepth=saturate(distance/max(.01,wallWidth));
            float strata=.87+.13*VeinNoise(float2(p.x*15,p.y*3+wallDepth*7)+37);
            float wallLight=lerp(.48,.78,saturate(dot(inward,light)*.5+.5));
            wallEarth*=wallLight*strata;
            // A compact bevel belongs to the higher surface; the darker wall is
            // a separate material band below it, followed by the recessed metal.
            float topLip=(1-smoothstep(.004,.055,-distance))*(1-exposed);
            earth*=1+topLip*lerp(-.18,.28,saturate(dot(outward,light)*.5+.5));
            float castLength=.19+variation*.055;
            float occluder=CopperCoverOpening(p+light*castLength);
            float contact=(1-smoothstep(.015,.07,distance))*.36;
            float castShadow=(1-smoothstep(-.04,.065,occluder))*.73;
            copper*=1-max(contact,castShadow);
            colour=lerp(earth,copper,exposed);
            colour=lerp(colour,wallEarth,wallBand*.96);
            }
            else
            {
                float2 lip=p-inward*max(0,distance);
                // A higher mouth and lower copper floor provide room for real
                // earth thickness without taking it from protected ore cores.
                float height=clamp(_CopperWallHeight,.08,.71);
                float projectionHeight=min(.75,height+.04);
                float projectedDistance;float2 faceInward;
                CopperEarthMouthFrame(p-float2(0,projectionHeight),projectedDistance,faceInward);
                float2 upperLip=p-faceInward*projectedDistance;
                CopperEarthFace face=CopperEarthFacet(upperLip);
                float stone=VeinHash(face.key+83);
                // Short uneven breaks follow the fixed user reference. Avoid
                // tall regular terraces; only a small part follows stone facets.
                float raisedHeight=height*(.64+.28*VeinNoise(upperLip*2.1+83)+.08*stone);
                float2 lowered=p-float2(0,raisedHeight);
                float raisedDistance;float2 actualInward;
                CopperEarthMouthFrame(lowered,raisedDistance,actualInward);
                float lifted=CopperCarpetHost(i.positionWS.xy-float2(0,raisedHeight)*_VeinGrid.xy)
                    ? CopperEarthMouthField(lowered):-.5;
                float liftedAA=clamp(fwidth(lifted)*.65,.003,.025);
                float liftedExposure=smoothstep(-liftedAA,liftedAA,lifted);
                // Outside copper, emit only the actual face and its narrow lip.
                // This avoids rectangular host-cell backings around the wall.
                if(!copperCell)clip(lifted+liftedAA+.025);
                float upper=saturate(outward.y*.85+.15);
                float terrace=floor(stone*3)/2;
                // A few broad descending faces, with stepped feet and small chips.
                float depth=(.08+.18*upper)*lerp(.84,1.12,terrace);
                depth+=dot(face.offset,float2(.012,-.020));
                depth-=.022*(1-smoothstep(.015,.10,face.seam));
                // Pull the wall foot back before it reaches a protected core or
                // connecting spine; never cut isolated copper disks out of a wall.
                float wallWidth=CopperEarthClearance(lip,inward,max(.01,depth));
                float foot=distance-wallWidth;
                float footAA=max(.003,min(.035,fwidth(foot)*.65));
                float wallBand=(1-smoothstep(-footAA,footAA,foot))*exposed;
                // Final safety against the AA tail; the column already retreats
                // before this region, so this does not create interior windows.
                wallBand*=1-smoothstep(-.055,-.015,protectedOpening);
                float riseFacing=smoothstep(-.05,.20,-actualInward.y);
                wallBand=max(wallBand,liftedExposure*(1-exposed)*riseFacing);
                float wallDepth=saturate(raisedDistance/max(.035,raisedHeight*saturate(-actualInward.y)+.08));
                float2 faceWorld=(upperLip-float2(0,projectionHeight-raisedHeight)-faceInward*.035)*_VeinGrid.xy+_VeinGrid.zw;
                // Stretch the host's upper rock down the face instead of painting
                // a constant brown outline. The vertical strata are coarse facets.
                faceWorld+=float2(-faceInward.x*.055,wallDepth*.38)*_VeinGrid.xy;
                half3 wallEarth=lerp(TerrainMaterialSample(faceWorld).rgb,earth,.22);
                float faceLight=saturate(dot(actualInward,light)*.5+.5);
                float tilt=(VeinHash(face.key-42)-.5)*.30;
                float plane=clamp(.62+.22*faceLight+tilt*.6+dot(face.offset,float2(-.08,.06)),.50,.94);
                float strataLine=frac(wallDepth*(2+floor(stone*2))+stone*.55);
                float strata=lerp(.92,1,smoothstep(.02,.10,strataLine));
                float fracture=lerp(.76,1,smoothstep(.006,.045,face.seam));
                float shoulder=1-smoothstep(.04,.18,wallDepth);
                half3 wallArt=CopperEarthMaterial(p);
                if(dot(wallArt,wallArt)>.000001)
                {
                    half hostLuma=max(.015h,dot(wallEarth,half3(.3,.5,.2)));
                    half artLuma=dot(wallArt,half3(.3,.5,.2));
                    wallEarth=lerp(wallArt,artLuma*wallEarth/hostLuma,.45h);
                    // The painted faces already contain relief light and creases.
                    wallEarth*=(.92+.10*faceLight)*lerp(.90,1,smoothstep(.006,.045,face.seam));
                }
                else wallEarth*=plane*strata*fracture*(1+shoulder*.20);
                float topLip=(1-smoothstep(.006,.065,-raisedDistance))*(1-liftedExposure);
                earth*=1+topLip*lerp(-.13,.36,saturate(dot(-actualInward,light)*.5+.5));
                // The contact and overhang shadow start at the new wall foot.
                // Only the inward light-facing edges cast the longer copper shadow.
                float contact=(1-smoothstep(.008,.070,foot))*.46;
                float directed=saturate(-dot(inward,light));
                float shadowReach=.04+directed*(.075+raisedHeight*.22+wallWidth*.16);
                float castShadow=(1-smoothstep(.015,shadowReach,foot))*(.12+.56*directed);
                copper*=1-max(contact,castShadow);
                colour=lerp(earth,copper,exposed);
                colour=lerp(colour,wallEarth,wallBand);
            }
        }
    }
    SurfaceData2D surface=(SurfaceData2D)0;InputData2D input=(InputData2D)0;
    InitializeSurfaceData(colour,1,half4(1,1,1,1),surface);
    InitializeInputData(i.uv,i.lightingUV,input);
    return CombinedShapeLightShared(surface,input);
}
#endif
