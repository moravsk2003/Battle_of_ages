using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.RTS;
using BattleOfAges.Utils;

namespace BattleOfAges.Building
{
    /// <summary>
    /// Ore Mine building component. Holds up to 10 workers, each generating 1 ore per second (tier 0..2, rolled like a
    /// pebble) into an output buffer that haulers carry to the storage chest; the HUD label shows it and lets workers out.
    /// Supports worker assignment and hauling to storage chests.
    /// </summary>
    [DisallowMultipleComponent]
    public class MineNode : MonoBehaviour
    {
        public const int MAX_WORKERS = 10;

        private static readonly List<MineNode> activeMines = new List<MineNode>();
        public static IReadOnlyList<MineNode> ActiveMines => activeMines;

        private static Shader cachedLitShader;
        private static Shader cachedUnlitShader;

        [Header("Mine Production Settings")]
        [SerializeField] private int assignedWorkerCount = 0;
        [SerializeField] private int maxAccumulatedOre = 500;

        /// <summary>Highest ore tier a mine yields; each mined piece rolls its tier like a pebble (10% I, 1% II).</summary>
        public const int MAX_MINED_TIER = 2;

        private float productionTimer = 0f;
        private readonly int[] oreByTier = new int[MAX_MINED_TIER + 1];
        private readonly List<NpcResourceGatherer> workers = new List<NpcResourceGatherer>(MAX_WORKERS);

        public int AssignedWorkerCount => assignedWorkerCount;
        public int MaxAccumulatedOre => maxAccumulatedOre;
        public int AccumulatedOreCount { get { int n = 0; foreach (int c in oreByTier) n += c; return n; } }
        public int OreOfTier(int tier) => tier >= 0 && tier <= MAX_MINED_TIER ? oreByTier[tier] : 0;
        public bool IsFullWorkers => assignedWorkerCount >= MAX_WORKERS;
        public IReadOnlyList<NpcResourceGatherer> Workers => workers;

        private void OnEnable()
        {
            if (!activeMines.Contains(this))
            {
                activeMines.Add(this);
            }
        }

        private void Start()
        {
            SnapToTerrain();
        }

        public void SnapToTerrain()
        {
            Vector3 pos = transform.position;
            pos.y = RtsUnit.GetTerrainHeight(pos);
            transform.position = pos;
        }

        private void Update()
        {
            if (assignedWorkerCount <= 0) return;

            productionTimer += Time.deltaTime;
            if (productionTimer >= 1.0f)
            {
                productionTimer = 0f;
                Produce(assignedWorkerCount); // each worker produces 1 ore per second
            }
        }

        /// <summary>Adds mined ore to the buffer (up to its capacity), each piece with a rolled tier.</summary>
        public void Produce(int pieces)
        {
            for (int i = 0; i < pieces && AccumulatedOreCount < maxAccumulatedOre; i++)
                oreByTier[Mathf.Min(BattleOfAges.Resources.OreTierHelper.DeterminePebbleOreTier(), MAX_MINED_TIER)]++;
        }

        public bool AssignWorker(NpcResourceGatherer worker = null)
        {
            if (assignedWorkerCount >= MAX_WORKERS) return false;
            if (worker != null)
            {
                if (workers.Contains(worker)) return true;
                workers.Add(worker);
            }
            assignedWorkerCount++;
            return true;
        }

        /// <summary>A worker who is not inside (still walking here) does not change the count.</summary>
        public bool UnassignWorker(NpcResourceGatherer worker = null)
        {
            if (worker != null && !workers.Remove(worker)) return false;
            if (assignedWorkerCount <= 0) return false;
            assignedWorkerCount--;
            return true;
        }

        /// <summary>Sends the last worker who went in back out (the mine label's button).</summary>
        public bool ReleaseOneWorker()
        {
            for (int i = workers.Count - 1; i >= 0; i--)
            {
                NpcResourceGatherer w = workers[i];
                if (w == null) { workers.RemoveAt(i); assignedWorkerCount = Mathf.Max(0, assignedWorkerCount - 1); continue; }
                w.LeaveMine();
                return true;
            }
            return false;
        }

