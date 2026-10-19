using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Utils;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// A flying arrow of a bow shot (docs/GAME_DESIGN.md §11.1): it leaves the bow when the draw (the wind-up) is over and
    /// the hit lands when it arrives, on a low arc after the target; a piercing arrow then goes on through. An arrow whose
    /// target fell meanwhile flies on to where it was and sticks in the ground for a while. Pooled (one shared mesh and
    /// material): a battle of hundreds of archers does not allocate per shot.
    /// </summary>
    public sealed class ArrowShot : MonoBehaviour
    {
        public const float Speed = 45f, Length = 0.8f, StuckSeconds = 4f, MaxLife = 3f;
        /// <summary>Height of the arc for each metre of the shot.</summary>
        public const float ArcPerMetre = 0.04f;

        private static readonly Stack<ArrowShot> pool = new Stack<ArrowShot>();
        private static Mesh mesh;

        private CombatUnit shooter;
        private HealthComponent target;
        private int damage;
        private bool piercing;
        private Vector3 from, aim;
        private float flight, t, stuckFor;
        private bool stuck;

        /// <summary>Shoots: the hit is delivered on arrival (edit-mode tests: at once).</summary>
        public static void Fire(CombatUnit shooter, HealthComponent target, int damage, bool piercing)
        {
            if (!Application.isPlaying) { Hit(shooter, target, damage, piercing); return; }
            ArrowShot a = null;
            while (a == null && pool.Count > 0) a = pool.Pop(); // (a scene change destroys the pooled ones)
            if (a == null) a = Create();
            if (a == null) { Hit(shooter, target, damage, piercing); return; }
            a.gameObject.SetActive(true);
            a.shooter = shooter;
            a.target = target;
            a.damage = damage;
            a.piercing = piercing;
            a.from = shooter.transform.position + Vector3.up * 1.45f + shooter.transform.forward * 0.4f;
            a.aim = Chest(target);
            a.flight = Mathf.Max(0.05f, Vector3.Distance(a.from, a.aim) / Speed);
            a.t = 0f;
            a.stuck = false;
            a.transform.position = a.from;
            a.transform.rotation = Quaternion.LookRotation(a.aim - a.from);
        }

        private static Vector3 Chest(HealthComponent h) => h.transform.position + Vector3.up * 1.2f;

        private static void Hit(CombatUnit shooter, HealthComponent target, int damage, bool piercing)
        {
            if (target == null || !target.IsAlive) return;
            if (shooter == null) { target.TakeDamage(damage); return; }
            shooter.DeliverHit(target, damage, ignoreArmor: false);
            if (piercing) shooter.PierceBehind(target, Mathf.RoundToInt(damage / GameBalance.PierceShotMultiplier)); // on through (shares of a normal shot)
        }

        private static ArrowShot Create()
        {
            Material look = MeshKit.PropMaterial();
            if (look == null) return null; // no shader: the shot just lands at once
            var go = new GameObject("Arrow");
            go.AddComponent<MeshFilter>().sharedMesh = Mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = look;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.AddComponent<ArrowShot>();
        }

        /// <summary>Shaft along +Z (tip at the front), steel head, three fletchings at the back.</summary>
        public static Mesh Mesh
        {
            get
            {
                if (mesh != null) return mesh;
                var kit = new MeshKit();
                Vector3 woodS = new Vector3(4f, 0f, 0.22f), steelS = new Vector3(3f, 0.9f, 0.7f), clothS = new Vector3(1f, 0f, 0.15f);
                kit.Box(Matrix4x4.identity, new Vector3(0.018f, 0.018f, Length), 0.004f, woodS, _ => new Color(0.55f, 0.40f, 0.24f));
                kit.Box(Matrix4x4.TRS(new Vector3(0f, 0f, Length * 0.5f + 0.04f), Quaternion.Euler(0f, 0f, 45f), Vector3.one), new Vector3(0.035f, 0.035f, 0.09f), 0.012f, steelS, _ => new Color(0.75f, 0.77f, 0.8f));
                for (int i = 0; i < 3; i++)
                    kit.Box(Matrix4x4.TRS(new Vector3(0f, 0f, -Length * 0.5f + 0.08f), Quaternion.Euler(0f, 0f, i * 120f), Vector3.one) * Matrix4x4.Translate(new Vector3(0f, 0.025f, 0f)),
                            new Vector3(0.004f, 0.04f, 0.14f), 0.001f, clothS, _ => new Color(0.85f, 0.85f, 0.8f));
                mesh = kit.ToMesh("Arrow");
                return mesh;
            }
        }

        private void Update()
        {
            if (stuck)
            {
                stuckFor += Time.deltaTime;
                if (stuckFor >= StuckSeconds) Release();
                return;
            }
            if (target != null && target.IsAlive) aim = Chest(target); // it follows a moving target (the hit was decided at release)
            t += Time.deltaTime / flight;
            if (t >= 1f)
            {
                if (target != null && target.IsAlive) { Hit(shooter, target, damage, piercing); Release(); return; }
                Stick();
                return;
            }
            Vector3 prev = transform.position;
            float arc = Vector3.Distance(from, aim) * ArcPerMetre;
            Vector3 p = Vector3.Lerp(from, aim, t) + Vector3.up * (4f * arc * t * (1f - t));
            transform.position = p;
            if ((p - prev).sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(p - prev);
            if (t * flight > MaxLife) Release();
        }

        /// <summary>Its target is gone: the arrow ends in the ground where the target stood.</summary>
        private void Stick()
        {
            stuck = true;
            stuckFor = 0f;
            Vector3 p = aim;
            p.y = RTS.RtsUnit.GetTerrainHeight(p) + 0.25f;
            transform.position = p;
            transform.rotation = Quaternion.LookRotation((aim - from).normalized + Vector3.down * 0.8f);
        }

        private void Release()
        {
            shooter = null;
            target = null;
            gameObject.SetActive(false);
            pool.Push(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { pool.Clear(); mesh = null; }
    }
}
