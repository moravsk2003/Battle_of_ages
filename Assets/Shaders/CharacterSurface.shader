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
        _Weathering ("Weathering (props and buildings: sun, rain, moss, mud)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _Metallic, _Smoothness, _Pattern, _PatternScale, _DetailStrength, _ObjectSpace, _UseVertexColor, _VertexSurface, _Weathering;
        CBUFFER_END
        float _SurfaceDetailCut; // global, set by the graphics preset: 0 all fine detail, 1 none

        float hash31(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
        float noise3D(float3 x)
        {
            float3 i = floor(x), f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(hash31(i), hash31(i + float3(1, 0, 0)), f.x), lerp(hash31(i + float3(0, 1, 0)), hash31(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(hash31(i + float3(0, 0, 1)), hash31(i + float3(1, 0, 1)), f.x), lerp(hash31(i + float3(0, 1, 1)), hash31(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }

        // What a pattern gives a point: albedo multiplier (tinted), height in metres (relief: bumps the normal), smoothness
        // multiplier, occlusion. aa = the pixel's footprint in metres: detail finer than it fades to its mean (no shimmer).
        struct Pat { float3 albedo; float height; float smooth; float occ; };

        float Fade(float aa, float period) { return saturate(1.5 - 1.5 * aa / (period * 0.5)); }

        // The two coordinates across a surface facing the rest normal's main axis (woven or pebbled things)
        float2 Across(float3 p, float3 nrm)
        {
            float3 a = abs(nrm);
            return a.x > a.y && a.x > a.z ? p.zy : (a.y > a.z ? p.xz : p.xy);
        }

        // Distance to the nearest of jittered points on a grid (F1): pebbles, cells
        float Cells(float2 q)
        {
            float2 i = floor(q), f = frac(q);
            float d = 8.0;
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                float2 o = float2(x, y);
                float2 h = float2(hash31(float3(i + o, 1.7)), hash31(float3(i + o, 5.3)));
                d = min(d, length(o + h * 0.8 + 0.1 - f));
            }
            return d;
        }

        // A thin bright line where a noise crosses its middle: scratches, creases
        float Ridge(float v, float width) { return 1.0 - smoothstep(0.0, width, abs(v * 2.0 - 1.0)); }

        Pat PatternSample(float3 p, float pattern, float3 nrm, float aa)
        {
            Pat o;
            o.albedo = 1.0; o.height = 0.0; o.smooth = 1.0; o.occ = 1.0;
            float n1 = noise3D(p * 6.0), n2 = noise3D(p * 23.0 + 7.1);
            if (pattern < 0.5) // skin: blotches and flush, fine wrinkles, pores up close
            {
                float blotch = noise3D(p * 18.0 + 2.3), fine = noise3D(p * 70.0 + 3.3);
                o.albedo = lerp(float3(1.04, 0.96, 0.95), float3(0.97, 1.0, 1.01), blotch) * lerp(0.95, 1.03, fine) * lerp(0.96, 1.03, n1);
                float pores = noise3D(p * 1100.0) * Fade(aa, 0.0009);
                o.height = (fine - 0.5) * 0.0002 - pores * 0.00004;
                o.smooth = lerp(0.75, 1.2, n2);
                return o;
            }
            if (pattern < 1.5) // woven cloth: threads, uneven yarn, dye and fading, grime low down, soft folds and crinkles
            {
                float2 q = Across(p, nrm) * 2500.0; // ~2.5 mm homespun threads
                float warp = sin(q.x) * 0.5 + 0.5, weft = sin(q.y) * 0.5 + 0.5;
                float weave = lerp(warp, weft, step(0.5, frac((floor(q.x / 6.283) + floor(q.y / 6.283)) * 0.5)));
                float near = Fade(aa, 0.0025);
                weave = lerp(0.5, weave, near);
                float slub = noise3D(p * float3(70.0, 700.0, 70.0)) * Fade(aa, 0.003);
                float dye = noise3D(p * 2.5 + 11.0), dye2 = noise3D(p * 11.0 + 4.0);
                float folds = noise3D(p * float3(7.0, 2.6, 7.0)), crinkle = noise3D(p * 45.0 + 1.3);
                float grime = saturate((0.8 - p.y) / 0.7) * lerp(0.5, 1.0, n2); // dust and mud towards the hem
                o.albedo = lerp(0.86, 1.06, dye) * lerp(0.94, 1.04, dye2) * lerp(0.84, 1.06, weave) * lerp(0.95, 1.04, slub)
                         * lerp(float3(1, 1, 1), float3(0.82, 0.76, 0.68), grime * 0.45);
                o.height = (folds - 0.5) * 0.012 + (crinkle - 0.5) * 0.0012 + (weave - 0.5) * 0.0002 + slub * 0.00015;
                o.smooth = lerp(0.7, 1.0, weave) * lerp(1.0, 0.7, grime);
                o.occ = lerp(0.8, 1.0, folds) * lerp(0.9, 1.0, weave);
                return o;
            }
            if (pattern < 2.5) // leather: pebbled grain, creases, scuffed lighter patches, darker oiled ones
            {
                float2 q = Across(p, nrm);
                float pebble = Cells(q * 420.0) * Fade(aa, 0.0025);
                float crease = Ridge(noise3D(p * float3(30.0, 90.0, 30.0)), 0.1) * smoothstep(0.55, 0.75, noise3D(p * 12.0 + 8.0)); // short, in patches
                float wear = smoothstep(0.55, 0.8, n1), oil = smoothstep(0.5, 0.8, noise3D(p * 14.0 + 6.0));
                o.albedo = lerp(0.88, 1.05, noise3D(p * 40.0)) * lerp(0.92, 1.04, n2) * (1.0 - crease * 0.25) * lerp(1.0, 1.25, wear) * lerp(1.0, 0.85, oil);
                o.height = pebble * 0.00018 - crease * 0.0003 + (n2 - 0.5) * 0.0015;
                o.smooth = lerp(0.75, 1.25, oil) * lerp(1.0, 0.75, wear) * lerp(0.85, 1.05, pebble);
                o.occ = 1.0 - crease * 0.4;
                return o;
            }
            if (pattern < 3.5) // metal: hammer marks, brushing, scratches, dull grime and rust spots
            {
                float dents = noise3D(p * 38.0 + 2.0), dents2 = noise3D(p * 90.0);
                float brushed = noise3D(p * float3(500.0, 6.0, 500.0)) * Fade(aa, 0.003) + 0.5 * (1.0 - Fade(aa, 0.003));
                float scratch = Ridge(noise3D(p * float3(70.0, 9.0, 70.0) + 5.0), 0.05) * Fade(aa, 0.003);
                float scratch2 = Ridge(noise3D(p * float3(12.0, 55.0, 30.0) + 9.0), 0.04) * Fade(aa, 0.003);
                float grime = smoothstep(0.5, 0.85, n1), rust = smoothstep(0.72, 0.9, noise3D(p * 19.0 + 3.0)) * grime;
                o.albedo = lerp(0.95, 1.03, brushed) * lerp(1.0, 0.7, grime) * lerp(float3(1, 1, 1), float3(0.95, 0.62, 0.42), rust * 0.6)
                         + (scratch + scratch2) * 0.22;
                o.height = (dents - 0.5) * 0.0006 + (dents2 - 0.5) * 0.00012 - (scratch + scratch2) * 0.00004;
                o.smooth = lerp(1.0, 0.5, grime) * lerp(0.9, 1.05, brushed) * lerp(1.0, 0.6, rust) * (1.0 - (scratch + scratch2) * 0.3);
                o.occ = lerp(1.0, 0.82, grime);
                return o;
            }
            if (pattern < 4.5) // hair and wood: fibres along the length, clumps and growth bands
            {
                float strands = noise3D(p * float3(260.0, 14.0, 260.0)), clumps = noise3D(p * float3(30.0, 5.0, 30.0));
                float rings = sin((p.x + p.z) * 160.0 + clumps * 6.0) * 0.5 + 0.5;
                o.albedo = lerp(0.72, 1.12, strands) * lerp(0.85, 1.05, clumps) * lerp(0.94, 1.03, rings);
                o.height = (strands - 0.5) * 0.0002 * Fade(aa, 0.004) + (clumps - 0.5) * 0.001;
                o.smooth = lerp(0.8, 1.3, strands);
                o.occ = lerp(0.8, 1.0, clumps);
                return o;
            }
            if (pattern < 5.5) { o.smooth = 1.6; return o; } // eyes: glossy
            if (pattern > 7.5) // plaster / stone: blotches, grit and pits, lime wash worn off in places
            {
                float grit = noise3D(p * 140.0 + 1.7) * Fade(aa, 0.008);
                float pits = smoothstep(0.78, 0.92, noise3D(p * 55.0 + 9.2));
                float stain = noise3D(p * float3(3.0, 0.8, 3.0) + 2.0);
                o.albedo = lerp(0.86, 1.06, n1) * lerp(0.95, 1.04, n2) * lerp(0.94, 1.03, grit) * (1.0 - pits * 0.22)
                         * lerp(float3(1, 1, 1), float3(0.86, 0.84, 0.78), saturate(stain * 1.4 - 0.5));
                o.height = grit * 0.0003 - pits * 0.0012 + (n2 - 0.5) * 0.002;
                o.smooth = lerp(0.8, 1.1, n2);
                o.occ = 1.0 - pits * 0.3;
                return o;
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
                float grime = smoothstep(0.5, 0.85, n1);
                o.albedo = lerp(0.45, 1.12, ring) * lerp(0.9, 1.05, n2) * lerp(1.0, 0.75, grime);
                o.height = ring * 0.0012;
                o.smooth = lerp(0.55, 1.15, ring) * lerp(1.0, 0.6, grime);
                o.occ = lerp(0.65, 1.0, ring);
                return o;
            }
            // fur: fine strands lying along the body (rest Z), clumps and soft tone variation
            float fs = noise3D(p * float3(260.0, 260.0, 26.0)), fc = noise3D(p * float3(40.0, 40.0, 9.0));
            o.albedo = lerp(0.78, 1.12, fs) * lerp(0.88, 1.06, fc) * lerp(0.94, 1.04, n1);
            o.height = (fs - 0.5) * 0.0003 * Fade(aa, 0.004) + (fc - 0.5) * 0.0015;
            o.smooth = lerp(0.7, 1.2, fs);
            o.occ = lerp(0.8, 1.0, fc);
            return o;
        }

        // The surface's normal tilted by a height field (metres) through the screen-space derivatives: a normal map with no
        // map or tangents (Mikkelsen's surface gradient); one pattern sample per pixel
        float3 BumpNormal(float3 n, float3 pos, float h)
        {
            float3 dpdx = ddx(pos), dpdy = ddy(pos);
            float dhdx = ddx(h), dhdy = ddy(h);
            float3 r1 = cross(dpdy, n), r2 = cross(n, dpdx);
            float det = dot(dpdx, r1);
            float3 grad = sign(det) * (dhdx * r1 + dhdy * r2);
            return normalize(abs(det) * n - grad);
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
                float detail = (1.0 - smoothstep(4.0, 25.0, distance(_WorldSpaceCameraPos.xyz, i.positionWS))) * _DetailStrength * (1.0 - _SurfaceDetailCut);
                float pattern = floor(i.surface.x + 0.5);
                float3 restN = TransformWorldToObjectDir(n);
                float aa = length(fwidth(i.restPos));
                Pat pat = PatternSample(i.restPos, pattern, restN, aa);
                float3 nd = BumpNormal(n, i.positionWS, pat.height * detail);

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
                s.albedo = _BaseColor.rgb * tint * lerp(1.0, pat.albedo, saturate(detail + 0.5));
                s.metallic = i.surface.y;
                s.smoothness = saturate(i.surface.z * lerp(1.0, pat.smooth, saturate(detail + 0.5)));
                s.occlusion = pat.occ;

                // Props and buildings stand out in the weather: wood greys where the sun and rain reach it and shows knots,
                // stone and plaster grow moss on top and stay damp low down, iron rusts, and mud splashes the bottom
                if (_Weathering > 0.5)
                {
                    float3 p = i.restPos;
                    float up = saturate(n.y), low = saturate(1.0 - p.y / 0.45);
                    float w1 = noise3D(p * 3.0 + 4.0), w2 = noise3D(p * 11.0 + 1.0);
                    float lum = dot(s.albedo, float3(0.3, 0.59, 0.11));
                    if (pattern > 3.5 && pattern < 4.5) // wood
                    {
                        float silver = saturate(0.25 + up * 0.45) * smoothstep(0.3, 0.75, w1);
                        s.albedo = lerp(s.albedo, float3(0.62, 0.6, 0.56) * (lum * 1.6 + 0.12), silver * 0.55);
                        float knot = 1.0 - smoothstep(0.03, 0.09, length(frac(p.xz * float2(2.3, 1.7) + noise3D(p * 2.0) * 0.6) - 0.5) + abs(frac(p.y * 1.3) - 0.5) * 0.2);
                        s.albedo *= 1.0 - knot * 0.45 * detail;
                        s.smoothness *= lerp(1.0, 0.6, silver);
                    }
                    else if (pattern > 7.5) // stone, plaster
                    {
                        float moss = smoothstep(0.55, 0.8, w1 * 0.6 + w2 * 0.4) * saturate(up * 1.5 + low * 0.8);
                        s.albedo = lerp(s.albedo, float3(0.28, 0.34, 0.16) * lerp(0.8, 1.2, w2), moss * 0.6);
                        s.albedo *= lerp(1.0, 0.78, low * smoothstep(0.3, 0.7, w2)); // damp
                    }
                    else if (pattern > 2.5 && pattern < 3.5) // iron
                    {
                        float rust = smoothstep(0.45, 0.75, w1 * 0.5 + w2 * 0.5);
                        s.albedo = lerp(s.albedo, float3(0.36, 0.2, 0.11) * lerp(0.7, 1.2, w2), rust * 0.55);
                        s.metallic *= 1.0 - rust * 0.7;
                        s.smoothness *= 1.0 - rust * 0.6;
                    }
                    float mud = low * low * smoothstep(0.35, 0.65, w2);
                    s.albedo = lerp(s.albedo, float3(0.3, 0.24, 0.17), mud * 0.5);
                }
                s.alpha = 1.0;
                s.normalTS = half3(0, 0, 1);

                half4 color = UniversalFragmentPBR(inputData, s);

                // Metal: the sky and the ground in it (the procedural sky gives no reflection probe: without this steel looked matte)
                if (s.metallic > 0.3)
                {
                    float3 r = reflect(-inputData.viewDirectionWS, nd);
                    float3 skyLight = SampleSH(float3(0, 1, 0));
                    float lum = dot(skyLight, float3(0.3, 0.5, 0.2));
                    float3 horizon = float3(0.80, 0.84, 0.88), zenith = float3(0.36, 0.52, 0.78), ground = float3(0.22, 0.24, 0.18);
                    float3 env = lerp(ground, lerp(horizon, zenith, saturate(r.y * 1.6)), smoothstep(-0.04, 0.06, r.y)) * lum * 1.6;
                    float fres = lerp(0.55, 1.0, pow(1.0 - saturate(dot(nd, inputData.viewDirectionWS)), 4.0));
                    color.rgb += env * s.albedo * s.metallic * s.smoothness * fres * s.occlusion * 0.7;
                }

                // Cloth: a soft sheen of fibres at grazing angles
                if (pattern > 0.5 && pattern < 1.5)
                {
                    float rim = pow(1.0 - saturate(dot(nd, inputData.viewDirectionWS)), 3.0);
                    color.rgb += s.albedo * rim * 0.18 * (SampleSH(nd) + GetMainLight().color * 0.3);
                }

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
