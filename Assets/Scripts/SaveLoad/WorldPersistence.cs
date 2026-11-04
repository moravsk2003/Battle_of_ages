using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.Resources;

namespace BattleOfAges.SaveLoad
{
    /// <summary>
    /// Remembers what happened to the generated world: felled / used-up trees, raiders killed or wounded in each camp
    /// and wildlife killed. The world streams in chunks rebuilt from their seed; every chunk asks here when it builds
    /// its trees, camp and animals (Register*), and reports their state when it is unloaded (Record*). So a felled
    /// tree stays felled after walking away and back, and the whole set goes into a save.
    /// </summary>
    public static class WorldPersistence
    {
        private static readonly Dictionary<string, TreeSave> Trees = new Dictionary<string, TreeSave>();
        private static readonly Dictionary<string, CampSave> Camps = new Dictionary<string, CampSave>();
        private static readonly HashSet<string> KilledAnimals = new HashSet<string>();

        // ---- In a game with others (Net.NetSession): what changes here is told, what is told is applied
        /// <summary>A camp's state or a killed animal to tell the others; null when alone.</summary>
        public static System.Action<CampSave> CampChanged;
        public static System.Action<string> AnimalKilled;
        /// <summary>Trees are told only while someone listens; those changed since the last look.</summary>
        public static bool ListeningToTrees;
        private static readonly HashSet<ResourceNodeTree> dirtyTrees = new HashSet<ResourceNodeTree>();
        private static readonly Dictionary<string, ResourceNodeTree> liveTrees = new Dictionary<string, ResourceNodeTree>();
        private static bool applying;

        /// <summary>A tree has changed by someone's hand here.</summary>
        public static void NoteTree(ResourceNodeTree tree)
        {
            if (ListeningToTrees && !applying && tree != null && !string.IsNullOrEmpty(tree.PersistentKey)) dirtyTrees.Add(tree);
        }

        /// <summary>The trees changed since the last call, as they are now.</summary>
        public static List<TreeSave> TakeChangedTrees()
        {
            var changed = new List<TreeSave>();
            foreach (ResourceNodeTree t in dirtyTrees)
            {
                if (t == null || string.IsNullOrEmpty(t.PersistentKey)) continue;
                RecordTree(t);
                changed.Add(new TreeSave { Key = t.PersistentKey, State = (int)t.CurrentState, Wood = t.WoodRemaining });
            }
            dirtyTrees.Clear();
            return changed;
        }

        /// <summary>What another player has done to a tree: remembered, and shown if it stands here now.</summary>
        public static void ApplyTree(TreeSave s)
        {
            if (string.IsNullOrEmpty(s.Key)) return;
            applying = true;
            bool whole = s.State == (int)TreeState.WholeTree;
            if (whole) Trees.Remove(s.Key);
            else Trees[s.Key] = s;
            if (liveTrees.TryGetValue(s.Key, out ResourceNodeTree tree) && tree != null && tree.PersistentKey == s.Key)
            {
                if (whole) { if (tree.CurrentState != TreeState.WholeTree) tree.ResetTreeState(); }
                else if (tree.CurrentState != (TreeState)s.State) tree.ApplySavedState((TreeState)s.State, s.Wood);
                else tree.SetWood(s.Wood);
            }
            applying = false;
        }

        /// <summary>What is known of a tree (a tree nobody has touched: false).</summary>
        public static bool TryGetTree(string key, out TreeSave s) => Trees.TryGetValue(key, out s);

        /// <summary>A camp as its owner left it: whoever brings it to life next starts from this.</summary>
        public static void ApplyCamp(CampSave c) { if (c != null && !string.IsNullOrEmpty(c.Camp)) Camps[c.Camp] = c; }

        public static void ApplyKilledAnimal(string key) { if (!string.IsNullOrEmpty(key)) KilledAnimals.Add(key); }

        /// <summary>True while one scene is replaced by another: the old world must not overwrite the loaded state.</summary>
        public static bool Suspended { get; set; }

        public static void Clear()
        {
            Trees.Clear();
            Camps.Clear();
            KilledAnimals.Clear();
            dirtyTrees.Clear();
            liveTrees.Clear();
        }

        public static void Load(WorldSave w)
        {
            Clear();
            if (w == null) return;
            foreach (TreeSave t in w.Trees) if (!string.IsNullOrEmpty(t.Key)) Trees[t.Key] = t;
            foreach (CampSave c in w.Camps) if (!string.IsNullOrEmpty(c.Camp)) Camps[c.Camp] = c;
            foreach (string a in w.KilledAnimals) KilledAnimals.Add(a);
        }

        /// <summary>Everything, including what is loaded right now.</summary>
        public static WorldSave Capture()
        {
            foreach (ResourceNodeTree t in Object.FindObjectsByType<ResourceNodeTree>(FindObjectsSortMode.None)) RecordTree(t);
            foreach (HostileMobAI mob in Object.FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None))
                if (mob.transform.parent != null && mob.transform.parent.name.StartsWith("HostileCamp_")) RecordMob(mob.transform.parent.name, mob);
            var w = new WorldSave();
            w.Trees.AddRange(Trees.Values);
            w.Camps.AddRange(Camps.Values);
            w.KilledAnimals.AddRange(KilledAnimals);
            return w;
        }

