using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.Managers;
using BattleOfAges.Player;
using BattleOfAges.Resources;
using BattleOfAges.RTS;
using BattleOfAges.CameraSystem;
using BattleOfAges.InputSystem;
using BattleOfAges.UI;

namespace BattleOfAges.SaveLoad
{
    /// <summary>
    /// Saves and loads the game: slots 1..3 and the autosave (slot 0, also F5 / F9), JSON files in
    /// persistentDataPath/saves. A load (or a new game) reloads the game scene and then applies the save: resources,
    /// armory, buildings (rebuilt by type), units (villagers rebuilt, other own units matched by name), the hero and
    /// the world changes (WorldPersistence). Autosave every 5 minutes of play.
    /// </summary>
    public static class SaveSystem
    {
        public const string GameScene = "SampleScene";
        public const string MenuScene = "MainMenu";
        public const int ManualSlots = 3;
        public const int AutoSlot = 0;
        public const float AutosaveSeconds = 300f;

        private static SaveData pending;
        private static bool newGamePending;
        /// <summary>Autosave runs for games started from the menu (not when the scene is opened directly: editor, tests).</summary>
        public static bool AutosaveEnabled { get; private set; }
        private static float playSecondsAtLoad, sceneStartTime;

        /// <summary>Tests point this at a temporary folder so they never touch the player's saves.</summary>
        public static string FolderOverride { get; set; }
        public static string Folder => !string.IsNullOrEmpty(FolderOverride) ? FolderOverride : Path.Combine(Application.persistentDataPath, "saves");
        public static string PathOf(int slot) => Path.Combine(Folder, slot == AutoSlot ? "auto.json" : $"slot{slot}.json");
        public static string SlotName(int slot) => slot == AutoSlot ? "Автозбереження" : $"Слот {slot}";
        public static float PlaySeconds => playSecondsAtLoad + (Time.unscaledTime - sceneStartTime);
        public static bool InGame => SceneManager.GetActiveScene().name == GameScene;

        // ------------------------------------------------------------------ slots

        public static SaveMeta Meta(int slot)
        {
            var meta = new SaveMeta { Slot = slot };
            SaveData d = Read(slot);
            if (d == null) return meta;
            meta.Exists = true;
            meta.SavedAt = new DateTime(d.SavedAtTicks, DateTimeKind.Utc).ToLocalTime();
            meta.PlaySeconds = d.PlaySeconds;
            meta.Wood = d.Wood;
            meta.Ore = d.OreTotal;
            meta.Units = d.Units.Count;
            meta.Buildings = d.Buildings.Count;
            return meta;
        }

        /// <summary>The most recently written slot (manual or auto), -1 when there is none.</summary>
        public static int LatestSlot()
        {
            int best = -1;
            DateTime when = DateTime.MinValue;
            for (int s = 0; s <= ManualSlots; s++)
            {
                string p = PathOf(s);
                if (!File.Exists(p)) continue;
                DateTime t = File.GetLastWriteTimeUtc(p);
                if (t > when) { when = t; best = s; }
            }
            return best;
        }

        public static void Delete(int slot)
        {
            try { if (File.Exists(PathOf(slot))) File.Delete(PathOf(slot)); }
            catch (Exception e) { Debug.LogWarning("[Save] delete failed: " + e.Message); }
        }

        private static SaveData Read(int slot)
        {
            try
            {
                string p = PathOf(slot);
                return File.Exists(p) ? JsonUtility.FromJson<SaveData>(File.ReadAllText(p)) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] slot {slot} unreadable: {e.Message}");
                return null;
            }
        }

        // ------------------------------------------------------------------ save

