Shader "Custom/StylizedWater"
{
    Properties
    {
        _DeepColor ("Deep Water Color", Color) = (0.08, 0.28, 0.45, 0.9)
        _ShallowColor ("Shallow Water Color", Color) = (0.22, 0.65, 0.75, 0.75)
        _FoamColor ("Foam Color", Color) = (0.92, 0.98, 1.0, 0.95)
        _WaveSpeed ("Wave Speed", Float) = 1.2
        _WaveScale ("Wave Scale", Float) = 0.5
        _FoamDistance ("Foam Shore Distance", Float) = 0.4
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _ShallowColor;
                float4 _FoamColor;
                float _WaveSpeed;
                float _WaveScale;
                float _FoamDistance;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                
                // Subtle procedural vertex wave ripple
                float wave = sin(_Time.y * _WaveSpeed + worldPos.x * _WaveScale + worldPos.z * _WaveScale * 0.7) * 0.06;
                worldPos.y += wave;

                output.positionWS = worldPos;
                output.positionCS = TransformWorldToHClip(worldPos);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEyeZ = LinearEyeDepth(rawDepth, _ZBufferParams);
                float surfaceEyeZ = input.positionCS.w;
                float depthDiff = max(0.0, sceneEyeZ - surfaceEyeZ);

                // Water gradient based on depth
                float depthFactor = saturate(depthDiff * 0.8);
                half4 waterCol = lerp(_ShallowColor, _DeepColor, depthFactor);

                // Shore foam line
                float foamFactor = 1.0 - saturate(depthDiff / max(0.01, _FoamDistance));
                foamFactor = smoothstep(0.4, 0.9, foamFactor);

                half4 finalColor = lerp(waterCol, _FoamColor, foamFactor);

                // Specular sun highlight
                Light mainLight = GetMainLight();
                float3 viewDir = normalize(GetCameraPositionWS() - input.positionWS);
                float3 halfDir = normalize(mainLight.direction + viewDir);
                float spec = pow(saturate(dot(input.normalWS, halfDir)), 64.0);
                finalColor.rgb += mainLight.color * spec * 0.8;

                finalColor.rgb = MixFog(finalColor.rgb, input.fogFactor);
                return finalColor;
            }
            ENDHLSL
        }
    }
}
