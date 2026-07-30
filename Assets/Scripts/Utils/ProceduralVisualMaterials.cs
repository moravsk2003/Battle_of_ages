using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Shared, lightweight material library for the procedural world models.
    /// The textures are generated once and reused, which keeps the art readable
    /// without adding runtime material instances for every object in the scene.
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
            Dark
        }

        private static readonly Dictionary<string, Material> CachedMaterials = new Dictionary<string, Material>();
        private static Shader litShader;

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

            Material material = new Material(litShader)
            {
                name = "MAT_" + key,
                color = Color.white,
                enableInstancing = true
            };

            Texture2D texture = BuildTexture(surface, baseColor);
            material.mainTexture = texture;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Glossiness", smoothness);
            CachedMaterials[key] = material;
            return material;
        }

        private static Texture2D BuildTexture(Surface surface, Color baseColor)
        {
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "TEX_" + surface,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };

            Color[] pixels = new Color[size * size];
            int seed = ((int)surface + 1) * 173;
            float hueVariation = surface == Surface.Metal || surface == Surface.Gold ? 0.055f : 0.12f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;
                    float largeNoise = Mathf.PerlinNoise(u * 3.5f + seed, v * 3.5f + seed * 0.37f);
                    float detailNoise = Mathf.PerlinNoise(u * 19f + seed * 0.61f, v * 19f + seed * 0.19f);
                    float shade = Mathf.Lerp(0.83f, 1.12f, largeNoise) + (detailNoise - 0.5f) * hueVariation;

                    switch (surface)
                    {
                        case Surface.Cloth:
                            shade += (Mathf.Sin(u * Mathf.PI * 64f) + Mathf.Sin(v * Mathf.PI * 64f)) * 0.018f;
                            break;
                        case Surface.Leather:
                        case Surface.Bark:
                            shade += Mathf.Sin(v * Mathf.PI * 18f + largeNoise * 4f) * 0.075f;
                            break;
                        case Surface.Wood:
                            shade += Mathf.Sin(v * Mathf.PI * 12f + largeNoise * 7f) * 0.1f;
                            break;
                        case Surface.Metal:
                            shade += Mathf.Sin(u * Mathf.PI * 34f) * 0.035f;
                            break;
                        case Surface.RoofTile:
                            float row = Mathf.Repeat(v * 7f, 1f);
                            float brick = Mathf.Repeat(u * 6f + Mathf.Floor(v * 7f) * 0.5f, 1f);
                            shade -= (row < 0.07f || brick < 0.045f) ? 0.2f : 0f;
                            break;
                        case Surface.Stone:
                        case Surface.Ore:
                            shade += (detailNoise > 0.72f ? -0.12f : 0f);
                            break;
                        case Surface.Grass:
                        case Surface.Foliage:
                            shade += Mathf.Sin((u + v) * Mathf.PI * 20f) * 0.025f;
                            break;
                    }

                    Color pixel = baseColor * Mathf.Clamp(shade, 0.55f, 1.28f);
                    if (surface == Surface.Ore && detailNoise > 0.76f)
                    {
                        pixel = Color.Lerp(pixel, new Color(1f, 0.76f, 0.23f), 0.35f);
                    }

                    pixels[y * size + x] = pixel;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }
    }
}
