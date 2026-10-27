using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Combat;
using BattleOfAges.Managers;
using BattleOfAges.RTS;
using BattleOfAges.Utils;

namespace BattleOfAges.UI
{
    /// <summary>
    /// What a hit looks like on screen (docs/GAME_DESIGN.md §21, stage 2.7): a number rising over whoever was hit near
    /// the camera (red when it is ours), a red vignette that pulses when the hero is low and flashes when hit, and a
    /// hit marker round the crosshair when the hero's own blow lands.
    /// </summary>
    public sealed class CombatFeedback : MonoBehaviour
    {
        public const int MaxNumbers = 40;
        public const float NumberSeconds = 0.9f, NearFirstPerson = 40f, NearMap = 150f;

        private RectTransform root, numbers, hitMarker;
        private Image vignette;
        private GameModeManager modes;
        private HealthComponent player;
        private CombatUnit playerCombat;
        private float flash, hitUntil;
        private readonly List<(RectTransform rt, Text text, Vector3 world, float born)> live = new List<(RectTransform, Text, Vector3, float)>();
        private readonly Stack<(RectTransform rt, Text text)> pool = new Stack<(RectTransform, Text)>();

        public int LiveNumbers => live.Count;

        public static CombatFeedback Create(RectTransform hudRoot)
        {
            var go = new GameObject("CombatFeedback", typeof(RectTransform));
            go.transform.SetParent(hudRoot, false);
            go.transform.SetAsFirstSibling(); // under the HUD panels
            CombatFeedback f = go.AddComponent<CombatFeedback>();
            f.Build((RectTransform)go.transform);
            return f;
        }

        private void Build(RectTransform r)
        {
            root = r;
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            RectTransform v = UiTheme.Child(root, "Vignette", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            vignette = v.gameObject.AddComponent<Image>();
            vignette.sprite = VignetteSprite(); vignette.raycastTarget = false; vignette.color = new Color(0.75f, 0.04f, 0.02f, 0f);
            numbers = UiTheme.Child(root, "Numbers", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            hitMarker = UiTheme.Rect(root, "HitMarker", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44f, 44f));
            foreach (Vector2 d in new[] { new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f) })
            {
                RectTransform t = UiTheme.Rect(hitMarker, "Tick", new Vector2(0.5f, 0.5f), d * 11f, new Vector2(10f, 2.5f));
                t.localEulerAngles = new Vector3(0f, 0f, d.x * d.y > 0f ? 45f : -45f);
                Image img = t.gameObject.AddComponent<Image>();
                img.sprite = UiTheme.BarSprite; img.color = new Color(1f, 0.95f, 0.85f, 0.95f); img.raycastTarget = false;
            }
            hitMarker.gameObject.SetActive(false);
        }

        private void OnEnable() => HealthComponent.AnyDamaged += OnHit;
        private void OnDisable() => HealthComponent.AnyDamaged -= OnHit;

        private void FindPlayer()
        {
            if (player != null) return;
            GameObject p = Managers.GameContext.Player;
            if (p == null) return;
            player = p.GetComponent<HealthComponent>();
            playerCombat = p.GetComponent<CombatUnit>();
        }

        private bool Map => modes != null && modes.CurrentMode == GameMode.RtsMap;

        private void OnHit(HealthComponent target, int damage)
        {
            if (target == null || root == null) return;
            FindPlayer();
            if (modes == null) modes = Managers.GameContext.Modes;
            if (target == player) flash = 1f;
            if (playerCombat != null && target.LastAttacker == playerCombat && target != player) hitUntil = Time.unscaledTime + 0.15f;

            Camera cam = ViewCamera.Current;
            if (cam == null) return;
            Vector3 at = target.transform.position + Vector3.up * 2.1f;
            float near = Map ? NearMap : NearFirstPerson;
            if ((at - cam.transform.position).sqrMagnitude > near * near) return;
            Vector3 sp = cam.WorldToScreenPoint(at);
            if (sp.z <= 0f) return;
            bool ours = target == player || (target.TryGetComponent(out CombatUnit c) && c.Team == UnitSpatialGrid.TeamPlayer && target.TryGetComponent(out RtsUnit _))
                        || target.GetComponentInParent<Building.BuildingNode>() != null;
            Spawn(damage, ours, at + new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f)));
        }

        /// <summary>A number at a world point (also for tests).</summary>
        public void Spawn(int damage, bool ours, Vector3 world)
        {
            if (live.Count >= MaxNumbers) Recycle(0);
            (RectTransform rt, Text text) n;
            if (pool.Count > 0) n = pool.Pop();
            else
            {
                RectTransform rt = UiTheme.Rect(numbers, "Damage", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 40f));
                Text t = UiTheme.Label(rt, "Text", "", 24, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);
                t.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
                n = (rt, t);
            }
            n.rt.gameObject.SetActive(true);
            n.text.text = damage.ToString();
            n.text.color = ours ? new Color(1f, 0.38f, 0.3f) : new Color(1f, 0.96f, 0.86f);
            n.text.fontSize = damage >= 100 ? 30 : damage >= 30 ? 26 : 22;
            live.Add((n.rt, n.text, world, Time.unscaledTime));
        }

        private void Recycle(int i)
        {
            live[i].rt.gameObject.SetActive(false);
            pool.Push((live[i].rt, live[i].text));
            live.RemoveAt(i);
        }

        private void LateUpdate()
        {
            Camera cam = ViewCamera.Current;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - live[i].born;
                if (age > NumberSeconds || cam == null) { Recycle(i); continue; }
                Vector3 sp = cam.WorldToScreenPoint(live[i].world);
                if (sp.z <= 0f) { live[i].rt.gameObject.SetActive(false); continue; }
                live[i].rt.gameObject.SetActive(true);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(numbers, sp, null, out Vector2 local);
                float rise = 70f * (1f - (1f - age / NumberSeconds) * (1f - age / NumberSeconds));
                live[i].rt.anchoredPosition = local + new Vector2(0f, rise);
                float a = Mathf.Clamp01((NumberSeconds - age) / 0.35f);
                Color c = live[i].text.color; c.a = a; live[i].text.color = c;
                live[i].rt.localScale = Vector3.one * (age < 0.08f ? Mathf.Lerp(1.4f, 1f, age / 0.08f) : 1f);
            }

            // The vignette and the hit marker, first person only
            FindPlayer();
            if (modes == null) modes = Managers.GameContext.Modes;
            bool fp = !Map;
            float low = 0f;
            if (fp && player != null && player.IsAlive && player.MaxHealth > 0)
            {
                float f = player.CurrentHealth / (float)player.MaxHealth;
                if (f < 0.35f) low = Mathf.Lerp(0.55f, 0.2f, f / 0.35f) * (0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 6f));
            }
            flash = Mathf.MoveTowards(flash, 0f, Time.unscaledDeltaTime * 3.5f);
            Color vc = vignette.color; vc.a = fp ? Mathf.Max(low, flash * 0.4f) : 0f; vignette.color = vc;
            bool marker = fp && Time.unscaledTime < hitUntil;
            if (hitMarker.gameObject.activeSelf != marker) hitMarker.gameObject.SetActive(marker);
        }

        private static Sprite vignetteSprite;
        private static Sprite VignetteSprite()
        {
            if (vignetteSprite != null) return vignetteSprite;
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "UI_Vignette" };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(u * u * 0.8f + v * v);
                float a = Mathf.Clamp01((r - 0.55f) / 0.6f);
                px[x + y * N] = new Color(1f, 1f, 1f, a * a);
            }
            tex.SetPixels(px);
            tex.Apply();
            return vignetteSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
