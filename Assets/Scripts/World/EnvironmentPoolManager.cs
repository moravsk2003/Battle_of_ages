using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Resources;
using BattleOfAges.Utils;

namespace BattleOfAges.World
{
    /// <summary>
    /// Central high-performance Object Pooling Manager for World Chunks, Trees, and Ore Pebbles.
    /// Eliminates runtime GC allocation and frame spikes during chunk streaming.
    /// </summary>
    [DisallowMultipleComponent]
    public class EnvironmentPoolManager : MonoBehaviour
    {
        public static EnvironmentPoolManager Instance { get; private set; }

        [Header("Initial Pool Sizes")]
        [SerializeField] private int initialTrees = 100;
        [SerializeField] private int initialPines = 160;
        [SerializeField] private int initialPebbles = 60;
        [SerializeField] private int initialChunks = 36;

        private readonly Queue<GameObject> treePool = new Queue<GameObject>();
        private readonly Queue<GameObject> pinePool = new Queue<GameObject>();
        private const string PinePrefix = "PooledPine";
        private readonly Queue<GameObject> pebblePool = new Queue<GameObject>();
        private readonly Queue<ProceduralBiomeChunk> chunkPool = new Queue<ProceduralBiomeChunk>();

        private Transform poolContainer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            GameObject container = new GameObject("[EnvironmentObjectPool]");
            container.transform.SetParent(transform);
            poolContainer = container.transform;
            poolContainer.gameObject.SetActive(false);

            PrewarmPools();
            PrewarmStreamedModels();
        }

        /// <summary>
        /// Builds (and drops) one visual of every wildlife species and raider type, so their first appearance while chunks
        /// stream does not pay for loading baked meshes and building the cached character / gear meshes (27-50 ms each).
        /// </summary>
        private void PrewarmStreamedModels()
        {
            var warm = new GameObject("[WarmUp]");
            warm.transform.SetParent(poolContainer, false); // inactive: what is built below never renders, animates or thinks
            void Keep(GameObject built) => built.transform.SetParent(warm.transform, false);

            var deer = new GameObject("Deer"); DeerModelBuilder.Build(deer.transform); Keep(deer);
            var boar = new GameObject("Boar"); BoarModelBuilder.Build(boar.transform); Keep(boar);
            var rabbit = new GameObject("Rabbit"); RabbitModelBuilder.Build(rabbit.transform); Keep(rabbit);
            foreach (HumanoidModelBuilder.HumanType type in new[] { HumanoidModelBuilder.HumanType.Warlord, HumanoidModelBuilder.HumanType.Bandit, HumanoidModelBuilder.HumanType.Goblin })
                Keep(HumanoidModelBuilder.CreateHumanoidModel(type, "Raider"));
            Destroy(warm);
        }

        private void PrewarmPools()
        {
            for (int i = 0; i < initialTrees; i++)
            {
                GameObject tree = SteppeTreeBuilder.CreateSteppeTree($"PooledTree_{i}", i);
                tree.transform.SetParent(poolContainer, false);
                tree.SetActive(false);
                treePool.Enqueue(tree);
            }

            for (int i = 0; i < initialPines; i++)
            {
                GameObject pine = SteppeTreeBuilder.CreatePineTree($"{PinePrefix}_{i}", i);
                pine.transform.SetParent(poolContainer, false);
                pine.SetActive(false);
                pinePool.Enqueue(pine);
            }

            for (int i = 0; i < initialPebbles; i++)
            {
                GameObject pebble = OrePebbleNode.CreatePebblePrefab($"PooledPebble_{i}");
                pebble.transform.SetParent(poolContainer, false);
                pebble.SetActive(false);
                pebblePool.Enqueue(pebble);
            }

            for (int i = 0; i < initialChunks; i++)
            {
                GameObject chunkObj = new GameObject($"PooledChunk_{i}");
                chunkObj.transform.SetParent(poolContainer, false);
                ProceduralBiomeChunk chunk = chunkObj.AddComponent<ProceduralBiomeChunk>();
                chunkObj.SetActive(false);
                chunkPool.Enqueue(chunk);
            }
        }

        public GameObject GetTree(Transform parent, Vector3 position, Quaternion rotation, Vector3 localScale, int variant = 0, bool conifer = false)
        {
            GameObject tree;
            Queue<GameObject> pool = conifer ? pinePool : treePool;
            if (pool.Count > 0)
            {
                tree = pool.Dequeue();
            }
            else
            {
                tree = conifer
                    ? SteppeTreeBuilder.CreatePineTree(PinePrefix + "_Extra", variant)
                    : SteppeTreeBuilder.CreateSteppeTree("PooledTree_Extra", variant);
            }

            tree.transform.SetParent(parent, false);
            tree.transform.position = position;
            tree.transform.rotation = rotation;
            tree.transform.localScale = localScale;
            tree.SetActive(true);

            ResourceNodeTree node = tree.GetComponent<ResourceNodeTree>();
            if (node != null)
            {
                node.ResetTreeState();
            }

            return tree;
        }

        public void ReturnTree(GameObject tree)
        {
            if (tree == null) return;

            tree.SetActive(false);
            tree.transform.SetParent(poolContainer, false);
            (tree.name.StartsWith(PinePrefix) ? pinePool : treePool).Enqueue(tree);
        }

        public GameObject GetPebble(Transform parent, Vector3 position)
        {
            GameObject pebble;
            if (pebblePool.Count > 0)
            {
                pebble = pebblePool.Dequeue();
            }
            else
            {
                pebble = OrePebbleNode.CreatePebblePrefab("PooledPebble_Extra");
            }

            pebble.transform.SetParent(parent, false);
            pebble.transform.position = position;
            pebble.SetActive(true);

            OrePebbleNode node = pebble.GetComponent<OrePebbleNode>();
            if (node != null)
            {
                node.TargetedBy = null;
            }

            return pebble;
        }

        public void ReturnPebble(GameObject pebble)
        {
            if (pebble == null) return;

            pebble.SetActive(false);
            pebble.transform.SetParent(poolContainer, false);
            pebblePool.Enqueue(pebble);
        }

        /// <summary>A chunk for coord, inactive: its first generation step (terrain) activates it, so stale ground never shows.</summary>
        public ProceduralBiomeChunk GetChunk(Transform parent, Vector2Int coord)
        {
            ProceduralBiomeChunk chunk;
            if (chunkPool.Count > 0)
            {
                chunk = chunkPool.Dequeue();
                chunk.gameObject.name = $"Chunk_{coord.x}_{coord.y}";
                chunk.transform.SetParent(parent, false);
            }
            else
            {
                GameObject chunkObj = new GameObject($"Chunk_{coord.x}_{coord.y}");
                chunkObj.SetActive(false);
                chunkObj.transform.SetParent(parent, false);
                chunk = chunkObj.AddComponent<ProceduralBiomeChunk>();
            }

            return chunk;
        }

        public void ReturnChunk(ProceduralBiomeChunk chunk)
        {
            if (chunk == null) return;

            chunk.ReleaseChunkResources();
            chunk.gameObject.SetActive(false);
            chunk.transform.SetParent(poolContainer, false);
            chunkPool.Enqueue(chunk);
        }
    }
}
