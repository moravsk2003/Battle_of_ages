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

        /// <summary>True while one scene is replaced by another: the old world must not overwrite the loaded state.</summary>
        public static bool Suspended { get; set; }

        public static void Clear()
        {
            Trees.Clear();
            Camps.Clear();
            KilledAnimals.Clear();
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
            if (key != null && Trees.TryGetValue(key, out TreeSave s)) tree.ApplySavedState((TreeState)s.State, s.Wood);
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
        }

        private static CampSave CampOf(string camp)
        {
            if (!Camps.TryGetValue(camp, out CampSave c)) Camps[camp] = c = new CampSave { Camp = camp };
            return c;
        }

        public static bool IsMobKilled(string camp, string mob) => Camps.TryGetValue(camp, out CampSave c) && c.Killed.Contains(mob);

        // ------------------------------------------------------------------ wildlife

        /// <summary>A chunk spawned an animal: a killed one does not come back.</summary>
        public static bool RegisterAnimal(GameObject animal)
        {
            if (animal == null) return false;
            string key = animal.name;
            if (KilledAnimals.Contains(key)) { animal.SetActive(false); Object.Destroy(animal); return false; }
            HealthComponent hp = animal.GetComponent<HealthComponent>();
            if (hp != null) hp.OnDeath += () => { if (!Suspended) KilledAnimals.Add(key); };
            return true;
        }
    }
}
