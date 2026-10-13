Shader "Custom/GrassBlades"
{
    // GPU-instanced grass clumps drawn by GrassFieldRenderer. Colour follows the terrain shader's grass tone
    // (same biome tint and noise fields) so blades grow out of the ground seamlessly; tips are lighter, wind
    // sways the upper part, blades shrink into the ground towards the edge of the draw radius.
    Properties
    {
        _BiomeTint ("Biome Tint (set by script)", Color) = (0.35, 0.55, 0.22, 1)
        _TipColor ("Tip Highlight", Color) = (0.80, 0.86, 0.45, 1)
        _WindStrength ("Wind Strength", Float) = 0.14
        _WindSpeed ("Wind Speed", Float) = 1.7
        _FadeStart ("Fade Start Distance", Float) = 28
        _FadeEnd ("Fade End Distance", Float) = 38
        _Translucency ("Back-light Translucency", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BiomeTint, _TipColor;
                float _WindStrength, _WindSpeed, _FadeStart, _FadeEnd, _Translucency;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;     // r: height along blade (0 root .. 1 tip), g: per-blade random
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 bladeData : TEXCOORD2;
                float3 rootWS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            // Same noise as Custom/StylizedTerrain so the grass tone matches the ground under it.
            float hash21(float2 p) { p = frac(p * float2(234.34, 435.345)); p += dot(p, p + 34.23); return frac(p.x * p.y); }
            float noise2D(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), f.x), lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), f.x), f.y);
            }
            float fbm3(float2 p) { return noise2D(p) * 0.55 + noise2D(p * 2.07 + 13.1) * 0.30 + noise2D(p * 4.13 + 71.7) * 0.15; }

            Varyings vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o = (Varyings)0;

                float3 rootWS = TransformObjectToWorld(float3(0, 0, 0));
                float h = input.color.r;
                float dist = distance(_WorldSpaceCameraPos.xyz, rootWS);
                float fade = 1.0 - smoothstep(_FadeStart, _FadeEnd, dist);

                float3 posOS = input.positionOS.xyz;
                posOS.y *= fade;                                  // sink into the ground at the edge of the field
                float3 posWS = TransformObjectToWorld(posOS);

                // Wind: gusts travelling across the field, stronger towards the tips
                float phase = _Time.y * _WindSpeed + rootWS.x * 0.21 + rootWS.z * 0.17 + input.color.g * 2.0;
                float gust = sin(phase) * 0.6 + sin(phase * 2.3 + 1.7) * 0.25 + 0.35;
                float bend = h * h * _WindStrength * fade;
                posWS.x += gust * bend;
                posWS.z += sin(phase * 1.3) * bend * 0.5;
                posWS.y -= abs(gust) * bend * 0.25;

                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = normalize(lerp(float3(0, 1, 0), TransformObjectToWorldNormal(input.normalOS), 0.35));
                o.bladeData = input.color.rg;
                o.rootWS = rootWS;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool isFront : SV_IsFrontFace) : SV_Target
            {
                float2 uv = i.rootWS.xz;
                float m1 = fbm3(uv * 0.012);
                float m2 = fbm3(uv * 0.05 + float2(31.7, 7.3));
                float m3 = noise2D(uv * 0.33 + float2(5.1, 9.9));

                // Ground tone (as in the terrain shader), then a lighter, slightly yellow tip
                float3 ground = _BiomeTint.rgb * lerp(0.78, 1.10, m1);
                ground = lerp(ground, ground * float3(1.16, 1.07, 0.80), smoothstep(0.52, 0.78, m2) * 0.45);
                ground *= lerp(0.86, 1.08, m3);
                float h = i.bladeData.x;
                float3 albedo = ground * lerp(0.62, 1.0, saturate(h * 1.4));                       // darker at the root (AO)
                albedo = lerp(albedo, _TipColor.rgb * ground * 2.0, smoothstep(0.55, 1.0, h) * 0.35);
                albedo *= lerp(0.88, 1.12, i.bladeData.y);                                           // per-blade variation

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 n = normalize(i.normalWS);
                float ndl = saturate(dot(n, mainLight.direction) * 0.6 + 0.4);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float backLit = pow(saturate(dot(-viewDir, mainLight.direction)), 4.0) * _Translucency * h;
                float shadow = mainLight.shadowAttenuation;

                float3 color = albedo * mainLight.color * (ndl + backLit) * shadow;
                color += albedo * SampleSH(float3(0, 1, 0)) * lerp(0.55, 1.0, h);
                color = MixFog(color, i.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
