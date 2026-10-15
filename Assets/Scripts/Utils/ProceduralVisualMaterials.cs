using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// AAA Stylized Procedural Material Library with crisp Normal Maps, PBR Roughness/Metallic,
    /// and support for dynamic Wind Foliage shaders.
    /// </summary>
    public static class ProceduralVisualMaterials
    {
        public enum Surface
        {
            Cloth,
            Leather,
            Metal,
            Wood,
            Bark,
            Foliage,
            Stone,
            Ore,
            Grass,
            RoofTile,
            Gold,
            Dark,
            GemRuby,
            GemSapphire,
            Rope
        }

        private static readonly Dictionary<string, Material> CachedMaterials = new Dictionary<string, Material>();
        private static readonly Dictionary<Surface, (Texture2D albedo, Texture2D normal)> CachedTextures = new Dictionary<Surface, (Texture2D, Texture2D)>();
        private static Shader litShader;
        private static Shader windFoliageShader;
        private static Shader rockShader;

        public static void ClearCache()
        {
            CachedMaterials.Clear();
            CachedTextures.Clear();
        }

        public static Material Get(Surface surface, Color baseColor, float metallic = 0f, float smoothness = 0.35f)
        {
            string key = surface + "_" + ColorUtility.ToHtmlStringRGBA(baseColor) + "_" + metallic.ToString("F2") + "_" + smoothness.ToString("F2");
            if (CachedMaterials.TryGetValue(key, out Material cached) && cached != null)
            {
                return cached;
            }

            if (litShader == null)
            {
                litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            }
            if (windFoliageShader == null)
            {
                windFoliageShader = Shader.Find("Custom/StylizedWindFoliage");
            }
            if (rockShader == null)
            {
                rockShader = Shader.Find("Custom/ProceduralRock"); // UV-free procedural stone
            }

            Shader targetShader = (surface == Surface.Foliage || surface == Surface.Grass) && windFoliageShader != null
                ? windFoliageShader
                : (surface == Surface.Stone && rockShader != null ? rockShader : litShader);

            Material material = new Material(targetShader)
            {
                name = "MAT_" + key,
                enableInstancing = true
            };

            if (!CachedTextures.TryGetValue(surface, out var textures) || textures.albedo == null)
            {
                textures = BuildTextures(surface);
                CachedTextures[surface] = textures;
            }
            var albedoTex = textures.albedo;
            var normalTex = textures.normal;
            
            if (targetShader == windFoliageShader)
            {
                material.SetTexture("_BaseMap", albedoTex);
                material.SetColor("_BaseColor", baseColor);
                material.SetColor("_TopColor", Color.Lerp(baseColor, new Color(0.62f, 0.78f, 0.30f), 0.30f));
                material.SetColor("_BottomColor", baseColor * 0.45f);
                material.SetFloat("_Translucency", 0.55f);
                material.SetFloat("_WindSpeed", 1.6f);
                material.SetFloat("_WindStrength", surface == Surface.Grass ? 0.18f : 0.08f);
                material.SetFloat("_WindFrequency", 0.6f);
            }
            else
            {
                material.mainTexture = albedoTex;
                material.SetTexture("_BaseMap", albedoTex);
                material.SetColor("_BaseColor", baseColor);
                if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
                if (normalTex != null)
                {
                    material.EnableKeyword("_NORMALMAP");
                    material.SetTexture("_BumpMap", normalTex);
                    material.SetFloat("_BumpScale", surface == Surface.Stone ? 1.35f : surface == Surface.Wood || surface == Surface.Bark ? 0.45f : 0.8f);
                }
                material.SetFloat("_Metallic", metallic);
                material.SetFloat("_Smoothness", smoothness);
                material.SetFloat("_Glossiness", smoothness);

                if (surface == Surface.Gold || surface == Surface.GemRuby || surface == Surface.GemSapphire || surface == Surface.Ore)
                {
                    material.EnableKeyword("_EMISSION");
                    Color emitCol = (surface == Surface.GemRuby) ? new Color(0.9f, 0.1f, 0.2f) * 0.8f :
                                    (surface == Surface.GemSapphire) ? new Color(0.1f, 0.5f, 1.0f) * 0.8f :
                                    (surface == Surface.Gold) ? new Color(0.8f, 0.55f, 0.1f) * 0.25f :
                                    baseColor * 0.3f;
                    material.SetColor("_EmissionColor", emitCol);
                }
            }

            CachedMaterials[key] = material;
            return material;
        }

        private static (Texture2D albedo, Texture2D normal) BuildTextures(Surface surface)
        {
            const int size = 128;
            Texture2D albedo = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "TEX_Albedo_" + surface,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8
            };

            float[,] heights = new float[size, size];
            Color[] pixels = new Color[size * size];
            int seed = ((int)surface + 1) * 317;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;
                    float nLarge = Mathf.PerlinNoise(u * 4f + seed, v * 4f + seed * 0.5f);
                    float nMed = Mathf.PerlinNoise(u * 12f + seed * 1.3f, v * 12f + seed * 0.7f);
                    float nFine = Mathf.PerlinNoise(u * 32f + seed * 2.1f, v * 32f + seed * 1.9f);

                    float shade = Mathf.Lerp(0.88f, 1.12f, nLarge) + (nMed - 0.5f) * 0.12f + (nFine - 0.5f) * 0.06f;
                    float height = nLarge * 0.5f + nMed * 0.35f + nFine * 0.15f;

                    switch (surface)
                    {
                        case Surface.Cloth:
                            float weave = Mathf.Sin(u * Mathf.PI * 64f) * Mathf.Sin(v * Mathf.PI * 64f) * 0.08f;
                            shade += weave;
                            height += weave;
                            break;
                        case Surface.Leather:
                            float grain = Mathf.Sin(v * Mathf.PI * 24f + nLarge * 5f) * 0.08f;
                            shade += grain;
                            height += grain;
                            break;
                        case Surface.Wood:
                            float woodRing = Mathf.Sin(v * Mathf.PI * 18f + nLarge * 8f + u * 2f) * 0.045f + (nFine - 0.5f) * 0.05f; // subtle grain: stretched over big primitives strong rings read as zebra stripes
                            shade += woodRing;
                            height += woodRing * 0.6f;
                            break;
                        case Surface.Bark:
                            float furrow = Mathf.Abs(Mathf.Sin(v * Mathf.PI * 10f + nLarge * 6f)) * 0.12f;
                            shade -= furrow * 0.5f;
                            height += furrow * 2.0f;
                            break;
                        case Surface.Metal:
                            float brush = Mathf.Sin(u * Mathf.PI * 48f) * 0.04f;
                            shade += brush;
                            height += brush * 0.5f;
                            break;
                        case Surface.Stone:
                            float facet = (nMed > 0.65f ? 0.15f : 0f) - (nFine > 0.7f ? 0.12f : 0f);
                            shade += facet;
                            height += facet * 2.0f;
                            break;
                        case Surface.Rope:
                            float strand = Mathf.Sin((u + v * 3f) * Mathf.PI * 16f) * 0.18f;
                            shade += strand;
                            height += strand * 2.0f;
                            break;
                        case Surface.RoofTile:
                            float row = Mathf.Repeat(v * 8f, 1f);
                            float brick = Mathf.Repeat(u * 6f + Mathf.Floor(v * 8f) * 0.5f, 1f);
                            if (row < 0.08f || brick < 0.06f) { shade *= 0.65f; height = 0f; }
                            break;
                    }

                    heights[x, y] = height;
                    float val = Mathf.Clamp(shade, 0.45f, 1.45f);
                    Color pix = new Color(val, val, val, 1f);
                    pixels[y * size + x] = pix;
                }
            }

            albedo.SetPixels(pixels);
            albedo.Apply(true, true);

            // Generate Normal Map via Sobel derivative
            Texture2D normal = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "TEX_Normal_" + surface,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8
            };

            Color[] normPixels = new Color[size * size];
            float bumpFactor = 3.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int xL = (x - 1 + size) % size;
                    int xR = (x + 1) % size;
                    int yD = (y - 1 + size) % size;
                    int yU = (y + 1) % size;

                    float dX = (heights[xR, y] - heights[xL, y]) * bumpFactor;
                    float dY = (heights[x, yU] - heights[x, yD]) * bumpFactor;

                    Vector3 n = new Vector3(-dX, -dY, 1f).normalized;
                    normPixels[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }

            normal.SetPixels(normPixels);
            normal.Apply(true, true);

            return (albedo, normal);
        }
    }
}
