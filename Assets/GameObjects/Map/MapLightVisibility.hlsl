#ifndef MINING_MAP_LIGHT_VISIBILITY_INCLUDED
#define MINING_MAP_LIGHT_VISIBILITY_INCLUDED

float4 _HeadlampOriginRange;
float4 _HeadlampDirectionAngles;
float _HeadlampInnerRadius;
TEXTURE2D(_TerrainOcclusionTex);
SAMPLER(sampler_TerrainOcclusionTex);
float4 _TerrainOcclusionRect;
float4 _TerrainOcclusionSize;
float4 _UltroniumSources[32];
int _UltroniumCount;
float4 _AltarSource;
TEXTURE2D(_AltarMask);
SAMPLER(sampler_AltarMask);
float4 _AltarRect;
float4 _AltarMaskSize;

float MapAltarShellVisibility(float2 source, float2 destination)
{
    float2 size = _AltarMaskSize.xy;
    if (any(size <= 0)) return 1;
    float2 sourceUv = (source - _AltarRect.xy) * _AltarRect.zw;
    float2 targetUv = (destination - _AltarRect.xy) * _AltarRect.zw;
    if (max(sourceUv.x, targetUv.x) < 0 || min(sourceUv.x, targetUv.x) > 1 ||
        max(sourceUv.y, targetUv.y) < 0 || min(sourceUv.y, targetUv.y) > 1) return 1;
    float2 sourceCell = floor(sourceUv * size);
    float2 targetCell = floor(targetUv * size);
    int steps = min(96, (int)ceil(max(abs((targetUv.x-sourceUv.x)*size.x),
                                     abs((targetUv.y-sourceUv.y)*size.y)) * 2));
    [loop]
    for (int step = 1; step < steps; step++)
    {
        float2 cell = floor(lerp(sourceUv, targetUv, (float)step / steps) * size);
        if (all(cell == sourceCell) || all(cell == targetCell) ||
            any(cell < 0) || any(cell >= size)) continue;
        if (SAMPLE_TEXTURE2D_LOD(_AltarMask, sampler_AltarMask, (cell + .5) / size, 0).g > .5)
            return 0;
    }
    return 1;
}

float MapTorchVisibility(float2 source, float2 destination)
{
    float2 size = _TerrainOcclusionSize.xy;
    if (any(size <= 0)) return 1;
    float2 sourceCell = floor((source - _TerrainOcclusionRect.xy) * _TerrainOcclusionRect.zw * size);
    float2 targetCell = floor((destination - _TerrainOcclusionRect.xy) * _TerrainOcclusionRect.zw * size);
    float2 cellDelta = targetCell - sourceCell;
    int steps = min(64, (int)ceil(max(abs(cellDelta.x), abs(cellDelta.y)) * 2));
    [loop]
    for (int step = 1; step < steps; step++)
    {
        float2 position = lerp(source, destination, (float)step / steps);
        float2 cell = floor((position - _TerrainOcclusionRect.xy) * _TerrainOcclusionRect.zw * size);
        if (all(cell == sourceCell) || all(cell == targetCell) ||
            any(cell < 0) || any(cell >= size)) continue;
        float2 sampleUv = (cell + .5) / size;
        if (SAMPLE_TEXTURE2D_LOD(_TerrainOcclusionTex, sampler_TerrainOcclusionTex, sampleUv, 0).r > .5)
            return 0;
    }
    return 1;
}

float MapTorchLight(float2 worldPos)
{
    float2 uv = (worldPos - _TerrainOcclusionRect.xy) * _TerrainOcclusionRect.zw;
    if (any(uv < 0) || any(uv > 1)) return 0;
    return saturate(SAMPLE_TEXTURE2D(_TerrainOcclusionTex, sampler_TerrainOcclusionTex, uv).g);
}

float MapOtherLocalLight(float2 worldPos)
{
    float light = 0;
    float2 delta = worldPos - _HeadlampOriginRange.xy;
    float range = _HeadlampOriginRange.z;
    float distanceSquared = dot(delta, delta);
    if (_HeadlampOriginRange.w > 0 && range > 0 && distanceSquared < range * range)
    {
        float distance = sqrt(distanceSquared);
        float angle = dot(delta, _HeadlampDirectionAngles.xy) / max(distance, .0001);
        float beam = smoothstep(_HeadlampDirectionAngles.w, _HeadlampDirectionAngles.z, angle);
        float centerGlow = 1 - smoothstep(0, max(_HeadlampInnerRadius, .0001), distance);
        float falloff = 1 - smoothstep(_HeadlampInnerRadius, range, distance);
        light = saturate(max(beam, centerGlow) * falloff * _HeadlampOriginRange.w) *
            MapAltarShellVisibility(_HeadlampOriginRange.xy, worldPos);
    }

    [loop]
    for (int index = 0; index < 32; index++)
    {
        if (index >= _UltroniumCount) break;
        float4 ore = _UltroniumSources[index];
        float distance = length(worldPos - ore.xy);
        if (ore.z > 0 && distance < ore.z)
            light = max(light, saturate(ore.w * (1 - smoothstep(0, ore.z, distance))) *
                MapAltarShellVisibility(ore.xy, worldPos));
    }
    float altarDistance = length(worldPos - _AltarSource.xy);
    float2 altarUv = (worldPos-_AltarRect.xy)*_AltarRect.zw;
    if (_AltarSource.w > 0 && all(altarUv>0) && all(altarUv<1))
        light=max(light,SAMPLE_TEXTURE2D(_AltarMask,sampler_AltarMask,altarUv).r*saturate(.72+_AltarSource.w*.2));
    if (_AltarSource.z > 0 && altarDistance < _AltarSource.z &&
        MapAltarShellVisibility(_AltarSource.xy, worldPos) > 0 &&
        MapTorchVisibility(_AltarSource.xy, worldPos) > 0)
        light = max(light, saturate(_AltarSource.w * (1 - smoothstep(0, _AltarSource.z, altarDistance))));
    return light;
}

float MapLocalLight(float2 worldPos)
{
    return max(MapOtherLocalLight(worldPos), MapTorchLight(worldPos));
}

#endif
