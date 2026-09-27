#ifndef MINING_ARTIFACT_EMBEDDING
#define MINING_ARTIFACT_EMBEDDING

half4 ArtifactEmbedded(float2 uv, float2 positionWS, half4 tint)
{
    half4 artifact = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * tint;
    if (_UniformStone.x <= 0 || _EmbeddingStrength <= 0)
        return artifact;

    float2 cell = (positionWS - _UniformStone.zw) / _UniformStone.x;
    float pockets = .5 + .28 * sin(cell.x * 14 + cell.y * 9)
        + .22 * sin(cell.y * 19 - cell.x * 11);
    half pocket = smoothstep(.42, .62, pockets);
    half lipCoverage = lerp(.45h, 1.0h, pocket);
    float2 lipReach = lerp(12, 26, pocket) * lerp(1, 3.2, _EmbeddingStrength)
        * _MainTex_TexelSize.xy;
    half inner = artifact.a;
    half shadowInner = artifact.a;

    [unroll] for (int n = 0; n < 8; n++)
    {
        float angle = n * (TWO_PI / 8);
        float2 direction = float2(cos(angle), sin(angle));
        direction.y *= direction.y < 0 ? 1.65 : .55;
        inner = min(inner, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex,
            uv + direction * lipReach).a * tint.a);
        shadowInner = min(shadowInner, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex,
            uv + direction * lipReach * 1.35).a * tint.a);
    }

    half lip = saturate((artifact.a - inner) * 2.2h) * lipCoverage * _EmbeddingStrength;
    half shadow = saturate((artifact.a - shadowInner) * 1.5h)
        * (1 - lip) * _EmbeddingStrength;
    half3 rock = TerrainMaterialSample(positionWS).rgb;
    half3 inside = lerp(artifact.rgb * (1 - .12h * shadow), rock, lip);
    return half4(inside, artifact.a);
}

#endif
