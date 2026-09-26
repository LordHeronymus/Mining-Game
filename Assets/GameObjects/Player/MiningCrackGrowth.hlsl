// Every stage samples the same final fracture. Only its revealed area grows;
// previously revealed pixels keep their original position, colour and opacity.
half4 SampleGrowingCrack(float2 uv)
{
    half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
    // Final crack occupies the middle cell of the sheet's bottom row.
    float2 local = (uv - float2(1.0 / 3.0, 0)) * float2(3, 2);
    float2 offset = local - float2(0.5, 0.5);
    float angle = atan2(offset.y, offset.x);
    float distance = length(offset) * (1 + 0.08 * sin(angle * 3 + 0.7));
    float radius = lerp(0.045, 0.80, saturate(_CrackGrowth));
    color.a *= 1 - smoothstep(radius - 0.018, radius, distance);
    return color;
}
