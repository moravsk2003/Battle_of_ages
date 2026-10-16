using System;
using UnityEngine;
using BattleOfAges.Utils;

namespace BattleOfAges.Resources
{
    public enum TreeState
    {
        WholeTree,
        WoodPile,
        Depleted
    }

    /// <summary>
    /// Resource node component for trees supporting Branch Gathering (1 Wood/2.5s) and Tree Chopping (fells tree into Wood Pile).
    /// Features dynamic tree wobble animation, wood chip VFX, and realistic resource yield.
    /// </summary>
    [DisallowMultipleComponent]
    public class ResourceNodeTree : MonoBehaviour
    {
        private static Shader cachedLitShader;
        private static Material cachedBarkMaterial;
        private static Material cachedWoodCutMaterial;

        [Header("Resource Configuration")]
        [SerializeField] private TreeState currentState = TreeState.WholeTree;
        [SerializeField] private int maxWoodCapacity = 10;
        [SerializeField] private int currentWoodRemaining = 10;

        private float branchTimer = 0f;
        private float fellingChopProgress = 0f;
        private float woodPileChopTimer = 0f;

        private Vector3 initialEuler;
        private float wobbleVelocity = 0f;
        private float wobbleAngle = 0f;

        public TreeState CurrentState => currentState;
        public int WoodRemaining => currentWoodRemaining;
        public int MaxWoodCapacity => maxWoodCapacity;
        public float BranchProgress => Mathf.Clamp01(branchTimer / 2.5f);
        public float FellingProgress => Mathf.Clamp01(fellingChopProgress / 2.8f);

        public event Action<int> OnWoodHarvested;
        public event Action OnTreeChoppedDown;
        public event Action OnTreeDepleted;

        private GameObject woodPileObj;
        private GameObject stumpObj;

        private void Awake()
        {
            currentWoodRemaining = maxWoodCapacity;
            initialEuler = transform.localEulerAngles;
        }

        private void Update()
        {
            // Smooth spring damping for tree wobble reaction on hit
            if (Mathf.Abs(wobbleAngle) > 0.01f || Mathf.Abs(wobbleVelocity) > 0.01f)
            {
                float springForce = -wobbleAngle * 45f;
                float dampingForce = -wobbleVelocity * 8f;
                wobbleVelocity += (springForce + dampingForce) * Time.deltaTime;
                wobbleAngle += wobbleVelocity * Time.deltaTime;

                transform.localRotation = Quaternion.Euler(initialEuler.x + wobbleAngle, initialEuler.y, initialEuler.z);
            }
        }

        public void ApplyHitImpulse(float strength = 3.5f)
        {
            wobbleVelocity += UnityEngine.Random.Range(strength * 0.7f, strength * 1.3f);
        }

        public bool GatherBranchesProgress(float deltaTime)
        {
            if (currentState != TreeState.WholeTree) return false;

            branchTimer += deltaTime;
            if (branchTimer >= 2.5f)
            {
                branchTimer = 0f;
                ApplyHitImpulse(1.8f);
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
            ApplyHitImpulse(3.2f);

            if (currentState == TreeState.WholeTree)
            {
                fellingChopProgress += deltaTime;
                if (fellingChopProgress >= 2.8f)
                {
                    fellingChopProgress = 0f;
                    ConvertToWoodPile();
                    
                    // Yield initial 4 wood on tree fell
                    int fellYield = Mathf.Min(4, currentWoodRemaining);
                    currentWoodRemaining -= fellYield;
                    OnWoodHarvested?.Invoke(fellYield);
                    return true;
                }
                return false;
            }

            if (currentState == TreeState.WoodPile)
            {
                woodPileChopTimer += deltaTime;
                if (woodPileChopTimer >= 0.8f)
                {
                    woodPileChopTimer = 0f;

                    int woodExtracted = Mathf.Min(2, currentWoodRemaining);
                    currentWoodRemaining -= woodExtracted;

                    OnWoodHarvested?.Invoke(woodExtracted);

                        if (currentWoodRemaining <= 0)
                        {
                            currentState = TreeState.Depleted;
                            OnTreeDepleted?.Invoke();
                            
                            if (woodPileObj != null)
                            {
                                Destroy(woodPileObj);
                                woodPileObj = null;
                            }

                            // Disable colliders on depleted stump
                            Collider col = GetComponent<Collider>();
                            if (col != null) col.enabled = false;

                            return true;
                        }
                        return true;
                }
            }

            return false;
        }

        public void ConvertToWoodPile()
        {
            if (currentState != TreeState.WholeTree) return;

            currentState = TreeState.WoodPile;

            // Hide all crown / foliage clusters
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child == null) continue;

                string cName = child.name.ToLower();
                if (cName.Contains("crown") || cName.Contains("foliage") || cName.Contains("leaves"))
                {
                    child.gameObject.SetActive(false);
                }
            }