        public static bool Save(int slot)
        {
            if (!InGame) return false;
            try
            {
                SaveData d = Capture();
                Directory.CreateDirectory(Folder);
                string tmp = PathOf(slot) + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(d));
                if (File.Exists(PathOf(slot))) File.Delete(PathOf(slot));
                File.Move(tmp, PathOf(slot));
                StrategyHud.Notify(slot == AutoSlot ? "Гру збережено (авто)" : $"Гру збережено: {SlotName(slot)}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[Save] failed: " + e);
                StrategyHud.Notify("Не вдалося зберегти гру");
                return false;
            }
        }

        public static SaveData Capture()
        {
            var d = new SaveData { SavedAtTicks = DateTime.UtcNow.Ticks, PlaySeconds = PlaySeconds, GameHours = WorldClock.GameHours };

            ResourceManager res = ResourceManager.Instance;
            if (res != null)
            {
                d.Wood = res.WoodCount;
                foreach (var p in res.OreTiers()) d.Ore.Add(new TierCount(p.Key, p.Value));
            }
            d.Armory.AddRange(Armory.Instance.Items);

            // Player
            PlayerCombatController player = UnityEngine.Object.FindFirstObjectByType<PlayerCombatController>();
            if (player != null)
            {
                d.Player.Position = player.transform.position;
                d.Player.Yaw = player.transform.eulerAngles.y;
                HealthComponent hp = player.GetComponent<HealthComponent>();
                d.Player.Health = hp != null ? hp.CurrentHealth : 0;
                d.Player.Gear = player.Gear.SlotsCopy();
                HeroProgress hero = player.GetComponent<HeroProgress>();
                if (hero != null)
                {
                    d.Player.Level = hero.Level; d.Player.Xp = hero.Xp;
                    d.Player.AttributePoints = hero.AttributePoints; d.Player.SkillPoints = hero.SkillPoints;
                    d.Player.StrengthPoints = hero.StrengthPoints; d.Player.HealthPoints = hero.HealthPoints;
                }
            }
            GameModeManager modes = UnityEngine.Object.FindFirstObjectByType<GameModeManager>();
            if (modes != null) d.Player.Mode = (int)modes.CurrentMode;
            var mapCam = UnityEngine.Object.FindFirstObjectByType<RtsMapCameraController>();
            if (mapCam != null) { d.Player.HasMapCamera = true; d.Player.MapCameraPosition = mapCam.transform.position; }

            // Buildings (index = their place in the list, units refer to it)
            var buildings = new List<BuildingNode>();
            foreach (BuildingNode b in BuildingNode.Active)
            {
                if (b == null || b.name.StartsWith("BuildGhost")) continue;
                buildings.Add(b);
                var s = new BuildingSave { Type = (int)b.Type, Name = b.name, Position = b.transform.position, Yaw = b.transform.eulerAngles.y, SceneKey = b.SceneKey };
                HealthComponent hp = b.GetComponent<HealthComponent>();
                s.Health = hp != null ? hp.CurrentHealth : 0;
                MineNode mine = b.GetComponent<MineNode>();
                if (mine != null) foreach (var p in mine.OreStock()) s.MineOre.Add(new TierCount(p.Key, p.Value));
                TownHall hall = b.GetComponent<TownHall>();
                if (hall != null) s.Level = hall.Level;
                WorkbenchNode bench = b.GetComponent<WorkbenchNode>();
                if (bench != null)
                {
                    foreach (CraftingTask t in bench.Queue.Queue) s.Queue.Add(new TaskSave { Type = (int)t.Type, Level = t.Level, Progress = t.ProgressSeconds });
                    s.BenchWorkers = bench.AssignedWorkerCount;
                }
                d.Buildings.Add(s);
            }

            // Own units
            foreach (RtsUnit u in UnityEngine.Object.FindObjectsByType<RtsUnit>(FindObjectsSortMode.None))
            {
                if (u == null || u.GetComponent<FirstPersonController>() != null) continue;
                HealthComponent hp = u.GetComponent<HealthComponent>();
                if (hp != null && hp.MaxHealth > 0 && !hp.IsAlive) continue;
                NpcResourceGatherer g = u.GetComponent<NpcResourceGatherer>();
                Equipment gear = u.GetComponent<Equipment>();
                UnitProgress progress = u.GetComponent<UnitProgress>();
                var s = new UnitSave
                {
                    Name = u.name,
                    Villager = IsVillager(u),
                    Position = u.transform.position,
                    Yaw = u.transform.eulerAngles.y,
                    Health = hp != null ? hp.CurrentHealth : 0,
                    Gear = gear != null ? gear.SlotsCopy() : new Item[6],
                    Directive = g != null ? (int)g.CurrentDirective : 0,
                    Rank = progress != null ? (int)progress.Rank : -1,
                    BattleXp = progress != null ? progress.BattleXp : 0
                };
                if (g != null && g.IsInsideMine && g.AssignedMine != null)
                    s.InsideMine = buildings.FindIndex(b => b.gameObject == g.AssignedMine.gameObject);
                for (int i = 0; i < buildings.Count; i++)
                {
                    WorkbenchNode bench = buildings[i].GetComponent<WorkbenchNode>();
                    if (bench != null && ContainsUnit(bench.Workers, u)) s.AtBench = i;
                }
                d.Units.Add(s);
            }

            d.World = WorldPersistence.Capture();
            return d;
        }

        private static bool ContainsUnit(IReadOnlyList<RtsUnit> list, RtsUnit u)
        {
            foreach (RtsUnit x in list) if (x == u) return true;
            return false;
        }

        // ------------------------------------------------------------------ load / new game

        public static bool Load(int slot)
        {
            SaveData d = Read(slot);
            if (d == null) return false;
            pending = d;
            newGamePending = false;
            AutosaveEnabled = true;
            WorldPersistence.Suspended = true;
            WorldPersistence.Load(d.World);
            Time.timeScale = 1f;
            SceneManager.LoadScene(GameScene);
            return true;
        }

        public static void NewGame()
        {
            pending = null;
            newGamePending = true;
            AutosaveEnabled = true;
            WorldPersistence.Suspended = true;
            WorldPersistence.Clear();
            Time.timeScale = 1f;
            SceneManager.LoadScene(GameScene);
        }

        public static void ToMainMenu()
        {
            pending = null;
            WorldPersistence.Suspended = true;
            Time.timeScale = 1f;
            SceneManager.LoadScene(MenuScene);
        }

        // Called by SaveRunner when the game scene has loaded (before any Start).
        internal static void OnGameSceneLoaded(MonoBehaviour runner)
        {
            // Buildings that come with the scene get a key (name + place) before anything moves them
            foreach (BuildingNode b in BuildingNode.Active)
                if (b != null && !b.name.StartsWith("BuildGhost"))
                    b.SceneKey = b.name + "@" + Vector3Int.RoundToInt(b.transform.position);
            WorldPersistence.Suspended = false;
            sceneStartTime = Time.unscaledTime;
            if (pending != null)
            {
                playSecondsAtLoad = pending.PlaySeconds;
                WorldClock.GameHours = pending.GameHours;
                runner.StartCoroutine(ApplyWhenReady(pending));
                pending = null;
                return;
            }
            playSecondsAtLoad = 0f;
            if (!newGamePending) WorldPersistence.Clear(); // the scene was opened directly (editor, tests)
            if (newGamePending && ResourceManager.Instance != null) ResourceManager.Instance.ResetToInitial();
            newGamePending = false;
            runner.StartCoroutine(RegisterSceneTreesLater());
        }

        private static IEnumerator RegisterSceneTreesLater()
        {
            yield return null;
            WorldPersistence.RegisterSceneTrees();
        }

        private static IEnumerator ApplyWhenReady(SaveData d)
        {
            yield return null; // every Start has run (starting kits, singletons)
            yield return null;
            try { Apply(d); }
            catch (Exception e) { Debug.LogError("[Save] load failed: " + e); }
            WorldPersistence.RegisterSceneTrees();
            StrategyHud.Notify("Гру завантажено");
        }

        private static void Apply(SaveData d)
        {
            if (ResourceManager.Instance != null)
            {
                var ore = new List<KeyValuePair<int, int>>();
                foreach (TierCount t in d.Ore) ore.Add(new KeyValuePair<int, int>(t.Tier, t.Count));
                ResourceManager.Instance.SetState(d.Wood, ore);
            }
            Armory.Instance.Clear();
            foreach (Item i in d.Armory) Armory.Instance.Add(i);

            // Buildings: the scene's own keep their objects (only their state is restored; those missing from the save
            // were destroyed), the ones built during play are rebuilt by type
            var sceneBuildings = new Dictionary<string, BuildingNode>();
            foreach (BuildingNode b in new List<BuildingNode>(BuildingNode.Active))
            {
                if (b == null || b.name.StartsWith("BuildGhost")) continue;
                if (!string.IsNullOrEmpty(b.SceneKey) && !sceneBuildings.ContainsKey(b.SceneKey)) sceneBuildings[b.SceneKey] = b;
                else { b.gameObject.SetActive(false); UnityEngine.Object.Destroy(b.gameObject); }
            }
            var kept = new HashSet<string>();
            var built = new List<GameObject>();
            foreach (BuildingSave s in d.Buildings)
            {
                GameObject go;
                if (!string.IsNullOrEmpty(s.SceneKey) && sceneBuildings.TryGetValue(s.SceneKey, out BuildingNode own))
                {
                    go = own.gameObject;
                    kept.Add(s.SceneKey);
                }
                else
                {
                    go = BuildingPlacementSystem.CreateBuilding((BuildingType)s.Type);
                    if (go != null && !string.IsNullOrEmpty(s.Name)) go.name = s.Name;
                    if (go != null) go.transform.SetPositionAndRotation(s.Position, Quaternion.Euler(0f, s.Yaw, 0f));
                }
                built.Add(go);
                if (go == null) continue;
                HealthComponent hp = go.GetComponent<HealthComponent>();
                if (hp != null)
                {
                    if (!kept.Contains(s.SceneKey ?? "")) hp.InitializeHealth(BuildingData.GetDefinition((BuildingType)s.Type).MaxHealth);
                    if (s.Health > 0) hp.SetCurrentHealth(s.Health);
                }
                TownHall hall = go.GetComponent<TownHall>();
                if (hall != null && s.Level > 0) hall.Level = s.Level;
                MineNode mine = go.GetComponent<MineNode>();
                if (mine != null)
                {
                    var stock = new List<KeyValuePair<int, int>>();
                    foreach (TierCount t in s.MineOre) stock.Add(new KeyValuePair<int, int>(t.Tier, t.Count));
                    mine.SetOreStock(stock);
                }
                WorkbenchNode bench = go.GetComponent<WorkbenchNode>();
                if (bench != null)
                {
                    var tasks = new List<(ItemType, int, float)>();
                    foreach (TaskSave t in s.Queue) tasks.Add(((ItemType)t.Type, t.Level, t.Progress));
                    bench.LoadQueue(tasks);
                }
            }
            foreach (var pair in sceneBuildings)
                if (!kept.Contains(pair.Key) && pair.Value != null) { pair.Value.gameObject.SetActive(false); UnityEngine.Object.Destroy(pair.Value.gameObject); }

            // Units: villagers are rebuilt, the rest are the scene's own units (dead ones are removed)
            var savedNames = new HashSet<string>();
            foreach (UnitSave s in d.Units) savedNames.Add(s.Name);
            foreach (RtsUnit u in UnityEngine.Object.FindObjectsByType<RtsUnit>(FindObjectsSortMode.None))
            {
                if (u == null || u.GetComponent<FirstPersonController>() != null) continue;
                if (IsVillager(u) || !savedNames.Contains(u.name)) { u.gameObject.SetActive(false); UnityEngine.Object.Destroy(u.gameObject); }
            }
            foreach (UnitSave s in d.Units)
            {
                GameObject go = s.Villager ? BuildingNode.CreateWorkerNpc(s.Position, s.Name) : FindUnit(s.Name);
                if (go == null) continue;
                Place(go, s.Position, s.Yaw);
                ApplyGear(go, s.Gear);
                if (s.Rank >= 0) UnitProgress.Restore(go, (UnitRank)s.Rank, s.BattleXp);
                HealthComponent hp = go.GetComponent<HealthComponent>();
                if (hp != null && s.Health > 0) hp.SetCurrentHealth(s.Health);
                NpcResourceGatherer g = go.GetComponent<NpcResourceGatherer>();
                if (g != null)
                {
                    g.SetGatherDirective((GatherDirective)s.Directive);
                    if (s.InsideMine >= 0 && s.InsideMine < built.Count && built[s.InsideMine] != null)
                        g.EnterMineNow(built[s.InsideMine].GetComponent<MineNode>());
                }
                if (s.AtBench >= 0 && s.AtBench < built.Count && built[s.AtBench] != null)
                {
                    WorkbenchNode bench = built[s.AtBench].GetComponent<WorkbenchNode>();
                    RtsUnit unit = go.GetComponent<RtsUnit>();
                    if (bench != null && unit != null) bench.AssignWorker(unit);
                }
            }

            // The hero
            PlayerCombatController player = UnityEngine.Object.FindFirstObjectByType<PlayerCombatController>();
            if (player != null)
            {
                Place(player.gameObject, d.Player.Position, d.Player.Yaw);
                player.Gear.LoadSlots(d.Player.Gear);
                HeroProgress hero = player.GetComponent<HeroProgress>();
                if (hero != null) hero.Restore(d.Player.Level, d.Player.Xp, d.Player.AttributePoints, d.Player.SkillPoints, d.Player.StrengthPoints, d.Player.HealthPoints);
                HealthComponent hp = player.GetComponent<HealthComponent>();
                if (hp != null && d.Player.Health > 0) hp.SetCurrentHealth(d.Player.Health);
            }
            var mapCam = UnityEngine.Object.FindFirstObjectByType<RtsMapCameraController>();
            if (mapCam != null && d.Player.HasMapCamera) mapCam.transform.position = d.Player.MapCameraPosition;
            GameModeManager modes = UnityEngine.Object.FindFirstObjectByType<GameModeManager>();
            if (modes != null) modes.SetGameMode((GameMode)d.Player.Mode);
        }

        /// <summary>Villagers are rebuilt on load (CreateWorkerNpc names them); other own units are scene units matched by name.</summary>
        private static bool IsVillager(RtsUnit u) => u.name.StartsWith("NPC_Villager");

        private static GameObject FindUnit(string name)
        {
            foreach (RtsUnit u in UnityEngine.Object.FindObjectsByType<RtsUnit>(FindObjectsSortMode.None))
                if (u != null && u.name == name && u.gameObject.activeInHierarchy) return u.gameObject;
            return null;
        }

        private static void Place(GameObject go, Vector3 pos, float yaw)
        {
            CharacterController cc = go.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            if (cc != null) cc.enabled = true;
            RtsUnit unit = go.GetComponent<RtsUnit>();
            if (unit != null && go.GetComponent<FirstPersonController>() == null) unit.IssueMoveCommand(pos);
        }

        private static void ApplyGear(GameObject go, Item[] gear)
        {
            if (gear == null) return;
            Equipment e = go.GetComponent<Equipment>();
            if (e == null) e = go.AddComponent<Equipment>();
            e.LoadSlots(gear);
        }
    }

    /// <summary>Keeps the save system ticking: applies a pending load, autosaves, F5 quick save / F9 quick load.</summary>
    public sealed class SaveRunner : MonoBehaviour
    {
        private float autosaveTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("SaveRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<SaveRunner>();
        }

        private void Awake() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            autosaveTimer = SaveSystem.AutosaveSeconds;
            if (scene.name == SaveSystem.GameScene) SaveSystem.OnGameSceneLoaded(this);
            else WorldPersistence.Suspended = false;
        }

        private void Update()
        {
            if (!SaveSystem.InGame) return;
            if (Time.timeScale > 0f && SaveSystem.AutosaveEnabled)
            {
                autosaveTimer -= Time.unscaledDeltaTime;
                if (autosaveTimer <= 0f) { autosaveTimer = SaveSystem.AutosaveSeconds; SaveSystem.Save(SaveSystem.AutoSlot); }
            }
            if (UniversalInput.IsKeyDown(KeyCode.F5)) SaveSystem.Save(SaveSystem.AutoSlot);
            if (UniversalInput.IsKeyDown(KeyCode.F9) && File.Exists(SaveSystem.PathOf(SaveSystem.AutoSlot))) SaveSystem.Load(SaveSystem.AutoSlot);
        }
    }
}
