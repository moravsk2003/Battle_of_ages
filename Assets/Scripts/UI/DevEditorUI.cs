using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using BattleOfAges.Resources;
using BattleOfAges.RTS;
using BattleOfAges.Utils;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The tester's editor (KNOWN_ISSUES №21): endless resources, units of any rank made on the spot, own or the nearest
    /// kingdom's, units handed from one side to the other, trees planted. For trying things out, not for play: it is off
    /// in a game with others (each machine runs its own world, an editor on one would split them).
    /// </summary>
    public static class DevTools
    {
        /// <summary>Stores kept full while on (DevEditorUI tops them up).</summary>
        public static bool EndlessResources;

        public const int TopUpTo = 9999, OreTiers = 5;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => EndlessResources = false;

        /// <summary>Not with others: the editor would make this machine's world differ from theirs.</summary>
        public static bool Allowed => Net.NetSession.Current == null;

        public static void TopUp()
        {
            ResourceManager r = ResourceManager.Instance;
            if (r == null) return;
            if (r.WoodCount < TopUpTo) r.AddResource(ResourceType.Wood, TopUpTo - r.WoodCount);
            for (int tier = 1; tier <= OreTiers; tier++)
                if (r.GetOreTierCount(tier) < TopUpTo) r.AddOre(tier, TopUpTo - r.GetOreTierCount(tier));
            if (r.Food < TopUpTo) r.SetFood(TopUpTo);
        }

        /// <summary>Where the editor puts things: a few metres before the hero, on the ground.</summary>
        public static Vector3 Spot(float ahead = 6f)
        {
            GameObject hero = GameContext.Player;
            Vector3 p = hero != null ? hero.transform.position + Flat(hero.transform.forward) * ahead : Vector3.zero;
            p.y = RtsUnit.GetTerrainHeight(p);
            return p;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.001f ? v.normalized : Vector3.forward; }

        /// <summary>An own unit of this rank (a worker who has risen to it).</summary>
        public static GameObject SpawnOwn(UnitRank rank, Vector3 at)
        {
            GameObject unit = BuildingNode.CreateWorkerNpc(at, BuildingNode.NextVillagerName());
            UnitProgress progress = unit.GetComponent<UnitProgress>();
            if (progress == null) progress = UnitProgress.Setup(unit, UnitRank.Person);
            for (int i = 0; i < 4 && progress.Rank < rank; i++) progress.Promote();
            return unit;
        }

        /// <summary>A unit of the nearest kingdom (a kingdom of the editor's own if there is none near).</summary>
        public static HostileMobAI SpawnEnemy(UnitRank rank, Vector3 at)
        {
            NpcKingdom k = NearestKingdom(at, 600f);
            if (k == null) { k = NpcKingdom.Found(at + new Vector3(30f, 0f, 30f), Loc.Pick("Редактор", "Editor"), 0); }
            return k.AddSubject(rank, at);
        }

        /// <summary>These own units go over to the nearest kingdom, each as its rank; how many went.</summary>
        public static int GiveToEnemy(IEnumerable<RtsUnit> units)
        {
            var list = new List<RtsUnit>();
            foreach (RtsUnit u in units) if (u != null) list.Add(u);
            int moved = 0;
            foreach (RtsUnit u in list)
            {
                Vector3 at = u.transform.position;
                UnitProgress p = u.GetComponent<UnitProgress>();
                SpawnEnemy(p != null ? p.Rank : UnitRank.Person, at);
                Object.Destroy(u.gameObject);
                moved++;
            }
            return moved;
        }

        /// <summary>The nearest kingdom's unit near here comes over to the player, its rank kept; false if there is none.</summary>
        public static bool TakeNearestEnemy(Vector3 from, float radius)
        {
            HostileMobAI best = null;
            float bestD = radius * radius;
            foreach (NpcKingdom k in NpcKingdom.All)
            {
                if (k == null || k.IsDefeated) continue;
                foreach (HostileMobAI m in k.Subjects)
                {
                    if (m == null || m.Health == null || !m.Health.IsAlive) continue;
                    float d = (m.transform.position - from).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = m; }
                }
            }
            if (best == null) return false;
            UnitProgress p = best.GetComponent<UnitProgress>();
            UnitRank rank = p != null ? p.Rank : UnitRank.Person;
            Vector3 at = best.transform.position;
            best.Health.TakeDamage(int.MaxValue / 2, ignoreArmor: true); // (its kingdom loses it as a fallen one)
            Object.Destroy(best.gameObject);
            SpawnOwn(rank, at);
            return true;
        }

        /// <summary>A tree to fell, here (not kept in a save: trees come from the world's seed).</summary>
        public static GameObject PlantTree(Vector3 at)
        {
            GameObject tree = SteppeTreeBuilder.CreateSteppeTree("EditorTree", Random.Range(0, 3));
            tree.transform.position = at;
            return tree;
        }

        private static NpcKingdom NearestKingdom(Vector3 at, float radius)
        {
            NpcKingdom best = null;
            float bestD = radius * radius;
            foreach (NpcKingdom k in NpcKingdom.All)
            {
                if (k == null || k.IsDefeated) continue;
                float d = (k.Home - at).sqrMagnitude;
                if (d < bestD) { bestD = d; best = k; }
            }
            return best;
        }
    }

    /// <summary>The editor's window (F8): DevTools' buttons, and the stores kept full while asked.</summary>
    public sealed class DevEditorUI : MonoBehaviour
    {
        private static DevEditorUI instance;
        private GameObject window;
        private UnityEngine.UI.Text endlessLabel, status;
        private bool open;
        private float nextTopUp;

        public static bool IsVisible => instance != null && instance.open;
        public static DevEditorUI Instance => instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance == null) instance = new GameObject("DevEditorUI").AddComponent<DevEditorUI>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            Build();
            window.SetActive(false);
            DontDestroyOnLoad(gameObject);
            Loc.Changed += OnLanguageChanged;
        }

        private void OnDestroy()
        {
            Loc.Changed -= OnLanguageChanged;
            if (instance == this) instance = null;
        }

        private void OnLanguageChanged()
        {
            if (endlessLabel != null) endlessLabel.text = EndlessText();
        }

        public void Open()
        {
            open = true;
            window.SetActive(true);
            Say(DevTools.Allowed ? "" : Loc.Pick("У грі з іншими редактор вимкнено", "Editor disabled in multiplayer"));
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Close()
        {
            open = false;
            window.SetActive(false);
            GameModeManager modes = GameContext.Modes;
            if (!GameWindows.AnyModalOpen && !GameWindows.Paused && (modes == null || modes.CurrentMode == GameMode.FirstPerson))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void Update()
        {
            if (DevTools.EndlessResources && DevTools.Allowed && Time.unscaledTime >= nextTopUp)
            {
                nextTopUp = Time.unscaledTime + 0.5f;
                DevTools.TopUp();
            }
            if (Time.frameCount < 2) return;
            bool f8 = UniversalInput.IsKeyDown(KeyCode.F8);
            if (open) { if (f8) Close(); return; }
            if (f8 && !GameWindows.Paused && GameContext.Player != null)
            {
                GameWindows.CloseAllModals();
                Open();
            }
        }

        private void Say(string text) { if (status != null) status.text = text; }

        private void Do(System.Func<string> act)
        {
            if (!DevTools.Allowed) { Say(Loc.Pick("У грі з іншими редактор вимкнено", "Editor disabled in multiplayer")); return; }
            Say(act());
        }

        private void Build()
        {
            Canvas canvas = UiTheme.CreateCanvas("DevEditorCanvas", 126);
            canvas.transform.SetParent(transform, false);
            window = canvas.gameObject;
            RectTransform panel = UiTheme.Rect(canvas.transform, "Window", new Vector2(0f, 0.5f), new Vector2(250f, 0f), new Vector2(460f, 620f));
            UiTheme.PanelImage(panel);
            RectTransform head = UiTheme.Rect(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(400f, 40f));
            UiTheme.Label(head, "Text", "Редактор (F8)", 26, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            UiTheme.TextButton(panel, "Close", "×", new Vector2(1f, 1f), new Vector2(-12f, -10f), new Vector2(40f, 38f), Close, 24);

            float y = -64f;
            UnityEngine.UI.Button endless = Row(panel, ref y, "Безкінечні ресурси", () =>
            {
                DevTools.EndlessResources = !DevTools.EndlessResources;
                if (DevTools.EndlessResources) DevTools.TopUp();
                endlessLabel.text = EndlessText();
                return DevTools.EndlessResources
                    ? Loc.Pick("Склад тепер повний завжди", "Stores now stay full")
                    : Loc.Pick("Склад знову звичайний", "Stores back to normal");
            });
            endlessLabel = endless.GetComponentInChildren<UnityEngine.UI.Text>();
            LocText.Detach(endlessLabel);
            endlessLabel.text = EndlessText();

            y -= 6f;
            Caption(panel, ref y, "Свій юніт перед героєм");
            Pair(panel, ref y, "Людина", () => Own(UnitRank.Person, "людина", "villager"), "Воїн", () => Own(UnitRank.Warrior, "воїн", "warrior"));
            Pair(panel, ref y, "Лицар", () => Own(UnitRank.Knight, "лицар", "knight"), "Генерал", () => Own(UnitRank.General, "генерал", "general"));
            Caption(panel, ref y, "Ворог (найближче королівство)");
            Pair(panel, ref y, "Воїн", () => Enemy(UnitRank.Warrior, "воїн", "warrior"), "Лицар", () => Enemy(UnitRank.Knight, "лицар", "knight"));
            Caption(panel, ref y, "Перейти на інший бік");
            Row(panel, ref y, "Обрані на мапі → до ворога", () =>
            {
                RtsUnitCommander commander = GameContext.Commander;
                int n = commander != null ? DevTools.GiveToEnemy(commander.SelectedUnits) : 0;
                return n > 0
                    ? Loc.Pick($"До ворога перейшло: {n}", $"Switched to enemy: {n}")
                    : Loc.Pick("Спершу оберіть своїх на мапі (M)", "First select allies on the map (M)");
            });
            Row(panel, ref y, "Найближчий ворог → мій", () =>
                GameContext.Player != null && DevTools.TakeNearestEnemy(GameContext.Player.transform.position, 40f)
                    ? Loc.Pick("Ворог перейшов до вас", "Enemy switched to your side")
                    : Loc.Pick("Ворогів ближче 40 м немає", "No enemies within 40 m"));
            Caption(panel, ref y, "Світ");
            Row(panel, ref y, "Посадити дерево", () =>
            {
                DevTools.PlantTree(DevTools.Spot(5f));
                return Loc.Pick("Дерево посаджено (у збереження не потрапить)", "Tree planted (won't persist in saves)");
            });

            RectTransform st = UiTheme.Rect(panel, "Status", new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(420f, 50f));
            status = UiTheme.Label(st, "Text", "", 16, TextAnchor.MiddleCenter, UiTheme.Ink);
            LocText.Detach(status);
        }

        private static string EndlessText() => Loc.Pick(
            "Безкінечні ресурси: " + (DevTools.EndlessResources ? "так" : "ні"),
            "Infinite resources: " + (DevTools.EndlessResources ? "Yes" : "No"));

        private string Own(UnitRank rank, string nameUk, string nameEn)
        {
            DevTools.SpawnOwn(rank, DevTools.Spot());
            return Loc.Pick($"Свій: {nameUk}", $"Ally: {nameEn}");
        }

        private string Enemy(UnitRank rank, string nameUk, string nameEn)
        {
            DevTools.SpawnEnemy(rank, DevTools.Spot(12f));
            return Loc.Pick($"Ворог: {nameUk}", $"Enemy: {nameEn}");
        }

        private void Caption(RectTransform panel, ref float y, string text)
        {
            RectTransform r = UiTheme.Rect(panel, "Caption", new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(420f, 28f));
            UiTheme.Label(r, "Text", text, 17, TextAnchor.MiddleLeft, UiTheme.Gold, FontStyle.Bold);
            y -= 30f;
        }

        private UnityEngine.UI.Button Row(RectTransform panel, ref float y, string label, System.Func<string> act)
        {
            UnityEngine.UI.Button b = UiTheme.TextButton(panel, "Row", label, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(420f, 42f), () => Do(act), 17);
            y -= 48f;
            return b;
        }

        private void Pair(RectTransform panel, ref float y, string a, System.Func<string> actA, string b, System.Func<string> actB)
        {
            UiTheme.TextButton(panel, "A", a, new Vector2(0.5f, 1f), new Vector2(-106f, y), new Vector2(206f, 42f), () => Do(actA), 17);
            UiTheme.TextButton(panel, "B", b, new Vector2(0.5f, 1f), new Vector2(106f, y), new Vector2(206f, 42f), () => Do(actB), 17);
            y -= 48f;
        }
    }
}
