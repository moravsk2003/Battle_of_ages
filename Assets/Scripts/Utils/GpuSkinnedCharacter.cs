using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Draws a character on the GPU once it is small on screen: the Animator, foot IK and skinned renderer stop, and a
    /// plain instanced MeshRenderer plays the baked clips (GpuSkinBake) in the vertex shader, so a crowd of distant
    /// soldiers costs no animation or skinning on the CPU. Up close the Animator takes over again (exact attacks, feet
    /// on the ground). The clip follows the unit: idle / walk / run by speed, attacks on the upper body, death.
    /// Only characters drawn by ONE skinned renderer (gear merged into the body) can do this; others keep the Animator.
    /// One driver re-evaluates a slice of all characters each frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GpuSkinnedCharacter : MonoBehaviour
    {
        /// <summary>Screen height fraction below which the GPU draws the character (FP view: ~11 m away).</summary>
        public const float FarScreenHeight = 0.15f;
        public const float CullScreenHeight = 0.006f;
        private const float Hysteresis = 1.15f, CharacterHeight = 2f, WalkSpeed = 0.4f, RunSpeed = 2.8f, FadeTime = 0.25f;

        /// <summary>Viewer for capture / test cameras instead of the current view camera.</summary>
        public static Camera ViewerOverride;

        /// <summary>Off: every character uses its Animator (comparisons).</summary>
        public static bool Enabled
        {
            get => gpuEnabled;
            set { gpuEnabled = value; if (!value) foreach (GpuSkinnedCharacter c in Active.ToArray()) c.SetFar(false); }
        }
        private static bool gpuEnabled = true;

        /// <summary>Every character that can be drawn on the GPU is, at any distance (side-by-side captures).</summary>
        public static bool ForceGpu;

        private static readonly List<GpuSkinnedCharacter> Active = new List<GpuSkinnedCharacter>();
        private static int cursor;
        private static GameObject driverObject;
        private static readonly int LocoId = Shader.PropertyToID("_Loco"), PrevId = Shader.PropertyToID("_LocoPrev"),
                                    SwingId = Shader.PropertyToID("_Swing"), FadeId = Shader.PropertyToID("_Fade");

        public bool IsFar { get; private set; }

        private Animator animator;
        private SkinnedMeshRenderer body;
        private SkinnedMeshLod lod;
        private FootIK footIK;
        private CharacterAnimationDriver driver;
        private CombatUnit combat;
        private HealthComponent health;

        private MeshRenderer gpuRenderer;
        private MeshFilter gpuFilter;
        private MaterialPropertyBlock block;
        private GpuSkinBake bake;
        private Mesh sourceMesh;
        private bool hidden, speck, failed;

        private GpuSkinBake.Clip locoClip;
        private Vector4 loco, prev, swing = new Vector4(0, 0, 1, -1), fade;
        private bool dead;
        private int index = -1;

        private bool started, ready, rejected;

        // Set up on the driver's first look, not here: builders still replace the body in this frame.
        private void Start()
        {
            started = true;
            Register();
        }

        private bool Setup()
        {
            if (rejected) return false;
            animator = GetComponentInChildren<Animator>();
            body = animator != null ? animator.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            if (!Eligible()) { rejected = true; Destroy(this); return false; }
            animator.keepAnimatorStateOnDisable = true;
            lod = GetComponent<SkinnedMeshLod>();
            footIK = animator.GetComponent<FootIK>();
            driver = GetComponentInChildren<CharacterAnimationDriver>();
            combat = GetComponentInParent<CombatUnit>();
            health = GetComponentInParent<HealthComponent>();
            if (combat != null) combat.OnAttackStarted += HandleAttack;
            if (health != null) health.OnDeath += HandleDeath;
            dead = health != null && !health.IsAlive;
            ready = true;
            return true;
        }

        /// <summary>One skinned body in the animated model (its LOD group, if any, only culls it), drawn with Custom/CharacterSurface.</summary>
        private bool Eligible()
        {
            if (body == null || animator.runtimeAnimatorController == null) return false;
            if (animator.GetComponentsInChildren<Renderer>(true).Length != 1) return false;
            foreach (Material m in body.sharedMaterials)
                if (m == null || m.shader == null || m.shader.name != "Custom/CharacterSurface") return false;
            return true;
        }

        private void OnEnable() { if (started) Register(); }

        private void OnDisable()
        {
            // A corpse disables its scripts: it keeps the look it has (its last pose, or the end of the fall)
            if (index < 0) return;
            int last = Active.Count - 1;
            Active[index] = Active[last];
            Active[index].index = index;
            Active.RemoveAt(last);
            index = -1;
        }

        private void OnDestroy()
        {
            if (combat != null) combat.OnAttackStarted -= HandleAttack;
            if (health != null) health.OnDeath -= HandleDeath;
            if (gpuRenderer != null) Destroy(gpuRenderer.gameObject);
        }

        private void Register()
        {
            if (index >= 0) return;
            index = Active.Count;
            Active.Add(this);
            if (Application.isPlaying && driverObject == null)
            {
                driverObject = new GameObject("GpuSkinnedCharacterDriver") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(driverObject);
                driverObject.AddComponent<Driver>();
            }
        }

        private sealed class Driver : MonoBehaviour
        {
            private void Update()
            {
                if (Active.Count == 0) return;
                Camera cam = ViewerOverride != null ? ViewerOverride : ViewCamera.Current;
                if (cam == null) return;
                int n = Mathf.Min(Active.Count, Mathf.Max(32, Active.Count / 4));
                for (int i = 0; i < n; i++)
                {
                    cursor = (cursor + 1) % Active.Count;
                    Active[cursor].Refresh(cam);
                }
            }
        }

        /// <summary>Re-evaluates every character for this camera now (before a screenshot from a capture camera).</summary>
        public static void RefreshAll(Camera cam)
        {
            foreach (GpuSkinnedCharacter c in Active.ToArray()) c.Refresh(cam);
        }

        private void Refresh(Camera cam)
        {
            if (!ready && !Setup()) return;
            float h = ScreenHeight(cam);
            bool wantFar = gpuEnabled && !failed && (ForceGpu || (IsFar ? h < FarScreenHeight * Hysteresis : h < FarScreenHeight));
            if (wantFar != IsFar) SetFar(wantFar);
            if (!IsFar) return;

            if (CurrentSource() != sourceMesh && !Build()) { SetFar(false); return; }
            speck = h < CullScreenHeight;
            gpuRenderer.enabled = !hidden && !speck;
            if (!dead) FollowLocomotion();
        }

        private float ScreenHeight(Camera cam)
        {
            float h;
            if (cam.orthographic) h = CharacterHeight / (2f * cam.orthographicSize);
            else
            {
                float d = Mathf.Max(0.01f, Vector3.Distance(cam.transform.position, transform.position));
                h = CharacterHeight / (2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            }
            return h * QualitySettings.lodBias;
        }

        // Knights draw their distant mesh; everyone else the body as it is (with its merged gear)
        private Mesh CurrentSource() => lod != null && lod.Far != null ? lod.Far : body.sharedMesh;

        /// <summary>The GPU renderer beside the body, with the current body's mesh and materials.</summary>
        private bool Build()
        {
            Mesh source = CurrentSource();
            bake = GpuSkinBake.For(animator, transform, body, source);
            Mesh mesh = bake != null ? GpuSkinBake.SkinMesh(source) : null;
            if (mesh == null) { failed = true; return false; }
            if (gpuRenderer == null)
            {
                var go = new GameObject("GpuBody") { layer = body.gameObject.layer };
                go.transform.SetParent(body.transform.parent, false);
                go.transform.localPosition = body.transform.localPosition;
                go.transform.localRotation = body.transform.localRotation;
                go.transform.localScale = body.transform.localScale;
                gpuFilter = go.AddComponent<MeshFilter>();
                gpuRenderer = go.AddComponent<MeshRenderer>();
                gpuRenderer.enabled = false;
                block = new MaterialPropertyBlock();
            }
            gpuFilter.sharedMesh = mesh;
            Material[] src = body.sharedMaterials;
            var mats = new Material[src.Length];
            for (int i = 0; i < src.Length; i++) mats[i] = bake.MaterialFor(src[i]);
            gpuRenderer.sharedMaterials = mats;
            gpuRenderer.shadowCastingMode = body.shadowCastingMode;
            gpuRenderer.receiveShadows = body.receiveShadows;
            sourceMesh = source;
            return true;
        }

        private void SetFar(bool far)
        {
            if (far == IsFar) return;
            if (far)
            {
                if (gpuRenderer == null || CurrentSource() != sourceMesh) { if (!Build()) return; }
                IsFar = true;
                StartPose();
                animator.enabled = false;
                if (footIK != null) footIK.enabled = false;
                if (lod != null) lod.enabled = false;
                body.enabled = false;
                gpuRenderer.enabled = !hidden;
            }
            else
            {
                IsFar = false;
                if (gpuRenderer != null) gpuRenderer.enabled = false;
                animator.enabled = true;
                if (footIK != null) footIK.enabled = true;
                body.enabled = !hidden;
                if (lod != null) lod.enabled = true; // re-picks its mesh and hides specks
                if (driver != null) driver.Resync(dead);
            }
        }

        /// <summary>Hidden by its owner (a worker inside a mine): neither body is drawn.</summary>
        public void SetHidden(bool value)
        {
            hidden = value;
            if (!ready) return;
            if (IsFar) { body.enabled = false; if (gpuRenderer != null) gpuRenderer.enabled = !hidden && !speck; }
            else { if (gpuRenderer != null) gpuRenderer.enabled = false; if (lod == null) body.enabled = !hidden; }
        }

        private float Now => Time.timeSinceLevelLoad; // the shader's _Time.y

        private void StartPose()
        {
            fade = Vector4.zero;
            swing.w = -1f;
            if (dead && bake.Death.Valid) { locoClip = bake.Death; loco = bake.Death.Play(Now - bake.Death.Length); }
            else
            {
                locoClip = LocomotionClip();
                loco = locoClip.Play(Now - Random.value * locoClip.Length); // crowds out of step
            }
            prev = loco;
            Push();
        }

        private GpuSkinBake.Clip LocomotionClip()
        {
            float speed = driver != null ? driver.Speed : 0f;
            return speed < WalkSpeed ? bake.Idle : speed < RunSpeed ? bake.Walk : bake.Run;
        }

        private void FollowLocomotion()
        {
            GpuSkinBake.Clip clip = LocomotionClip();
            if (clip.FirstRow == locoClip.FirstRow) return;
            CrossFade(clip, Now);
        }

        private void CrossFade(GpuSkinBake.Clip clip, float start)
        {
            prev = loco;
            locoClip = clip;
            loco = clip.Play(start);
            fade = new Vector4(Now, FadeTime, 0f, 0f);
            Push();
        }

        private void HandleAttack()
        {
            if (!IsFar || dead || !bake.Attack.Valid) return;
            swing = bake.Attack.Play(Now);
            swing.y = -Mathf.Abs(swing.y); // played once
            Push();
        }

        private void HandleDeath()
        {
            dead = true;
            if (!IsFar || !bake.Death.Valid) return;
            swing.w = -1f;
            CrossFade(bake.Death, Now);
        }

        private void Push()
        {
            if (gpuRenderer == null) return;
            block.SetVector(LocoId, loco);
            block.SetVector(PrevId, prev);
            block.SetVector(SwingId, swing);
            block.SetVector(FadeId, fade);
            gpuRenderer.SetPropertyBlock(block);
        }
    }
}
