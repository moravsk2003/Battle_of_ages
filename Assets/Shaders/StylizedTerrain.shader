Shader "Custom/StylizedTerrain"
{
    Properties
    {
        _GrassDeep ("Deep Green Grass", Color) = (0.18, 0.42, 0.12, 1)
        _GrassMid ("Lush Meadow Grass", Color) = (0.28, 0.56, 0.16, 1)
        _GrassLight ("Sunlit Grass", Color) = (0.44, 0.68, 0.22, 1)
        _FlowerYellow ("Dandelion Yellow", Color) = (0.95, 0.82, 0.22, 1)
        _FlowerBlue ("Cornflower Blue", Color) = (0.35, 0.65, 0.92, 1)
        _FlowerRed ("Poppy Red", Color) = (0.92, 0.22, 0.25, 1)
        _DirtColor ("Path Soil Color", Color) = (0.36, 0.25, 0.14, 1)
        _RockColor ("Cliff Rock Color", Color) = (0.45, 0.43, 0.41, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 vertexColor : COLOR;
                float fogFactor : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _GrassDeep;
                float4 _GrassMid;
                float4 _GrassLight;
                float4 _FlowerYellow;
                float4 _FlowerBlue;
                float4 _FlowerRed;
                float4 _DirtColor;
                float4 _RockColor;
            CBUFFER_END

            float hash21(float2 p)
            {
                p = frac(p * float2(234.34, 435.345));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            float noise2D(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                output.positionWS = worldPos;
                output.positionCS = TransformWorldToHClip(worldPos);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.vertexColor = input.color;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float2 worldUV = input.positionWS.xz;

                // Stylized grass patch noise
                float nMacro = noise2D(worldUV * 0.06);
                float nMedium = noise2D(worldUV * 0.25 + float2(15.2, 33.7));
                float nDetail = noise2D(worldUV * 1.2 + float2(8.4, 91.2));
                float nFine = noise2D(worldUV * 3.5);

                // Multi-tone lush grass blend
                half3 grassCol = lerp(_GrassDeep.rgb, _GrassMid.rgb, nMacro);
                grassCol = lerp(grassCol, _GrassLight.rgb, nMedium * 0.6);

                // Stylized brush/blade texture overlay
                float bladePattern = (sin(worldUV.x * 2.2 + worldUV.y * 1.8 + nFine * 3.0) * 0.5 + 0.5);
                grassCol *= (0.90 + bladePattern * 0.18);

                // Wildflower clusters
                float flowerCluster = smoothstep(0.70, 0.92, nDetail * nMacro);
                if (flowerCluster > 0.05)
                {
                    float flowerType = hash21(floor(worldUV * 1.2));
                    half3 flowerCol = (flowerType < 0.45) ? _FlowerYellow.rgb : ((flowerType < 0.75) ? _FlowerBlue.rgb : _FlowerRed.rgb);
                    grassCol = lerp(grassCol, flowerCol, flowerCluster * 0.85);
                }

                // Slope soil & cliffs
                float slope = 1.0 - saturate(normal.y);
                float dirtBlend = smoothstep(0.18, 0.40, slope + (nMedium - 0.5) * 0.18);
                float rockBlend = smoothstep(0.42, 0.68, slope + (nDetail - 0.5) * 0.22);

                half3 surfaceAlbedo = lerp(grassCol, _DirtColor.rgb, dirtBlend);
                surfaceAlbedo = lerp(surfaceAlbedo, _RockColor.rgb, rockBlend);

                // Base camp dirt clearing (around campfire center 0..5m)
                float distFromCenter = length(input.positionWS.xz - float2(1.5, 3.5));
                float campDirt = 1.0 - smoothstep(1.8, 4.8, distFromCenter + (nDetail - 0.5) * 1.2);
                surfaceAlbedo = lerp(surfaceAlbedo, _DirtColor.rgb * 0.92, campDirt * 0.75);

                // Lighting
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float NdotL = saturate(dot(normal, mainLight.direction) * 0.65 + 0.35);
                float shadow = mainLight.shadowAttenuation;

                half3 directLighting = NdotL * shadow * mainLight.color * surfaceAlbedo;
                half3 ambient = SampleSH(normal) * surfaceAlbedo * 1.15;

                half3 finalColor = directLighting + ambient;
                finalColor = MixFog(finalColor, input.fogFactor);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
            };

            float3 _LightDirection;

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
