Shader "Custom/CharacterSurface"
{
    // Procedural surface detail for characters without textures. Patterns are evaluated in the mesh's rest pose
    // (vertex positions baked into UV3 in metres by CharacterModelSetupTool), so they stick to the skin while it
    // animates. Primitive attachments (no UV3) use object space instead (_ObjectSpace = 1).
    // _Pattern: 0 skin, 1 woven cloth, 2 leather, 3 metal, 4 hair, 5 eyes, 6 fur, 7 chain mail, 8 plaster / stone.
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.7, 0.7, 0.7, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3
        _Pattern ("Pattern", Float) = 0
        _PatternScale ("Pattern Scale", Float) = 1
        _DetailStrength ("Detail Strength", Range(0, 2)) = 1
        _ObjectSpace ("Use Object Space (primitives)", Float) = 0
        _UseVertexColor ("Multiply By Vertex Color", Float) = 0
        _VertexSurface ("Per-Vertex Surface (colour, uv2 = pattern, metallic, smoothness)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _Metallic, _Smoothness, _Pattern, _PatternScale, _DetailStrength, _ObjectSpace, _UseVertexColor, _VertexSurface;
        CBUFFER_END

        float hash31(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
        float noise3D(float3 x)
        {
            float3 i = floor(x), f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(hash31(i), hash31(i + float3(1, 0, 0)), f.x), lerp(hash31(i + float3(0, 1, 0)), hash31(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(hash31(i + float3(0, 0, 1)), hash31(i + float3(1, 0, 1)), f.x), lerp(hash31(i + float3(0, 1, 1)), hash31(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }

        // Returns albedo multiplier (x), height for micro relief (y), smoothness multiplier (z), occlusion (w).
        float4 PatternSample(float3 p, float pattern, float3 nrm)
        {
            float n1 = noise3D(p * 6.0), n2 = noise3D(p * 23.0 + 7.1), n3 = noise3D(p * 70.0 + 3.3);
            if (pattern < 0.5) // skin: soft mottling, pores up close
                return float4(lerp(0.93, 1.05, n1) * lerp(0.97, 1.02, n3), n3 * 0.15, lerp(0.8, 1.1, n2), 1.0);
            if (pattern < 1.5) // woven cloth: warp/weft threads + fibres + fold shading
            {
                float k = 210.0;
                float warp = sin(p.x * k + n2 * 2.0) * 0.5 + 0.5, weft = sin(p.y * k + n2 * 2.0) * 0.5 + 0.5;
                float weave = lerp(warp, weft, step(0.5, frac((floor(p.x * k / 6.283) + floor(p.y * k / 6.283)) * 0.5)));
                float folds = noise3D(p * float3(4.0, 1.5, 4.0));
                float slub = noise3D(p * float3(40.0, 300.0, 40.0)); // uneven thread thickness
                return float4(lerp(0.93, 1.03, weave) * lerp(0.94, 1.04, slub) * lerp(0.86, 1.06, folds) * lerp(0.96, 1.02, n3), weave * 0.35, 0.8, lerp(0.85, 1.0, folds));
            }
            if (pattern < 2.5) // leather: grain, creases, worn lighter patches
            {
                float grain = noise3D(p * 120.0);
                float crease = smoothstep(0.82, 0.97, 1.0 - abs(noise3D(p * float3(9.0, 28.0, 9.0)) * 2.0 - 1.0));
                float wear = smoothstep(0.55, 0.8, n1);
                return float4(lerp(0.88, 1.05, grain) * (1.0 - crease * 0.35) * lerp(1.0, 1.22, wear), grain * 0.6 + crease, lerp(0.7, 1.3, wear), 1.0 - crease * 0.4);
            }
            if (pattern < 3.5) // metal: brushed streaks, scratches, dark patina in recesses
            {
                float brushed = noise3D(p * float3(4.0, 160.0, 4.0));
                float scratch = smoothstep(0.93, 0.99, 1.0 - abs(noise3D(p * float3(60.0, 8.0, 60.0)) * 2.0 - 1.0));
                float patina = smoothstep(0.55, 0.85, n1);
                return float4(lerp(0.88, 1.06, brushed) * lerp(1.0, 0.72, patina) + scratch * 0.25, scratch * 0.4, lerp(1.0, 0.55, patina) * lerp(0.9, 1.05, brushed), lerp(1.0, 0.8, patina));
            }
            if (pattern < 4.5) // hair: strands along the length + clumps
            {
                float strands = noise3D(p * float3(220.0, 12.0, 220.0));
                float clumps = noise3D(p * float3(30.0, 6.0, 30.0));
                return float4(lerp(0.7, 1.15, strands) * lerp(0.85, 1.05, clumps), strands, lerp(0.8, 1.4, strands), lerp(0.8, 1.0, clumps));
            }
            if (pattern < 5.5) return float4(1, 0, 1.6, 1); // eyes: glossy
            if (pattern > 7.5) // plaster / stone: soft blotches, fine grit and pits, no directional lines
            {
                float grit = noise3D(p * 140.0 + 1.7);
                float pits = smoothstep(0.78, 0.92, noise3D(p * 55.0 + 9.2));
                return float4(lerp(0.88, 1.06, n1) * lerp(0.95, 1.04, n2) * lerp(0.94, 1.03, grit) * (1.0 - pits * 0.18), grit * 0.35 - pits * 0.6, lerp(0.8, 1.1, n2), 1.0 - pits * 0.25);
            }
            if (pattern > 6.5) // chain mail: staggered rows of rings in the plane facing the (rest) normal
            {
                float2 q = abs(nrm.x) > abs(nrm.z) ? p.zy : p.xy;
                float2 g = q / float2(0.0105, 0.0085);
                g.x += 0.5 * fmod(floor(g.y) + 1000.0, 2.0);
                float2 c = frac(g) - 0.5;
                float d = length(c * float2(1.0, 0.82));
                float ring = smoothstep(0.16, 0.27, d) * (1.0 - smoothstep(0.40, 0.55, d));
                float fade = saturate(1.5 - 1.5 * max(fwidth(g.x), fwidth(g.y)));
                ring = lerp(0.62, ring, fade);
                return float4(lerp(0.45, 1.12, ring) * lerp(0.9, 1.05, n2), ring * 0.8, lerp(0.55, 1.15, ring), lerp(0.7, 1.0, ring));
            }
            // fur: fine strands lying along the body (rest Z), clumps and soft tone variation
            float strands = noise3D(p * float3(260.0, 260.0, 26.0));
            float clump = noise3D(p * float3(40.0, 40.0, 9.0));
            return float4(lerp(0.78, 1.12, strands) * lerp(0.88, 1.06, clump) * lerp(0.94, 1.04, n1), strands * 0.8, lerp(0.7, 1.2, strands), lerp(0.8, 1.0, clump));
        }

        float3 RestPosition(float3 uv3, float3 positionOS)
        {
            float3 objectScale = float3(length(unity_ObjectToWorld._m00_m10_m20), length(unity_ObjectToWorld._m01_m11_m21), length(unity_ObjectToWorld._m02_m12_m22));
            return (_ObjectSpace > 0.5 ? positionOS * objectScale : uv3) * _PatternScale;
        }

        // ---- GPU skinning for distant characters (GpuSkinnedCharacter). _BoneTex holds, per baked frame (row), the
        // skin matrix (bone * bindpose, in the renderer's space) of every bone as 3 texels, then the upper-body anchor
        // bone's matrix and its inverse; the last row holds the upper-body mask. Weights and bone indices come in UV4 / UV5.
        #if defined(_GPU_SKIN)
            TEXTURE2D(_BoneTex);
            float4 _BoneTexInfo; // x anchor column, y mask row

            UNITY_INSTANCING_BUFFER_START(GpuSkinProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Loco)     // first row, frames (negative: holds the last frame), fps, start time
                UNITY_DEFINE_INSTANCED_PROP(float4, _LocoPrev) // the clip fading out
                UNITY_DEFINE_INSTANCED_PROP(float4, _Swing)    // upper-body attack like _Loco; start time < 0: none
                UNITY_DEFINE_INSTANCED_PROP(float4, _Fade)     // fade start time, fade duration
            UNITY_INSTANCING_BUFFER_END(GpuSkinProps)

            float3x4 BoneLoad(int column, int row)
            {
                return float3x4(LOAD_TEXTURE2D_LOD(_BoneTex, int2(column, row), 0),
                                LOAD_TEXTURE2D_LOD(_BoneTex, int2(column + 1, row), 0),
                                LOAD_TEXTURE2D_LOD(_BoneTex, int2(column + 2, row), 0));
            }

            // The two baked rows around the clip's current time and the blend between them
            void ClipRows(float4 clip, out int r0, out int r1, out float k)
            {
                float frames = abs(clip.y);
                float t = max(0.0, (_Time.y - clip.w) * clip.z);
                t = clip.y > 0.0 ? fmod(t, frames) : min(t, frames - 1.0);
                float f = floor(t);
                k = t - f;
                float next = f + 1.0;
                if (next >= frames) next = clip.y > 0.0 ? 0.0 : frames - 1.0;
                r0 = (int)(clip.x + f);
                r1 = (int)(clip.x + next);
            }

            float3x4 BoneAt(int column, int r0, int r1, float k) { return lerp(BoneLoad(column, r0), BoneLoad(column, r1), k); }

            float3x4 MulAffine(float3x4 a, float3x4 b)
            {
                float3x3 r = mul((float3x3)a, (float3x3)b);
                float3 t = mul((float3x3)a, float3(b._m03, b._m13, b._m23)) + float3(a._m03, a._m13, a._m23);
                return float3x4(float4(r[0], t.x), float4(r[1], t.y), float4(r[2], t.z));
            }

            void GpuSkin(inout float3 position, inout float3 normal, float4 weights, float4 bones)
            {
                float4 loco = UNITY_ACCESS_INSTANCED_PROP(GpuSkinProps, _Loco);
                float4 prev = UNITY_ACCESS_INSTANCED_PROP(GpuSkinProps, _LocoPrev);
                float4 swing = UNITY_ACCESS_INSTANCED_PROP(GpuSkinProps, _Swing);
                float4 fade = UNITY_ACCESS_INSTANCED_PROP(GpuSkinProps, _Fade);

                int a0, a1; float ak;
                ClipRows(loco, a0, a1, ak);
                float fadeK = fade.y > 0.0 ? saturate((_Time.y - fade.x) / fade.y) : 1.0;
                int p0 = 0, p1 = 0; float pk = 0.0;
                if (fadeK < 1.0) ClipRows(prev, p0, p1, pk);

                // Attack on the upper body (like the Animator's masked layer): the swing pose of the upper bones,
                // re-seated on the walking pose of the bone they hang from.
                float swingW = 0.0;
                int s0 = 0, s1 = 0; float sk = 0.0;
                float3x4 seat = (float3x4)0;
                if (swing.w >= 0.0)
                {
                    float t = _Time.y - swing.w, clipLength = abs(swing.y) / swing.z;
                    swingW = saturate(t * 12.0) * saturate(1.0 - (t - (clipLength - 0.18)) * 5.0);
                    if (swingW > 0.0)
                    {
                        ClipRows(swing, s0, s1, sk);
                        int anchor = (int)_BoneTexInfo.x;
                        seat = MulAffine(BoneAt(anchor, a0, a1, ak), BoneAt(anchor + 3, s0, s1, sk));
                    }
                }

                float3 p = 0.0, n = 0.0;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float w = weights[i];
                    if (w <= 0.0) continue;
                    int column = (int)bones[i] * 3;
                    float3x4 m = BoneAt(column, a0, a1, ak);
                    if (fadeK < 1.0) m = lerp(BoneAt(column, p0, p1, pk), m, fadeK);
                    if (swingW > 0.0)
                    {
                        float mask = LOAD_TEXTURE2D_LOD(_BoneTex, int2(column, (int)_BoneTexInfo.y), 0).x;
                        if (mask > 0.0) m = lerp(m, MulAffine(seat, BoneAt(column, s0, s1, sk)), swingW * mask);
                    }
                    p += w * mul(m, float4(position, 1.0));
                    n += w * mul((float3x3)m, normal);
                }
                position = p;
                normal = n;
            }

            #define GPU_SKIN_INPUT float4 skinWeights : TEXCOORD4; float4 skinBones : TEXCOORD5;
            #define GPU_SKIN(input) GpuSkin(input.positionOS.xyz, input.normalOS, input.skinWeights, input.skinBones)
        #else
            #define GPU_SKIN_INPUT
            #define GPU_SKIN(input)
        #endif
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _GPU_SKIN
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float3 uv2 : TEXCOORD2; float3 uv3 : TEXCOORD3; float4 color : COLOR; GPU_SKIN_INPUT UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 restPos : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                float3 surface : TEXCOORD4; // pattern, metallic, smoothness
                float4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input); GPU_SKIN(input);
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.restPos = RestPosition(input.uv3, input.positionOS.xyz);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.color = input.color;
                o.surface = _VertexSurface > 0.5 ? input.uv2 : float3(_Pattern, _Metallic, _Smoothness);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float detail = (1.0 - smoothstep(4.0, 25.0, distance(_WorldSpaceCameraPos.xyz, i.positionWS))) * _DetailStrength;
                float pattern = floor(i.surface.x + 0.5);
                float3 restN = TransformWorldToObjectDir(n);
                float4 pat = PatternSample(i.restPos, pattern, restN);

                // Micro relief: gradient of the pattern height (rest space ~ world orientation for upright characters)
                float e = 0.0025;
                float hx = PatternSample(i.restPos + float3(e, 0, 0), pattern, restN).y;
                float hy = PatternSample(i.restPos + float3(0, e, 0), pattern, restN).y;
                float hz = PatternSample(i.restPos + float3(0, 0, e), pattern, restN).y;
                float3 grad = float3(hx - pat.y, hy - pat.y, hz - pat.y) / e;
                grad = TransformObjectToWorldDir(grad);
                grad -= n * dot(grad, n);
                float3 nd = normalize(n - grad * 0.0012 * detail);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = nd;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(nd);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                float3 tint = (_UseVertexColor > 0.5 || _VertexSurface > 0.5) ? i.color.rgb : float3(1, 1, 1);
                s.albedo = _BaseColor.rgb * tint * lerp(1.0, pat.x, saturate(detail + 0.35));
                s.metallic = i.surface.y;
                s.smoothness = saturate(i.surface.z * pat.z);
                s.occlusion = pat.w;
                s.alpha = 1.0;
                s.normalTS = half3(0, 0, 1);

                half4 color = UniversalFragmentPBR(inputData, s);

                // Skin: soft wrap light so faces are not harshly shadowed (cheap subsurface)
                if (pattern < 0.5)
                {
                    Light l = GetMainLight(inputData.shadowCoord);
                    float wrap = saturate((dot(n, l.direction) + 0.45) / 1.45) - saturate(dot(n, l.direction));
                    color.rgb += s.albedo * float3(1.0, 0.45, 0.35) * wrap * 0.35 * l.color * l.shadowAttenuation;
                }

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _GPU_SKIN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; GPU_SKIN_INPUT UNITY_VERTEX_INPUT_INSTANCE_ID };
            float3 _LightDirection;
            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input); GPU_SKIN(input);
                float3 pws = TransformObjectToWorld(input.positionOS.xyz);
                return TransformWorldToHClip(ApplyShadowBias(pws, TransformObjectToWorldNormal(input.normalOS), _LightDirection));
            }
            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _GPU_SKIN
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; GPU_SKIN_INPUT UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 DepthVert(Attributes input) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(input); GPU_SKIN(input); return TransformObjectToHClip(input.positionOS.xyz); }
            half DepthFrag(float4 pos : SV_POSITION) : SV_Target { return pos.z; }
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
            #pragma multi_compile_local _ _GPU_SKIN
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; GPU_SKIN_INPUT UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings DNVert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input); GPU_SKIN(input);
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }
            half4 DNFrag(Varyings i) : SV_Target
            {
                float3 n = NormalizeNormalPerPixel(i.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(n) * 0.5 + 0.5)), 0.0);
                #else
                    return half4(n, 0.0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
