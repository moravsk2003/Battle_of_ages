using System;
using UnityEngine;

namespace BattleOfAges.Resources
{
    public enum TreeState
    {
        WholeTree,
        WoodPile,
        Depleted
    }

    /// <summary>
    /// Resource node component for trees supporting Branch Gathering (1 Wood/5s) and Chopping (2 Wood/1s into Wood Pile).
    /// Uses static shader caching and shared procedural materials.
    /// </summary>
    [DisallowMultipleComponent]
    public class ResourceNodeTree : MonoBehaviour
    {
        private static Shader cachedLitShader;
        private static Material cachedBarkMaterial;

        [Header("Resource Configuration")]
        [SerializeField] private TreeState currentState = TreeState.WholeTree;
        [SerializeField] private int maxWoodCapacity = 1000;
        [SerializeField] private int currentWoodRemaining = 1000;

        private float branchTimer = 0f;
        private float chopTimer = 0f;

        public TreeState CurrentState => currentState;
        public int WoodRemaining => currentWoodRemaining;
        public int MaxWoodCapacity => maxWoodCapacity;
        public float BranchProgress => Mathf.Clamp01(branchTimer / 5.0f);

        public event Action<int> OnWoodHarvested;
        public event Action OnTreeChoppedDown;
        public event Action OnTreeDepleted;

        private GameObject woodPileObj;

        private void Awake()
        {
            currentWoodRemaining = maxWoodCapacity;
        }

        public bool GatherBranchesProgress(float deltaTime)
        {
            if (currentState != TreeState.WholeTree) return false;

            branchTimer += deltaTime;
            if (branchTimer >= 5.0f)
            {
                branchTimer = 0f;
                OnWoodHarvested?.Invoke(1);
                return true;
            }
            return false;
        }

        public void CancelBranchGathering()
        {
            branchTimer = 0f;
        }

        public bool ChopWoodProgress(float deltaTime)
        {
            if (currentState == TreeState.WholeTree)
            {
                ConvertToWoodPile();
            }

            if (currentState != TreeState.WoodPile) return false;

            chopTimer += deltaTime;
            if (chopTimer >= 0.5f)
            {
                chopTimer = 0f;

                int woodExtracted = Mathf.Min(1, currentWoodRemaining);
                currentWoodRemaining -= woodExtracted;

                OnWoodHarvested?.Invoke(woodExtracted);

                if (currentWoodRemaining <= 0)
                {
                    currentState = TreeState.Depleted;
                    OnTreeDepleted?.Invoke();
                    Destroy(gameObject);
                    return false;
                }
                return true;
            }
            return false;
        }

        public void ConvertToWoodPile()
        {
            if (currentState != TreeState.WholeTree) return;

            currentState = TreeState.WoodPile;
            currentWoodRemaining = maxWoodCapacity;

            for (int i = 0; i < 3; i++)
            {
                Transform foliage = transform.Find($"Foliage_{i}");
                if (foliage != null) foliage.gameObject.SetActive(false);
            }

            Transform trunk = transform.Find("Trunk");
            if (trunk != null)
            {
                trunk.localScale = new Vector3(0.7f, 0.3f, 0.7f);
                trunk.localPosition = new Vector3(0, 0.3f, 0);
            }

            CreateWoodPileVisuals();
            OnTreeChoppedDown?.Invoke();
        }

        private void CreateWoodPileVisuals()
        {
            if (woodPileObj != null) return;

            woodPileObj = new GameObject("WoodPileLogs");
            woodPileObj.transform.SetParent(transform, false);

            if (cachedLitShader == null)
            {
                cachedLitShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            }
            if (cachedBarkMaterial == null)
            {
                cachedBarkMaterial = new Material(cachedLitShader) { color = new Color(0.42f, 0.28f, 0.14f) };
            }

            for (int i = 0; i < 4; i++)
            {
                GameObject log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                log.name = $"Log_{i}";
                log.transform.SetParent(woodPileObj.transform, false);
                log.transform.localScale = new Vector3(0.4f, 1.0f, 0.4f);
                log.transform.localRotation = Quaternion.Euler(0, 45f * i, 90f);
                log.transform.localPosition = new Vector3((i % 2 - 0.5f) * 0.6f, 0.2f + (i / 2 * 0.25f), 0);
                
                MeshRenderer mr = log.GetComponent<MeshRenderer>();
                if (mr != null) mr.material = cachedBarkMaterial;

                Collider col = log.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
        }
    }
}