            // Replace tall trunk with low stump
            Transform trunk = transform.Find("StylizedTrunk") ?? transform.Find("Trunk");
            if (trunk != null)
            {
                trunk.gameObject.SetActive(false);
            }

            CreateStumpAndWoodPileVisuals();

            // Adjust collider for stump
            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                capsule.center = new Vector3(0f, 0.25f, 0f);
                capsule.height = 0.5f;
                capsule.radius = 0.5f;
            }

            OnTreeChoppedDown?.Invoke();
        }

        private void CreateStumpAndWoodPileVisuals()
        {
            if (stumpObj == null)
            {
                stumpObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stumpObj.name = "TreeStump";
                stumpObj.transform.SetParent(transform, false);
                stumpObj.transform.localPosition = new Vector3(0f, 0.22f, 0f);
                stumpObj.transform.localScale = new Vector3(0.85f, 0.22f, 0.85f);

                Material barkMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Bark, new Color(0.32f, 0.20f, 0.10f), 0f, 0.22f);
                stumpObj.GetComponent<MeshRenderer>().sharedMaterial = barkMat;

                Collider sc = stumpObj.GetComponent<Collider>();
                if (sc != null) DestroyImmediate(sc);
            }

            if (woodPileObj == null)
            {
                woodPileObj = new GameObject("WoodPileLogs");
                woodPileObj.transform.SetParent(transform, false);

                Material woodMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.38f, 0.24f, 0.12f), 0f, 0.25f);
                Material cutEndMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.72f, 0.55f, 0.32f), 0f, 0.20f);

                // Stacked 4 fallen logs next to the stump
                Vector3[] logOffsets = {
                    new Vector3(0.55f, 0.18f, 0.2f),
                    new Vector3(0.55f, 0.18f, -0.25f),
                    new Vector3(-0.5f, 0.18f, 0.05f),
                    new Vector3(0.55f, 0.42f, -0.05f)
                };

                for (int i = 0; i < 4; i++)
                {
                    GameObject log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    log.name = $"FallenLog_{i}";
                    log.transform.SetParent(woodPileObj.transform, false);
                    log.transform.localPosition = logOffsets[i];
                    log.transform.localScale = new Vector3(0.34f, 0.85f, 0.34f);
                    log.transform.localRotation = Quaternion.Euler(0f, 15f + i * 25f, 90f);

                    MeshRenderer mr = log.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = woodMat;

                    Collider col = log.GetComponent<Collider>();
                    if (col != null) DestroyImmediate(col);
                }
            }
        }

        /// <summary>Id of this tree in the world save ("chunkX,chunkY:index" or a scene path); set where it is placed.</summary>
        public string PersistentKey { get; set; }
        public bool IsUntouched => currentState == TreeState.WholeTree && currentWoodRemaining >= maxWoodCapacity;

        /// <summary>Restores a saved state (felled into a wood pile, or a bare stump) without any harvest events.</summary>
        public void ApplySavedState(TreeState state, int woodRemaining)
        {
            ResetTreeState();
            currentWoodRemaining = Mathf.Clamp(woodRemaining, 0, maxWoodCapacity);
            if (state == TreeState.WholeTree) return;
            ConvertToWoodPile();
            if (state != TreeState.Depleted) return;
            currentState = TreeState.Depleted;
            currentWoodRemaining = 0;
            if (woodPileObj != null) { Destroy(woodPileObj); woodPileObj = null; }
            Collider col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        public void ResetTreeState()
        {
            currentState = TreeState.WholeTree;
            currentWoodRemaining = maxWoodCapacity;
            branchTimer = 0f;
            fellingChopProgress = 0f;
            woodPileChopTimer = 0f;

            if (woodPileObj != null)
            {
                Destroy(woodPileObj);
                woodPileObj = null;
            }

            if (stumpObj != null)
            {
                Destroy(stumpObj);
                stumpObj = null;
            }

            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child != null) child.gameObject.SetActive(true);
            }

            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                capsule.enabled = true;
                capsule.center = new Vector3(0f, 1.7f, 0f);
                capsule.radius = 0.45f;
                capsule.height = 3.4f;
            }
        }
    }
}
