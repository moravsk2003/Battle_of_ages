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
        [SerializeField] private int initialPebbles = 60;
        [SerializeField] private int initialChunks = 36;

        private readonly Queue<GameObject> treePool = new Queue<GameObject>();
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

        public GameObject GetTree(Transform parent, Vector3 position, Quaternion rotation, Vector3 localScale, int variant = 0)
        {
            GameObject tree;
            if (treePool.Count > 0)
            {
                tree = treePool.Dequeue();
            }
            else
            {
                tree = SteppeTreeBuilder.CreateSteppeTree("PooledTree_Extra", variant);
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
            treePool.Enqueue(tree);
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

        public ProceduralBiomeChunk GetChunk(Transform parent, Vector2Int coord)
        {
            ProceduralBiomeChunk chunk;
            if (chunkPool.Count > 0)
            {
                chunk = chunkPool.Dequeue();
                chunk.gameObject.name = $"Chunk_{coord.x}_{coord.y}";
                chunk.transform.SetParent(parent, false);
                chunk.gameObject.SetActive(true);
            }
            else
            {
                GameObject chunkObj = new GameObject($"Chunk_{coord.x}_{coord.y}");
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
