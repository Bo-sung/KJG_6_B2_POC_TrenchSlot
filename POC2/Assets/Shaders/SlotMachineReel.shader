Shader "WeaponWorkshop/SlotMachineReel"
{
    Properties
    {
        _Atlas ("Six Module Captures", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Atlas);
            SAMPLER(sampler_Atlas);

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // The cylinder's local Y is its axle; +Z faces the machine front.
                float angle = atan2(input.positionOS.x, input.positionOS.z);
                float u = frac(angle / (2.0 * PI) + 1.0 / 12.0);
                float v = saturate(input.positionOS.y * 0.5 + 0.5);
                return SAMPLE_TEXTURE2D(_Atlas, sampler_Atlas, float2(u, v));
            }
            ENDHLSL
        }
    }
}
