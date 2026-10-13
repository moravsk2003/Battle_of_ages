Shader "Custom/StylizedTerrain"
{
    // Procedural terrain surface (no textures): biome colour from vertex colours (blended per vertex by
    // ProceduralBiomeChunk), multi-scale noise variation, dirt and stratified rock on slopes, snow above the
    // snow line, noise-derived micro relief, lit with URP PBR (shadows, SSAO, additional lights, fog).
    Properties
    {
        _GrassDeep ("Deep Green Grass", Color) = (0.18, 0.42, 0.12, 1)
        _GrassMid ("Lush Meadow Grass", Color) = (0.28, 0.56, 0.16, 1)
        _GrassLight ("Sunlit Grass", Color) = (0.44, 0.68, 0.22, 1)
        _FlowerYellow ("Dandelion Yellow", Color) = (0.95, 0.82, 0.22, 1)
        _FlowerBlue ("Cornflower Blue", Color) = (0.35, 0.65, 0.92, 1)
        _FlowerRed ("Poppy Red", Color) = (0.92, 0.22, 0.25, 1)
        _DirtColor ("Path Soil Color", Color) = (0.33, 0.27, 0.20, 1)
        _RockColor ("Cliff Rock Color", Color) = (0.43, 0.42, 0.40, 1)
        _SnowColor ("Snow Color", Color) = (0.86, 0.89, 0.93, 1)
        _SnowHeight ("Snow Line Height", Float) = 46
        _DetailStrength ("Micro Relief Strength", Range(0, 2)) = 0.9
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _GrassDeep, _GrassMid, _GrassLight;
            float4 _FlowerYellow, _FlowerBlue, _FlowerRed;
            float4 _DirtColor, _RockColor, _SnowColor;
            float _SnowHeight, _DetailStrength;
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

        float fbm3(float2 p)
        {
            return noise2D(p) * 0.55 + noise2D(p * 2.07 + 13.1) * 0.30 + noise2D(p * 4.13 + 71.7) * 0.15;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 vertexColor : COLOR;
                float fogFactor : TEXCOORD2;
            };

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

            float microHeight(float2 uv)
            {
                return noise2D(uv * 1.3) * 0.6 + noise2D(uv * 4.1 + 5.3) * 0.3 + noise2D(uv * 11.0 + 2.1) * 0.1;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 P = input.positionWS;
                float2 uv = P.xz;
                float3 N = normalize(input.normalWS);
                float camDist = distance(_WorldSpaceCameraPos.xyz, P);
                float detailFade = 1.0 - smoothstep(25.0, 110.0, camDist);

                // ---- Noise fields at several scales
                float m1 = fbm3(uv * 0.012);                     // regional tone
                float m2 = fbm3(uv * 0.05 + float2(31.7, 7.3));  // patches (dry grass, dirt)
                float m3 = noise2D(uv * 0.33 + float2(5.1, 9.9)); // clumps
                float m4 = noise2D(uv * 2.6 + float2(1.7, 3.3)); // fine speckle

                // ---- Grass: biome colour (vertex colour), white vertex colour = fall back to material colours
                float3 vc = input.vertexColor.rgb;
                float isWhite = step(2.9, vc.r + vc.g + vc.b);
                float3 materialGrass = lerp(_GrassDeep.rgb, _GrassMid.rgb, m1);
                float3 biome = lerp(vc, materialGrass, isWhite);

                float3 grass = biome * lerp(0.78, 1.10, m1);
                float dry = smoothstep(0.52, 0.78, m2);
                grass = lerp(grass, grass * float3(1.16, 1.07, 0.80), dry * 0.45);   // sun-dried yellowish patches
                grass *= lerp(0.86, 1.08, m3);
                grass *= lerp(1.0, lerp(0.80, 1.14, m4), detailFade);              // blade speckle up close

                // Sparse wildflowers up close
                float flowers = smoothstep(0.80, 0.93, m3 * noise2D(uv * 1.7 + 4.4)) * detailFade;
                float flowerPick = hash21(floor(uv * 1.7));
                float3 flowerCol = flowerPick < 0.45 ? _FlowerYellow.rgb : (flowerPick < 0.75 ? _FlowerBlue.rgb : _FlowerRed.rgb);
                grass = lerp(grass, flowerCol * 0.85, flowers * 0.6);

                // ---- Dirt and rock
                float slope = 1.0 - saturate(N.y);
                float3 dirt = _DirtColor.rgb * lerp(0.80, 1.15, m3) * lerp(0.92, 1.06, m4);
                float dirtPatch = smoothstep(0.66, 0.80, m2 * 0.65 + m3 * 0.35) * 0.7;
                float dirtSlope = smoothstep(0.14, 0.28, slope + (m3 - 0.5) * 0.12);
                float alpine = smoothstep(24.0, 60.0, P.y);                          // mountains show bare rock sooner
                float rockSlope = smoothstep(lerp(0.30, 0.14, alpine), lerp(0.46, 0.28, alpine), slope + (m4 - 0.5) * 0.10);

                float strata = noise2D(float2((uv.x + uv.y) * 0.21, P.y * 1.9)) * 0.6 + noise2D(float2(uv.x * 0.9 - uv.y * 0.7, P.y * 6.0)) * 0.4;
                float3 rock = _RockColor.rgb * lerp(0.70, 1.18, strata) * lerp(0.88, 1.08, m4);
                rock = lerp(rock, rock * float3(0.92, 1.0, 0.90), smoothstep(0.4, 0.8, m2) * 0.5); // lichen tint

                float3 albedo = lerp(grass, dirt, max(dirtPatch, dirtSlope));
                albedo = lerp(albedo, rock, rockSlope);

                // ---- Snow on flatter ground above a noisy snow line
                float snowLine = _SnowHeight + (m1 - 0.5) * 14.0;
                float snow = smoothstep(snowLine, snowLine + 7.0, P.y) * (1.0 - smoothstep(0.22, 0.42, slope));
                albedo = lerp(albedo, _SnowColor.rgb * lerp(0.92, 1.03, m4), snow);

                // ---- Base camp trampled clearing around the campfire
                float distFromCamp = length(uv - float2(1.5, 3.5));
                float campDirt = 1.0 - smoothstep(1.8, 5.5, distFromCamp + (m3 - 0.5) * 1.5);
                albedo = lerp(albedo, _DirtColor.rgb * 0.9, campDirt * 0.8);

                // ---- Micro relief: perturb the normal with the gradient of a small-scale height field
                float e = 0.06;
                float h0 = microHeight(uv);
                float hx = microHeight(uv + float2(e, 0));
                float hz = microHeight(uv + float2(0, e));
                float reliefAmt = _DetailStrength * detailFade * lerp(0.35, 1.0, max(rockSlope, dirtSlope)) * (1.0 - snow * 0.6);
                float3 Nd = normalize(N + float3(-(hx - h0) / e, 0.0, -(hz - h0) / e) * 0.18 * reliefAmt);

                // ---- URP PBR lighting
                InputData inputData = (InputData)0;
                inputData.positionWS = P;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = Nd;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(P);
                inputData.shadowCoord = TransformWorldToShadowCoord(P);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(Nd);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = 0.0;
                surface.specular = half3(0, 0, 0);
                surface.smoothness = lerp(lerp(0.08, 0.18, rockSlope), 0.38, snow);
                surface.occlusion = lerp(0.82, 1.0, m3);   // cheap cavity darkening in clumps
                surface.alpha = 1.0;
                surface.normalTS = half3(0, 0, 1);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1.0);
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            float3 _LightDirection;

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_TARGET { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings DepthVert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }
            half DepthFrag(Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            // Needed so SSAO (DepthNormals source) sees the terrain.
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                float3 n = NormalizeNormalPerPixel(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(n);
                    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                    return half4(PackFloat2To888(remappedOctNormalWS), 0.0);
                #else
                    return half4(n, 0.0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
