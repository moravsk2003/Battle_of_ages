using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.RTS;
using BattleOfAges.Utils;
using BattleOfAges.Crafting;
using BattleOfAges.Resources;

namespace BattleOfAges.Building
{
    /// <summary>
    /// Ore Mine building component. Holds up to 10 workers, each generating 1 ore per second (tier 0..99: 80% tier 0,
    /// 18% tier I, then ten times rarer per tier) into an output buffer that haulers carry to the storage chest; the HUD label shows it and lets workers out.
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

        /// <summary>Mines yield every ore tier (0..99): 80% tier 0, 18% tier I, each further tier ten times rarer.</summary>
        public const int MAX_MINED_TIER = OreTierHelper.MAX_TIERS - 1;

        private float productionTimer = 0f;
        private float productionCarry;
        private readonly int[] oreByTier = new int[MAX_MINED_TIER + 1];
        private readonly List<NpcResourceGatherer> workers = new List<NpcResourceGatherer>(MAX_WORKERS);

        public int AssignedWorkerCount => assignedWorkerCount;
        public int MaxAccumulatedOre => maxAccumulatedOre;
        private int accumulatedOre;
        public int AccumulatedOreCount => accumulatedOre;
        /// <summary>Best tier waiting in the buffer (-1 when empty).</summary>
        public int HighestTierInStock { get { for (int t = MAX_MINED_TIER; t >= 0; t--) if (oreByTier[t] > 0) return t; return -1; } }
        public int OreOfTier(int tier) => tier >= 0 && tier <= MAX_MINED_TIER ? oreByTier[tier] : 0;
        public bool IsFullWorkers => assignedWorkerCount >= MAX_WORKERS;
        public IReadOnlyList<NpcResourceGatherer> Workers => workers;

        /// <summary>Ore per second: 1 per worker inside, x(1 + 0.25 x level) for a worker holding a pickaxe.</summary>
        public float OrePerSecond
        {
            get
            {
                float rate = 0f;
                int counted = 0;
                foreach (NpcResourceGatherer w in workers)
                {
                    if (w == null) continue;
                    counted++;
                    var gear = w.GetComponent<Equipment>();
                    int pick = gear != null ? gear.LevelOf(ItemType.Pickaxe) : 0;
                    rate += pick > 0 ? ItemStatCalculator.CalculateToolEfficiency(pick) : 1f;
                }
                return rate + Mathf.Max(0, assignedWorkerCount - counted); // workers without a record (tests, old saves)
            }
        }

        private void OnEnable()
        {
            if (!activeMines.Contains(this))
            {
                activeMines.Add(this);
            }
        }

        private void OnDisable() => activeMines.Remove(this); // a site's mine, a destroyed one

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

        private float pickSoundTimer;

        private void Update()
        {
            if (assignedWorkerCount <= 0) return;

            // Picks from inside: more often with more miners
            pickSoundTimer -= Time.deltaTime;
            if (pickSoundTimer <= 0f)
            {
                pickSoundTimer = Mathf.Lerp(1f, 0.35f, assignedWorkerCount / (float)MAX_WORKERS);
                UI.GameSound.At(UI.GameSound.Kind.Pick, transform.position, 0.8f);
            }

            productionTimer += Time.deltaTime;
            if (productionTimer >= 1.0f)
            {
                productionTimer = 0f;
                // 1 ore per second per worker, more with a pickaxe; fractions carry over to the next second
                productionCarry += OrePerSecond;
                int whole = Mathf.FloorToInt(productionCarry);
                productionCarry -= whole;
                Produce(whole);
            }
        }

        /// <summary>Adds mined ore to the buffer (up to its capacity), each piece with a rolled tier.</summary>
        public void Produce(int pieces)
        {
            for (int i = 0; i < pieces && accumulatedOre < maxAccumulatedOre; i++)
            {
                oreByTier[Mathf.Min(OreTierHelper.DetermineMineOreTier(), MAX_MINED_TIER)]++;
                accumulatedOre++;
            }
        }

        public bool AssignWorker(NpcResourceGatherer worker = null)
        {
            if (assignedWorkerCount >= MAX_WORKERS) return false;
            if (worker != null)
            {
                if (workers.Contains(worker)) return true;
                workers.Add(worker);
                GearDispenser.GiveTool(worker.GetComponent<Equipment>(), ItemType.Pickaxe); // takes the best pickaxe from the store itself
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

        /// <summary>Ore waiting per tier (tiers that have some), for saving.</summary>
        public List<KeyValuePair<int, int>> OreStock()
        {
            var list = new List<KeyValuePair<int, int>>();
            for (int t = 0; t <= MAX_MINED_TIER; t++) if (oreByTier[t] > 0) list.Add(new KeyValuePair<int, int>(t, oreByTier[t]));
            return list;
        }

        public void SetOreStock(IEnumerable<KeyValuePair<int, int>> stock)
        {
            System.Array.Clear(oreByTier, 0, oreByTier.Length);
            accumulatedOre = 0;
            if (stock == null) return;
            foreach (var p in stock)
            {
                if (p.Key < 0 || p.Key > MAX_MINED_TIER || p.Value <= 0) continue;
                oreByTier[p.Key] += p.Value;
                accumulatedOre += p.Value;
            }
        }

        /// <summary>Takes up to maxAmount ore of one tier, the best tier first so rare ore is not left behind.</summary>
        public int TakeOre(int maxAmount, out int tier)
        {
            for (tier = MAX_MINED_TIER; tier >= 0; tier--)
            {
                if (oreByTier[tier] <= 0) continue;
                int taken = Mathf.Min(oreByTier[tier], maxAmount);
                oreByTier[tier] -= taken;
                accumulatedOre -= taken;
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

        public static MineNode GetNearestMineWithOre(Vector3 fromPos) => Nearest(fromPos, m => m.AccumulatedOreCount > 0);

        private static MineNode Nearest(Vector3 fromPos, System.Predicate<MineNode> suits)
        {
            MineNode nearest = null;
            float minSqrDst = float.MaxValue;
            foreach (MineNode mine in activeMines)
            {
                if (mine == null || !mine.gameObject.activeInHierarchy || !suits(mine)) continue;
                float sqrDst = (fromPos - mine.transform.position).sqrMagnitude;
                if (sqrDst < minSqrDst) { minSqrDst = sqrDst; nearest = mine; }
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

            // A rock hill with a timbered adit, a rail track, an ore cart, a heap and a lantern (one shared mesh, see MineBuilder)
            MineBuilder.Attach(root.transform);

            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.3f, -0.45f); // the hill; the track and the cart in front stay walkable
            col.size = new Vector3(4.2f, 2.8f, 3.5f);

            // Clean child colliders
            foreach (var childCol in root.GetComponentsInChildren<Collider>())
            {
                if (childCol != col) DestroyImmediate(childCol);
            }

            return root;
        }
    }
}
