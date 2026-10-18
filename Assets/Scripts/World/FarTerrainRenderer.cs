using UnityEngine;
using UnityEngine.Rendering;

namespace BattleOfAges.World
{
    /// <summary>
    /// Cheap distant terrain: one coarse grid mesh (same height function and biome colours as the chunks) around the
    /// viewer, with a hole where the detailed streamed chunks are. Gives distant mountains and a horizon instead of the
    /// edge of the streamed area. Visual only: no collider, no props.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class FarTerrainRenderer : MonoBehaviour
    {
        private const float Spacing = 16f;          // grid step; chunk size (64) must be a multiple of it
        private const int HalfExtentCells = 96;     // 96 * 16 = 1536 m in every direction

        private Mesh mesh;
        private Vector3[] vertices, spareVertices;
        private Color[] colors, spareColors;
        private int[] triangles;
        private int triangleHoleCells = -1;         // hole size the mesh's triangles were built for
        private Vector2Int gridCentre;              // in cells; the grid is world-aligned, so a moved grid reuses its overlap
        private bool hasGrid;

        public void Initialize(Material material)
        {
            MeshRenderer mr = GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;

            mesh = new Mesh { name = "FarTerrain", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;

            int side = HalfExtentCells * 2 + 1;
            vertices = new Vector3[side * side];
            colors = new Color[side * side];
            spareVertices = new Vector3[side * side];
            spareColors = new Color[side * side];
        }

        /// <summary>
        /// Rebuilds the mesh around the viewer chunk, leaving out the detailed chunk square. Only the vertices the grid did
        /// not cover before are sampled (a one-chunk move samples 4 rows instead of all 193), and the triangles, which are
        /// the same relative to the grid, are set once.
        /// </summary>
        public void Rebuild(Vector2Int viewerChunk, float chunkSize, int detailRadiusChunks)
        {
            if (mesh == null) return;

            int side = HalfExtentCells * 2 + 1;
            int cellsPerChunk = Mathf.RoundToInt(chunkSize / Spacing);
            Vector2Int centre = viewerChunk * cellsPerChunk;
            Vector2Int shift = centre - gridCentre;
            bool reuse = hasGrid && Mathf.Abs(shift.x) < side && Mathf.Abs(shift.y) < side;
            transform.position = Vector3.zero;

            for (int z = 0; z < side; z++)
            {
                int oz = z + shift.y;
                for (int x = 0; x < side; x++)
                {
                    int i = z * side + x;
                    int ox = x + shift.x;
                    if (reuse && ox >= 0 && ox < side && oz >= 0 && oz < side)
                    {
                        int o = oz * side + ox;
                        spareVertices[i] = vertices[o];
                        spareColors[i] = colors[o];
                        continue;
                    }
                    float wx = (centre.x + x - HalfExtentCells) * Spacing;
                    float wz = (centre.y + z - HalfExtentCells) * Spacing;
                    float h = InfiniteWorldManager.GetSampledTerrainHeight(wx, wz);
                    spareVertices[i] = new Vector3(wx, h, wz);
                    spareColors[i] = ProceduralBiomeChunk.GetBlendedBiomeColor(wx, wz, h);
                }
            }
            (vertices, spareVertices) = (spareVertices, vertices);
            (colors, spareColors) = (spareColors, colors);
            gridCentre = centre;
            hasGrid = true;

            // Hole = the detailed chunk square: chunks cover [c - r - 0.5, c + r + 0.5] * chunkSize around the viewer chunk.
            float holeHalf = (detailRadiusChunks + 0.5f) * chunkSize;
            int holeCells = Mathf.RoundToInt(holeHalf / Spacing);

            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            if (holeCells != triangleHoleCells)
            {
                triangleHoleCells = holeCells;
                int quadCount = (side - 1) * (side - 1);
                if (triangles == null || triangles.Length != quadCount * 6) triangles = new int[quadCount * 6];
                int t = 0;
                for (int z = 0; z < side - 1; z++)
                {
                    for (int x = 0; x < side - 1; x++)
                    {
                        int cx = x - HalfExtentCells, cz = z - HalfExtentCells; // cell's lower-left corner, in cells from centre
                        bool insideHole = cx >= -holeCells && cx + 1 <= holeCells && cz >= -holeCells && cz + 1 <= holeCells;
                        if (insideHole) continue;

                        int i = z * side + x;
                        triangles[t++] = i;
                        triangles[t++] = i + side;
                        triangles[t++] = i + 1;
                        triangles[t++] = i + 1;
                        triangles[t++] = i + side;
                        triangles[t++] = i + side + 1;
                    }
                }
                mesh.SetTriangles(triangles, 0, t, 0);
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