        /// <summary>Takes up to maxAmount ore of one tier, the best tier first so rare ore is not left behind.</summary>
        public int TakeOre(int maxAmount, out int tier)
        {
            for (tier = MAX_MINED_TIER; tier >= 0; tier--)
            {
                if (oreByTier[tier] <= 0) continue;
                int taken = Mathf.Min(oreByTier[tier], maxAmount);
                oreByTier[tier] -= taken;
                return taken;
            }
            tier = 0;
            return 0;
        }

        public Vector3 GetOutputDepositPosition()
        {
            Vector3 pos = transform.position + transform.forward * 2.8f;
            pos.y = RtsUnit.GetTerrainHeight(pos);
            return pos;
        }

        public static MineNode GetNearestMineWithOre(Vector3 fromPos)
        {
            MineNode nearest = null;
            float minSqrDst = float.MaxValue;

            int count = activeMines.Count;
            for (int i = 0; i < count; i++)
            {
                MineNode mine = activeMines[i];
                if (mine == null || !mine.gameObject.activeInHierarchy) continue;
                if (mine.AccumulatedOreCount <= 0) continue;

                float sqrDst = (fromPos - mine.transform.position).sqrMagnitude;
                if (sqrDst < minSqrDst)
                {
                    minSqrDst = sqrDst;
                    nearest = mine;
                }
            }

            return nearest;
        }

        public static MineNode GetNearestMineWithCapacity(Vector3 fromPos)
        {
            MineNode nearest = null;
            float minSqrDst = float.MaxValue;

            int count = activeMines.Count;
            for (int i = 0; i < count; i++)
            {
                MineNode mine = activeMines[i];
                if (mine == null || !mine.gameObject.activeInHierarchy) continue;
                if (mine.IsFullWorkers) continue;

                float sqrDst = (fromPos - mine.transform.position).sqrMagnitude;
                if (sqrDst < minSqrDst)
                {
                    minSqrDst = sqrDst;
                    nearest = mine;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Creates procedural 3D Ore Mine prefab (cave entrance, timber beams, mine cart, ore mound).
        /// </summary>
        public static GameObject CreateMinePrefab(string name = "Mine")
        {
            GameObject root = new GameObject(name);
            root.AddComponent<MineNode>();
            root.AddComponent<BuildingNode>();

            if (cachedLitShader == null)
            {
                cachedLitShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            }

            Material rockMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Stone, new Color(0.27f, 0.30f, 0.33f), 0f, 0.22f);
            Material woodMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Bark, new Color(0.25f, 0.12f, 0.04f), 0f, 0.20f);
            Material oreMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Ore, new Color(0.58f, 0.29f, 0.05f), 0.40f, 0.48f);
            Material darkMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Dark, new Color(0.035f, 0.042f, 0.055f), 0f, 0.08f);

            // 1. Cave Mound Base (Dark Rock)
            GameObject mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mound.name = "CaveMound";
            mound.transform.SetParent(root.transform, false);
            mound.transform.localPosition = new Vector3(0f, 1.2f, -0.5f);
            mound.transform.localScale = new Vector3(3.8f, 2.6f, 3.8f);
            mound.GetComponent<MeshRenderer>().material = rockMat;

            GameObject rockLeft = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rockLeft.name = "MoundRockLeft";
            rockLeft.transform.SetParent(root.transform, false);
            rockLeft.transform.localPosition = new Vector3(-1.55f, 0.50f, 1.05f);
            rockLeft.transform.localScale = new Vector3(1.05f, 0.72f, 0.85f);
            rockLeft.transform.localRotation = Quaternion.Euler(18f, 22f, -12f);
            rockLeft.GetComponent<MeshRenderer>().sharedMaterial = rockMat;

