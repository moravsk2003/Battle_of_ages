Shader "Custom/AtmosphericSky"
{
    // Procedural skybox: zenith-to-horizon gradient, sun disk with halo and drifting fbm clouds.
    // The horizon colour is meant to match RenderSettings.fogColor so distant terrain melts into the sky.
    Properties
    {
        _ZenithColor ("Zenith Color", Color) = (0.30, 0.50, 0.80, 1)
        _HorizonColor ("Horizon Color", Color) = (0.66, 0.73, 0.80, 1)
        _GroundColor ("Below Horizon Color", Color) = (0.55, 0.60, 0.64, 1)
        _HorizonSharpness ("Horizon Sharpness", Range(0.5, 8)) = 2.6
        _SunColor ("Sun Color", Color) = (1.0, 0.92, 0.78, 1)
        _SunSize ("Sun Disk Size", Range(0.995, 0.99999)) = 0.9993
        _SunHaloStrength ("Sun Halo Strength", Range(0, 2)) = 0.45
        _CloudColor ("Cloud Lit Color", Color) = (0.97, 0.96, 0.94, 1)
        _CloudShadowColor ("Cloud Shadow Color", Color) = (0.56, 0.60, 0.68, 1)
        _CloudCoverage ("Cloud Coverage", Range(0, 1)) = 0.52
        _CloudSoftness ("Cloud Softness", Range(0.01, 0.5)) = 0.18
        _CloudScale ("Cloud Scale", Float) = 1.6
        _CloudSpeed ("Cloud Speed", Float) = 0.006
        _Exposure ("Exposure", Range(0.2, 3)) = 1.0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor, _HorizonColor, _GroundColor, _SunColor, _CloudColor, _CloudShadowColor;
                float _HorizonSharpness, _SunSize, _SunHaloStrength, _CloudCoverage, _CloudSoftness, _CloudScale, _CloudSpeed, _Exposure;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }
            float fbm(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int k = 0; k < 6; k++) { s += a * vnoise(p); p = p * 2.03 + float2(17.1, 9.3); a *= 0.5; }
                return s;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float3 sunDir = normalize(_MainLightPosition.xyz);
                float up = dir.y;

                // Sky gradient (exponential towards the horizon) and haze below it
                float t = pow(saturate(up), 1.0 / _HorizonSharpness);
                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, t);
                col = lerp(col, _GroundColor.rgb, saturate(-up * 4.0));

                // Sun: halo (forward scattering) + disk
                float sd = saturate(dot(dir, sunDir));
                col += _SunColor.rgb * (pow(sd, 6.0) * 0.18 + pow(sd, 64.0) * 0.35) * _SunHaloStrength;
                col += _SunColor.rgb * smoothstep(_SunSize, _SunSize + 0.00025, sd) * 6.0;

                // Clouds on a virtual plane above the camera
                if (up > 0.0)
                {
                    float2 uv = dir.xz / (up + 0.12) * _CloudScale + _Time.y * _CloudSpeed * float2(1.0, 0.35);
                    float density = fbm(uv);
                    float cover = smoothstep(1.0 - _CloudCoverage, 1.0 - _CloudCoverage + _CloudSoftness, density);
                    float thickness = smoothstep(1.0 - _CloudCoverage, 1.05, density);
                    float sunLit = pow(sd, 3.0);
                    float3 cloud = lerp(_CloudColor.rgb, _CloudShadowColor.rgb, thickness * 0.85);
                    cloud += _SunColor.rgb * sunLit * (1.0 - thickness) * 0.35; // silver lining towards the sun
                    float horizonFade = smoothstep(0.0, 0.18, up);
                    col = lerp(col, cloud, cover * horizonFade * 0.95);
                }

                return half4(col * _Exposure, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
