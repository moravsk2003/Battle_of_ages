using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Mesh assembly: grids of points with normals averaged from the faces (repeated points = crisp creases),
    /// oriented outwards automatically; colours are sRGB in, linear out.
    /// </summary>
    public sealed class MeshKit
    {
        public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>(), S = new List<Vector3>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<int> T = new List<int>();

        private static Color Lin(Color c)
        {
            Color l = QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
            l.a = 1f;
            return l;
        }

        /// <summary>Flat convex polygon (fan from the first point), facing away from inside; colour per vertex position.</summary>
        public void Polygon(IList<Vector3> pts, Vector3 inside, Vector3 surface, Func<Vector3, Color> color)
        {
            Vector3 centroid = Vector3.zero;
            foreach (Vector3 q in pts) centroid += q;
            centroid /= pts.Count;
            Vector3 n = Vector3.zero;
            for (int i = 0; i < pts.Count; i++) n += Vector3.Cross(pts[i] - centroid, pts[(i + 1) % pts.Count] - centroid);
            bool flip = Vector3.Dot(n, centroid - inside) < 0f;
            n = (flip ? -n : n).normalized;
            int start = V.Count;
            foreach (Vector3 q in pts) { V.Add(q); N.Add(n); C.Add(Lin(color(q))); S.Add(surface); }
            for (int i = 1; i < pts.Count - 1; i++) Tri(start, start + i, start + i + 1, flip);
        }

        /// <summary>
        /// Flat-shaded box with chamfered edges and corners (beams, planks, stones) in the frame m, centred, of the given
        /// size. jitter roughens it (stones: corner points moved by up to jitter, consistently, so it stays closed);
        /// color(local point) per vertex.
        /// </summary>
        public void Box(Matrix4x4 m, Vector3 size, float chamfer, Vector3 surface, Func<Vector3, Color> color, float jitter = 0f, int seed = 0)
        {
            Vector3 h = size * 0.5f;
            Matrix4x4 inv = m.inverse;
            float c = Mathf.Min(chamfer, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.9f);
            Vector3 J(Vector3 p)
            {
                if (jitter <= 0f) return p;
                Vector3 k = p * 7.3f + Vector3.one * (seed * 1.618f);
                return p + new Vector3(SdfCreature.Noise(k) - 0.5f, SdfCreature.Noise(k + Vector3.one * 17.1f) - 0.5f, SdfCreature.Noise(k + Vector3.one * 31.7f) - 0.5f) * (2f * jitter);
            }
            // Point on the face of axis ax (sign s), the other two coordinates at their inset (chamfered) extent with signs b, d
            Vector3 F(int ax, float s, float b, float d)
            {
                var p = new Vector3();
                p[ax] = s * h[ax];
                p[(ax + 1) % 3] = b * (h[(ax + 1) % 3] - c);
                p[(ax + 2) % 3] = d * (h[(ax + 2) % 3] - c);
                return p;
            }
            void Face(params Vector3[] local)
            {
                var w = new Vector3[local.Length];
                for (int i = 0; i < local.Length; i++) w[i] = m.MultiplyPoint3x4(J(local[i]));
                Polygon(w, m.MultiplyPoint3x4(Vector3.zero), surface, q => color(inv.MultiplyPoint3x4(q)));
            }
            float[] sg = { -1f, 1f };
            for (int ax = 0; ax < 3; ax++)
                foreach (float s in sg)
                {
                    Face(F(ax, s, -1, -1), F(ax, s, 1, -1), F(ax, s, 1, 1), F(ax, s, -1, 1));
                    if (c <= 0f) continue;
                    // Chamfer strip between this face and the next axis' faces
                    int a2 = (ax + 1) % 3;
                    foreach (float s2 in sg)
                    {
                        Vector3 P1(float d) => F(ax, s, s2, d);  // on face ax, toward a2
                        Vector3 P2(float d)                    // on face a2, toward ax; third axis coordinate d
                        {
                            var p = new Vector3();
                            p[a2] = s2 * h[a2];
                            p[ax] = s * (h[ax] - c);
                            int a3 = (ax + 2) % 3;
                            p[a3] = d * (h[a3] - c);
                            return p;
                        }
                        Face(P1(-1), P1(1), P2(1), P2(-1));
                    }
                }
            if (c > 0f)
                foreach (float sx in sg) foreach (float sy in sg) foreach (float sz in sg)
                    Face(F(0, sx, sy, sz), F(1, sy, sz, sx), F(2, sz, sx, sy));
        }

        /// <summary>Rows x columns of points (mesh space); wrap = columns form a closed ring; outward = hint for open sheets.</summary>
        public void Grid(Vector3[,] p, bool wrap, Vector3 surface, Func<int, int, Color> color, Vector3? outward = null, bool inward = false)
        {
            int rows = p.GetLength(0), cols = p.GetLength(1), qc = wrap ? cols : cols - 1;
            Vector3 centroid = Vector3.zero;
            foreach (Vector3 q in p) centroid += q;
            centroid /= p.Length;

            var nrm = new Vector3[rows, cols];
            float orient = 0f;
            for (int i = 0; i < rows - 1; i++)
            for (int j = 0; j < qc; j++)
            {
                int j1 = (j + 1) % cols;
                Vector3 a = p[i, j], b = p[i + 1, j], c = p[i + 1, j1], d = p[i, j1];
                Vector3 fn = Vector3.Cross(c - a, d - b);
                orient += outward.HasValue ? Vector3.Dot(fn, outward.Value) : Vector3.Dot(fn, (a + b + c + d) * 0.25f - centroid);
                nrm[i, j] += fn; nrm[i + 1, j] += fn; nrm[i + 1, j1] += fn; nrm[i, j1] += fn;
            }
            bool flip = (orient < 0f) != inward; // inward: inner shells seen from inside

            int start = V.Count;
            for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
            {
                Vector3 n = nrm[i, j].sqrMagnitude > 1e-20f ? nrm[i, j].normalized : (p[i, j] - centroid).normalized;
                V.Add(p[i, j]);
                N.Add(flip ? -n : n);
                C.Add(Lin(color(i, j)));
                S.Add(surface);
            }
            for (int i = 0; i < rows - 1; i++)
            for (int j = 0; j < qc; j++)
            {
                int j1 = (j + 1) % cols;
                int a = start + i * cols + j, b = start + (i + 1) * cols + j, c = start + (i + 1) * cols + j1, d = start + i * cols + j1;
                Tri(a, b, c, flip);
                Tri(a, c, d, flip);
            }
        }

        private void Tri(int a, int b, int c, bool flip)
        {
            if (Vector3.Cross(V[b] - V[a], V[c] - V[a]).sqrMagnitude < 1e-16f) return; // collapsed (crease / pole)
            T.Add(a);
            if (flip) { T.Add(c); T.Add(b); } else { T.Add(b); T.Add(c); }
        }

        /// <summary>Surface of revolution around the frame's Y axis: profile (radius, height), radiusScale(row, angle).</summary>
        public void Lathe(Matrix4x4 frame, Vector2[] profile, int segments, Vector3 surface, Func<int, float, Color> color, Func<int, float, float> radiusScale = null, bool inward = false)
        {
            var p = new Vector3[profile.Length, segments];
            for (int i = 0; i < profile.Length; i++)
            for (int j = 0; j < segments; j++)
            {
                float a = j * Mathf.PI * 2f / segments;
                float r = profile[i].x * (radiusScale != null ? radiusScale(i, a) : 1f);
                p[i, j] = frame.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * r, profile[i].y, Mathf.Sin(a) * r));
            }
            Grid(p, true, surface, (i, j) => color(i, j * Mathf.PI * 2f / segments), null, inward);
        }

        /// <summary>Rivet head: a low dome on a surface (centre, outward normal).</summary>
        public void Dome(Vector3 centre, Vector3 normal, float radius, Color color, Vector3 surface)
        {
            Matrix4x4 m = Matrix4x4.TRS(centre - normal * radius * 0.3f, Quaternion.FromToRotation(Vector3.up, normal), Vector3.one);
            var prof = new Vector2[5];
            for (int k = 0; k < 5; k++)
            {
                float a = k * Mathf.PI * 0.5f / 4f;
                prof[k] = new Vector2(radius * Mathf.Cos(a), radius * 0.75f * Mathf.Sin(a));
            }
            prof[4].x = 0f;
            Lathe(m, prof, 10, surface, (i, a) => color);
        }

        private static Material propMaterial;

        /// <summary>
        /// Shared material of static MeshKit props: colour and surface per vertex, patterns in object space (scaled), so
        /// nothing stretches whatever the part's size. One material for every prop.
        /// </summary>
        public static Material PropMaterial()
        {
            if (propMaterial != null) return propMaterial;
            Shader shader = Shader.Find("Custom/CharacterSurface");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            propMaterial = new Material(shader) { name = "MAT_Prop", enableInstancing = true };
            propMaterial.SetColor("_BaseColor", Color.white);
            propMaterial.SetFloat("_VertexSurface", 1f);
            propMaterial.SetFloat("_ObjectSpace", 1f);
            return propMaterial;
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(V);
            mesh.SetNormals(N);
            mesh.SetColors(C);
            mesh.SetUVs(2, S);
            mesh.SetTriangles(T, 0);
            mesh.RecalculateBounds();
            Debug.Log($"[MeshKit] {name}: {V.Count} verts, {T.Count / 3} tris");
            return mesh;
        }
    }
}