            GameObject rockRight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rockRight.name = "MoundRockRight";
            rockRight.transform.SetParent(root.transform, false);
            rockRight.transform.localPosition = new Vector3(1.42f, 0.38f, 1.03f);
            rockRight.transform.localScale = new Vector3(0.86f, 0.58f, 0.75f);
            rockRight.transform.localRotation = Quaternion.Euler(-16f, 48f, 18f);
            rockRight.GetComponent<MeshRenderer>().sharedMaterial = rockMat;

            // 2. Timber Entrance Arch (Left Beam, Right Beam, Top Beam)
            GameObject beamLeft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beamLeft.name = "BeamLeft";
            beamLeft.transform.SetParent(root.transform, false);
            beamLeft.transform.localPosition = new Vector3(-1.1f, 1.3f, 1.2f);
            beamLeft.transform.localScale = new Vector3(0.35f, 2.6f, 0.35f);
            beamLeft.GetComponent<MeshRenderer>().material = woodMat;

            GameObject beamRight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beamRight.name = "BeamRight";
            beamRight.transform.SetParent(root.transform, false);
            beamRight.transform.localPosition = new Vector3(1.1f, 1.3f, 1.2f);
            beamRight.transform.localScale = new Vector3(0.35f, 2.6f, 0.35f);
            beamRight.GetComponent<MeshRenderer>().material = woodMat;

            GameObject beamTop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beamTop.name = "BeamTop";
            beamTop.transform.SetParent(root.transform, false);
            beamTop.transform.localPosition = new Vector3(0f, 2.5f, 1.2f);
            beamTop.transform.localScale = new Vector3(2.6f, 0.35f, 0.4f);
            beamTop.GetComponent<MeshRenderer>().material = woodMat;

            // 3. Dark Tunnel Entrance Hole
            GameObject tunnelHole = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tunnelHole.name = "TunnelEntrance";
            tunnelHole.transform.SetParent(root.transform, false);
            tunnelHole.transform.localPosition = new Vector3(0f, 1.1f, 0.8f);
            tunnelHole.transform.localScale = new Vector3(1.8f, 2.1f, 1.2f);
            tunnelHole.GetComponent<MeshRenderer>().material = darkMat;

            // 4. Ore Pile / Mine Cart Outside
            GameObject orePile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orePile.name = "OrePile";
            orePile.transform.SetParent(root.transform, false);
            orePile.transform.localPosition = new Vector3(1.4f, 0.4f, 1.8f);
            orePile.transform.localScale = new Vector3(0.9f, 0.6f, 0.8f);
            orePile.GetComponent<MeshRenderer>().material = oreMat;

            // Small cart with separate wheels provides a clear mine landmark at RTS scale.
            GameObject cart = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cart.name = "MineCart";
            cart.transform.SetParent(root.transform, false);
            cart.transform.localPosition = new Vector3(-1.25f, 0.42f, 1.65f);
            cart.transform.localScale = new Vector3(0.95f, 0.42f, 0.62f);
            cart.transform.localRotation = Quaternion.Euler(0f, -14f, 0f);
            cart.GetComponent<MeshRenderer>().sharedMaterial = woodMat;

            for (int i = -1; i <= 1; i += 2)
            {
                GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                wheel.name = i < 0 ? "MineCartWheelLeft" : "MineCartWheelRight";
                wheel.transform.SetParent(root.transform, false);
                wheel.transform.localPosition = new Vector3(-1.25f + i * 0.49f, 0.22f, 1.65f);
                wheel.transform.localScale = new Vector3(0.22f, 0.10f, 0.22f);
                wheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                wheel.GetComponent<MeshRenderer>().sharedMaterial = darkMat;
            }

            // Root Collider
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.3f, 0f);
            col.size = new Vector3(3.8f, 2.8f, 3.8f);

            // Clean child colliders
            foreach (var childCol in root.GetComponentsInChildren<Collider>())
            {
                if (childCol != col) DestroyImmediate(childCol);
            }

            return root;
        }
    }
}
