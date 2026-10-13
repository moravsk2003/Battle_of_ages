Shader "Custom/StylizedWindFoliage"
{
    Properties
    {
        _BaseColor ("Base Color Tint", Color) = (0.28, 0.55, 0.18, 1)
        _TopColor ("Sunlight Foliage Color", Color) = (0.55, 0.78, 0.22, 1)
        _BottomColor ("Foliage Shadow Color", Color) = (0.18, 0.36, 0.12, 1)
        _Translucency ("Translucency Glow", Range(0, 1)) = 0.5
        _WindSpeed ("Wind Speed", Float) = 1.6
        _WindStrength ("Wind Strength", Float) = 0.08
        _WindFrequency ("Wind Frequency", Float) = 0.6
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Cull Off
            ZWrite On

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
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : NORMAL;
                float4 vertexColor : COLOR;
                float fogFactor : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TopColor;
                float4 _BottomColor;
                float _Translucency;
                float _WindSpeed;
                float _WindStrength;
                float _WindFrequency;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                
                // Procedural Wind sway
                float windPhase = _Time.y * _WindSpeed + (worldPos.x + worldPos.z) * _WindFrequency;
                float windGust = sin(windPhase) * cos(windPhase * 0.73f + worldPos.y * 0.5f);
                float heightWeight = saturate(input.positionOS.y * 0.35f);
                
                float3 windOffset = float3(windGust * _WindStrength * heightWeight, 0, sin(windPhase * 1.3f) * _WindStrength * 0.6f * heightWeight);
                worldPos += windOffset;

                output.positionWS = worldPos;
                output.positionCS = TransformWorldToHClip(worldPos);
                output.uv = input.uv;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.vertexColor = (length(input.color.rgb) > 0.01) ? input.color : float4(1, 1, 1, 1);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            float hash31(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float noise3D(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash31(i), hash31(i + float3(1, 0, 0)), f.x),
                                 lerp(hash31(i + float3(0, 1, 0)), hash31(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(hash31(i + float3(0, 0, 1)), hash31(i + float3(1, 0, 1)), f.x),
                                 lerp(hash31(i + float3(0, 1, 1)), hash31(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Top-to-bottom sunlight gradient. _TopColor/_BottomColor already contain the base colour
                // (multiplying by _BaseColor again squared the colour and made all foliage near-black).
                float3 normal = normalize(input.normalWS);
                float upDot = saturate(dot(normal, float3(0, 1, 0)) * 0.5 + 0.5);
                half3 gradientFoliage = lerp(_BottomColor.rgb, _TopColor.rgb, upDot);
                half3 baseAlbedo = gradientFoliage * input.vertexColor.rgb * 0.85;

                // Procedural leaf clumps: darker gaps between clusters of leaves, lighter leaf edges,
                // so the faceted crowns read as foliage instead of flat polygons.
                float3 p = input.positionWS;
                float clumps = noise3D(p * 2.3);
                float leaves = noise3D(p * 9.0 + 3.7);
                float camDist = distance(_WorldSpaceCameraPos.xyz, p);
                float detail = 1.0 - smoothstep(20.0, 80.0, camDist);
                baseAlbedo *= lerp(0.70, 1.12, smoothstep(0.25, 0.75, clumps));
                baseAlbedo *= lerp(1.0, lerp(0.82, 1.14, leaves), detail);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));

                // Soft half-lambert lighting for lush leaves
                float NdotL = saturate(dot(normal, mainLight.direction) * 0.6 + 0.4);
                float shadow = mainLight.shadowAttenuation;
                float lightIntensity = NdotL * shadow;

                // Backlight subsurface transmission glow
                float backLight = saturate(dot(-normal, mainLight.direction)) * _Translucency * shadow;
                half3 directLighting = (lightIntensity + backLight) * mainLight.color * baseAlbedo;

                // Spherical Harmonics ambient lighting
                half3 ambient = SampleSH(normal) * baseAlbedo * 1.1;

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
            Cull Off

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 texcoord     : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TopColor;
                float4 _BottomColor;
                float _Translucency;
                float _WindSpeed;
                float _WindStrength;
                float _WindFrequency;
            CBUFFER_END

            float3 _LightDirection;

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                float windPhase = _Time.y * _WindSpeed + (positionWS.x + positionWS.z) * _WindFrequency;
                float windGust = sin(windPhase) * cos(windPhase * 0.73f + positionWS.y * 0.5f);
                float heightWeight = saturate(input.positionOS.y * 0.35f);
                positionWS += float3(windGust * _WindStrength * heightWeight, 0, sin(windPhase * 1.3f) * _WindStrength * 0.6f * heightWeight);

                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            // Lets SSAO darken the ground around and under trees.
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TopColor;
                float4 _BottomColor;
                float _Translucency;
                float _WindSpeed;
                float _WindStrength;
                float _WindFrequency;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float windPhase = _Time.y * _WindSpeed + (positionWS.x + positionWS.z) * _WindFrequency;
                float windGust = sin(windPhase) * cos(windPhase * 0.73f + positionWS.y * 0.5f);
                float heightWeight = saturate(input.positionOS.y * 0.35f);
                positionWS += float3(windGust * _WindStrength * heightWeight, 0, sin(windPhase * 1.3f) * _WindStrength * 0.6f * heightWeight);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                float3 n = NormalizeNormalPerPixel(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = saturate(PackNormalOctQuadEncode(n) * 0.5 + 0.5);
                    return half4(PackFloat2To888(oct), 0.0);
                #else
                    return half4(n, 0.0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
