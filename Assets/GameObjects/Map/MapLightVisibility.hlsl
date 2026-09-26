#ifndef MINING_MAP_LIGHT_VISIBILITY_INCLUDED
#define MINING_MAP_LIGHT_VISIBILITY_INCLUDED

float4 _HeadlampOriginRange;
float4 _HeadlampDirectionAngles;
float _HeadlampInnerRadius;
float4 _TorchSources[64];
int _TorchCount;

float MapLocalLight(float2 worldPos)
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
        light = saturate(max(beam, centerGlow) * falloff * _HeadlampOriginRange.w);
    }
    [loop]
    for (int index = 0; index < 64; index++)
    {
        if (index >= _TorchCount) break;
        float4 torch = _TorchSources[index];
        float distance = length(worldPos - torch.xy);
        if (torch.z > 0 && distance < torch.z)
        {
            float glow = saturate(torch.w * (1 - smoothstep(0, torch.z, distance)));
            light = max(light, glow);
        }
    }
    return light;
}

#endif
