Shader "Custom/ProceduralRock"
{
    // UV-free stone: 3D world-space noise for tone variation, ridged-noise cracks, lichen/moss on upward faces and
    // a noise-derived micro relief, lit with URP PBR. Used for every ProceduralVisualMaterials "Stone" surface
    // (boulders, pebbles, campfire stones), whose generated meshes have no usable UVs.
    Properties
    {
        _BaseColor ("Stone Color", Color) = (0.45, 0.43, 0.40, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.25
        _Metallic ("Metallic", Range(0, 1)) = 0
        _LichenColor ("Lichen Color", Color) = (0.55, 0.55, 0.36, 1)
        _MossColor ("Moss Color", Color) = (0.28, 0.40, 0.17, 1)
        _CrackDarkness ("Crack Darkness", Range(0, 1)) = 0.45
        _ReliefStrength ("Micro Relief", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor, _LichenColor, _MossColor;
            float _Smoothness, _Metallic, _CrackDarkness, _ReliefStrength;
        CBUFFER_END

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
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float fogFactor : TEXCOORD2; };

            Varyings vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float relief(float3 p) { return noise3D(p * 3.1) * 0.6 + noise3D(p * 9.7 + 5.0) * 0.4; }

            half4 frag(Varyings input) : SV_Target
            {
                float3 p = input.positionWS;
                float3 n = normalize(input.normalWS);

                float big = noise3D(p * 0.55) * 0.6 + noise3D(p * 1.3 + 11.0) * 0.4;
                float med = noise3D(p * 2.6 + 4.0);
                float fine = noise3D(p * 8.5 + 9.0);
                float crack = smoothstep(0.88, 0.985, 1.0 - abs(noise3D(p * 1.9 + 21.0) * 2.0 - 1.0));

                float3 albedo = _BaseColor.rgb * lerp(0.70, 1.18, big) * lerp(0.88, 1.08, med) * lerp(0.92, 1.06, fine);
                albedo = lerp(albedo, albedo * _CrackDarkness, crack);

                float up = saturate(n.y);
                float growth = smoothstep(0.50, 0.85, up + (med - 0.5) * 0.45) * smoothstep(0.30, 0.65, big);
                albedo = lerp(albedo, lerp(_MossColor.rgb, _LichenColor.rgb, fine), growth * 0.7);

                float e = 0.02;
                float h0 = relief(p);
                float3 grad = float3(relief(p + float3(e, 0, 0)) - h0, relief(p + float3(0, e, 0)) - h0, relief(p + float3(0, 0, e)) - h0) / e;
                grad -= n * dot(grad, n);                         // keep only the tangential part
                float detail = 1.0 - smoothstep(15.0, 60.0, distance(_WorldSpaceCameraPos.xyz, p));
                float3 nd = normalize(n - grad * 0.06 * _ReliefStrength * detail);

                InputData inputData = (InputData)0;
                inputData.positionWS = p;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = nd;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(p);
                inputData.shadowCoord = TransformWorldToShadowCoord(p);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(nd);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.metallic = _Metallic;
                s.smoothness = _Smoothness * lerp(1.0, 0.5, growth);
                s.occlusion = lerp(0.65, 1.0, 1.0 - crack);
                s.alpha = 1.0;
                s.normalTS = half3(0, 0, 1);

                half4 color = UniversalFragmentPBR(inputData, s);
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
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };
            float3 _LightDirection;
            Varyings ShadowVert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                float3 pws = TransformObjectToWorld(input.positionOS.xyz);
                float3 nws = TransformObjectToWorldNormal(input.normalOS);
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(pws, nws, _LightDirection));
                return o;
            }
            half4 ShadowFrag(Varyings input) : SV_Target { return 0; }
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
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings DepthVert(Attributes input) { UNITY_SETUP_INSTANCE_ID(input); Varyings o; o.positionCS = TransformObjectToHClip(input.positionOS.xyz); return o; }
            half DepthFrag(Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings DNVert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }
            half4 DNFrag(Varyings input) : SV_Target
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