        // ------------------------------------------------------------------ trees

        public static void RegisterTree(ResourceNodeTree tree, string key)
        {
            if (tree == null) return;
            tree.PersistentKey = key;
            if (key != null) liveTrees[key] = tree;
            if (key != null && Trees.TryGetValue(key, out TreeSave s)) tree.ApplySavedState((TreeState)s.State, s.Wood);
            tree.StandSolid();
        }

        public static void RecordTree(ResourceNodeTree tree)
        {
            if (Suspended) return;
            if (tree == null || string.IsNullOrEmpty(tree.PersistentKey)) return;
            if (tree.IsUntouched) { Trees.Remove(tree.PersistentKey); return; }
            Trees[tree.PersistentKey] = new TreeSave { Key = tree.PersistentKey, State = (int)tree.CurrentState, Wood = tree.WoodRemaining };
        }

        /// <summary>Scene-placed trees (the base area) are keyed by their place in the hierarchy.</summary>
        public static void RegisterSceneTrees()
        {
            foreach (ResourceNodeTree t in Object.FindObjectsByType<ResourceNodeTree>(FindObjectsSortMode.None))
                if (string.IsNullOrEmpty(t.PersistentKey)) RegisterTree(t, "scene:" + PathOf(t.transform));
        }

        private static string PathOf(Transform t)
        {
            string p = t.name + "@" + t.GetSiblingIndex();
            for (Transform a = t.parent; a != null; a = a.parent) p = a.name + "/" + p;
            return p;
        }

        // ------------------------------------------------------------------ camps

        /// <summary>A camp was built: its fallen raiders stay dead, wounded ones keep their health and place.</summary>
        public static void RegisterCamp(GameObject camp)
        {
            if (camp == null) return;
            string key = camp.name;
            Camps.TryGetValue(key, out CampSave saved);
            foreach (HostileMobAI mob in camp.GetComponentsInChildren<HostileMobAI>(true))
            {
                string name = mob.name;
                if (saved != null && saved.Killed.Contains(name)) { mob.gameObject.SetActive(false); Object.Destroy(mob.gameObject); continue; }
                if (saved != null)
                    foreach (MobSave m in saved.Alive)
                        if (m.Name == name)
                        {
                            mob.transform.position = m.Position;
                            if (mob.Health != null && m.Health > 0) mob.Health.SetCurrentHealth(m.Health);
                        }
                HealthComponent hp = mob.Health;
                if (hp != null) hp.OnDeath += () => MarkKilled(key, name);
            }
        }

        /// <summary>The camp is being unloaded: remember its wounded raiders.</summary>
        public static void RecordCamp(GameObject camp)
        {
            if (Suspended) return;
            if (camp == null) return;
            foreach (HostileMobAI mob in camp.GetComponentsInChildren<HostileMobAI>(true)) RecordMob(camp.name, mob);
            if (CampChanged != null && Camps.TryGetValue(camp.name, out CampSave state)) CampChanged(state);
        }

        private static void RecordMob(string camp, HostileMobAI mob)
        {
            HealthComponent hp = mob.Health;
            if (hp == null || !hp.IsAlive) return;
            bool hurt = hp.CurrentHealth < hp.MaxHealth;
            if (!hurt && !Camps.ContainsKey(camp)) return; // untouched camps are rebuilt as they were
            CampSave c = CampOf(camp);
            c.Alive.RemoveAll(m => m.Name == mob.name);
            c.Alive.Add(new MobSave { Name = mob.name, Position = mob.transform.position, Health = hp.CurrentHealth });
        }

        private static void MarkKilled(string camp, string mob)
        {
            if (Suspended) return;
            CampSave c = CampOf(camp);
            if (!c.Killed.Contains(mob)) c.Killed.Add(mob);
            c.Alive.RemoveAll(m => m.Name == mob);
            CampChanged?.Invoke(c);
        }

        private static CampSave CampOf(string camp)
        {
            if (!Camps.TryGetValue(camp, out CampSave c)) Camps[camp] = c = new CampSave { Camp = camp };
            return c;
        }

        // ------------------------------------------------------------------ wildlife

        /// <summary>A chunk spawned an animal: a killed one does not come back.</summary>
        public static bool RegisterAnimal(GameObject animal)
        {
            if (animal == null) return false;
            string key = animal.name;
            if (KilledAnimals.Contains(key)) { animal.SetActive(false); Object.Destroy(animal); return false; }
            HealthComponent hp = animal.GetComponent<HealthComponent>();
            if (hp != null) hp.OnDeath += () => { if (Suspended) return; KilledAnimals.Add(key); AnimalKilled?.Invoke(key); };
            return true;
        }
    }
}
