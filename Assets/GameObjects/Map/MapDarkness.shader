Shader "Mining Game/Map Darkness"
{
    Properties { _MainTex ("Daylight mask", 2D) = "black" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _HeadlampOriginRange;
            float4 _HeadlampDirectionAngles;
            float _HeadlampInnerRadius;
            float4 _TorchSources[64];
            int _TorchCount;
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 worldPos : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.worldPos = TransformObjectToWorld(input.positionOS).xy;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float darkness = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                float2 delta = input.worldPos - _HeadlampOriginRange.xy;
                float range = _HeadlampOriginRange.z;
                float distanceSquared = dot(delta, delta);
                if (_HeadlampOriginRange.w > 0 && range > 0 && distanceSquared < range * range)
                {
                    float distance = sqrt(distanceSquared);
                    float angle = dot(delta, _HeadlampDirectionAngles.xy) / max(distance, .0001);
                    float beam = smoothstep(_HeadlampDirectionAngles.w, _HeadlampDirectionAngles.z, angle);
                    float centerGlow = 1 - smoothstep(0, max(_HeadlampInnerRadius, .0001), distance);
                    float falloff = 1 - smoothstep(_HeadlampInnerRadius, range, distance);
                    float lamp = saturate(max(beam, centerGlow) * falloff * _HeadlampOriginRange.w);
                    darkness = min(darkness, 1 - lamp);
                }
                [loop]
                for (int index = 0; index < 64; index++)
                {
                    if (index >= _TorchCount) break;
                    float4 torch = _TorchSources[index];
                    float distance = length(input.worldPos - torch.xy);
                    if (torch.z > 0 && distance < torch.z)
                    {
                        float glow = saturate(torch.w * (1 - smoothstep(0, torch.z, distance)));
                        darkness = min(darkness, 1 - glow);
                    }
                }
                return half4(0, 0, 0, darkness);
            }
            ENDHLSL
        }
    }
}
