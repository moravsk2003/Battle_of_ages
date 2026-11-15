using System;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Crafting;
using BattleOfAges.RTS;

namespace BattleOfAges.Building
{
    /// <summary>What a person can be set to: a district's buildings, or a job with no building.</summary>
    public enum LaborJob { Fields, Forest, Mining, Crafts, Building, Branches, Chop, Pebbles, Haul, Standing }

    /// <summary>One line of the people's window.</summary>
    public struct LaborRow
    {
        public LaborJob Job;
        public int Hands;
        /// <summary>Places there are (-1: as many as you like).</summary>
        public int Places;
        /// <summary>Buildings of the district (0 for a job with none).</summary>
        public int Buildings;
        /// <summary>People who come by themselves stop at this many (-1: no limit).</summary>
        public int Limit;
    }

    /// <summary>A district's limit in a save.</summary>
    [Serializable]
    public struct LaborLimitSave
    {
        public int HallX, HallZ, Job, Max;
    }

    /// <summary>
    /// Who of a settlement does what, by district (docs/GAME_DESIGN.md §10.1): how many at its fields, sawmills, mines, benches and sites,
    /// and on the jobs with no building. One more takes the nearest free person, then one on a job with no building. One less sends the
    /// person back to working on their own, and from then on the district takes no more than that by itself (the limit), or the fields
    /// would call back whoever had just been sent away.
    /// </summary>
    public static class SettlementLabor
    {
        public static readonly LaborJob[] Districts = { LaborJob.Fields, LaborJob.Forest, LaborJob.Mining, LaborJob.Crafts, LaborJob.Building };
        public static readonly LaborJob[] Jobs = { LaborJob.Branches, LaborJob.Chop, LaborJob.Pebbles, LaborJob.Haul, LaborJob.Standing };

        private static readonly Dictionary<(int, int, LaborJob), int> limits = new Dictionary<(int, int, LaborJob), int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => limits.Clear();

        public static void Clear() => limits.Clear();

        public static string NameOf(LaborJob job)
        {
            switch (job)
            {
                case LaborJob.Fields: return UI.Loc.Pick("Поля", "Fields");
                case LaborJob.Forest: return UI.Loc.Pick("Лісопилки", "Sawmills");
                case LaborJob.Mining: return UI.Loc.Pick("Шахти", "Mines");
                case LaborJob.Crafts: return UI.Loc.Pick("Верстаки", "Workbenches");
                case LaborJob.Building: return UI.Loc.Pick("Будівництво", "Construction");
                case LaborJob.Branches: return UI.Loc.Pick("Збирати гілки", "Gather Branches");
                case LaborJob.Chop: return UI.Loc.Pick("Рубати дерева", "Chop Trees");
                case LaborJob.Pebbles: return UI.Loc.Pick("Збирати руду з землі", "Gather Surface Ore");
                case LaborJob.Haul: return UI.Loc.Pick("Носити руду з шахт", "Haul Ore from Mines");
                default: return UI.Loc.Pick("Стояти, без роботи", "Idle / Standing");
            }
        }

        public static string TipOf(LaborJob job)
        {
            if (UI.Loc.En)
            {
                switch (job)
                {
                    case LaborJob.Fields: return "Produce food while supplies are low. Free workers come here automatically.";
                    case LaborJob.Forest: return "Sawmills produce wood without clearing the forest. Free workers join when wood is low.";
                    case LaborJob.Mining: return "Mine ore underground. Without haulers, ore stays inside the mine.";
                    case LaborJob.Crafts: return "Work at a bench, crafting queued gear and ingots.";
                    case LaborJob.Building: return "Construct placed sites. More builders finish faster.";
                    case LaborJob.Branches: return "Shake trees without cutting them down.";
                    case LaborJob.Chop: return "Fell trees for logs: yields more wood, thins the forest.";
                    case LaborJob.Pebbles: return "Collect ore pebbles on the ground and carry them to a chest.";
                    case LaborJob.Haul: return "Haul mined ore from mines to a storage chest.";
                    default: return "Stand ready for combat until given a task.";
                }
            }
            switch (job)
            {
                case LaborJob.Fields: return "Дають їжу, поки її бракує. Вільні люди приходять самі.";
                case LaborJob.Forest: return "Лісопилки дають дерево, не чіпаючи лісу. Вільні люди приходять самі, поки дерева бракує.";
                case LaborJob.Mining: return "Копають руду всередині шахти. Без носіїв вона лишається в шахті.";
                case LaborJob.Crafts: return "Працюють за верстаком: кують те, що замовлено.";
                case LaborJob.Building: return "Зводять ділянки. Що більше рук, то швидше.";
                case LaborJob.Branches: return "Обтрушують дерева, не рубаючи їх.";
                case LaborJob.Chop: return "Валять дерева на дрова: більше деревини, ліс рідшає.";
                case LaborJob.Pebbles: return "Підбирають камінці руди й несуть у скриню.";
                case LaborJob.Haul: return "Носять видобуту руду з шахти в скриню.";
                default: return "Стоять, готові до бою, поки їх не пошлють на роботу.";
            }
        }

        private static GatherDirective DirectiveOf(LaborJob job)
        {
            switch (job)
            {
                case LaborJob.Branches: return GatherDirective.WoodGatherBranchesOnly;
                case LaborJob.Chop: return GatherDirective.WoodChopTreesOnly;
                case LaborJob.Pebbles: return GatherDirective.OrePebblesOnly;
                case LaborJob.Haul: return GatherDirective.HaulOreFromMineToChest;
                default: return GatherDirective.Idle;
            }
        }

        private static bool HasLimit(LaborJob job) => job == LaborJob.Fields || job == LaborJob.Forest || job == LaborJob.Mining;

        // ------------------------------------------------------------------ who belongs where

        private static bool Belongs(Vector3 at, TownHall hall) => hall == null || TownHall.All.Count <= 1 || TownHall.Nearest(at) == hall;

        private static bool IsMine(LaborJob job, WorkPlace p) => job == LaborJob.Fields ? p is FieldNode : p is SawmillNode;

        private static (int, int, LaborJob) Key(TownHall hall, LaborJob job)
        {
            Vector3 at = hall != null ? hall.transform.position : Vector3.zero;
            return (Mathf.RoundToInt(at.x), Mathf.RoundToInt(at.z), job);
        }

        /// <summary>The most this district takes by itself in this settlement, -1: as many as there is room for.</summary>
        public static int LimitOf(LaborJob job, TownHall hall) => limits.TryGetValue(Key(hall, job), out int max) ? max : -1;

        public static void ClearLimits(TownHall hall)
        {
            foreach (LaborJob job in Districts) limits.Remove(Key(hall, job));
        }

        /// <summary>Is there room here for someone who comes by themselves?</summary>
        public static bool RoomFor(WorkPlace place)
        {
            if (place == null || limits.Count == 0) return true;
            LaborJob job = place is FieldNode ? LaborJob.Fields : LaborJob.Forest;
            TownHall hall = TownHall.Nearest(place.transform.position);
            int max = LimitOf(job, hall);
            return max < 0 || RowOf(job, hall).Hands < max;
        }

        public static bool RoomFor(MineNode mine)
        {
            if (mine == null || limits.Count == 0) return true;
            TownHall hall = TownHall.Nearest(mine.transform.position);
            int max = LimitOf(LaborJob.Mining, hall);
            return max < 0 || RowOf(LaborJob.Mining, hall).Hands < max;
        }

        // ------------------------------------------------------------------ how it stands

        public static LaborRow RowOf(LaborJob job, TownHall hall)
        {
            var row = new LaborRow { Job = job, Limit = LimitOf(job, hall), Places = -1 };
            switch (job)
            {
                case LaborJob.Fields:
                case LaborJob.Forest:
                    row.Places = 0;
                    foreach (WorkPlace p in WorkPlace.All)
                    {
                        if (p == null || !IsMine(job, p) || BuildGhost.Is(p) || !Belongs(p.transform.position, hall)) continue;
                        row.Buildings++;
                        row.Hands += p.WorkerCount;
                        row.Places += p.Slots;
                    }
                    break;
                case LaborJob.Mining:
                    row.Places = 0;
                    foreach (MineNode m in MineNode.ActiveMines)
                    {
                        if (m == null || BuildGhost.Is(m) || !Belongs(m.transform.position, hall)) continue;
                        row.Buildings++;
                        row.Hands += m.AssignedWorkerCount;
                        row.Places += MineNode.MAX_WORKERS;
                    }
                    break;
                case LaborJob.Crafts:
                    row.Places = 0;
                    foreach (WorkbenchNode b in WorkbenchNode.ActiveWorkbenches)
                    {
                        if (b == null || BuildGhost.Is(b) || !Belongs(b.transform.position, hall)) continue;
                        row.Buildings++;
                        row.Hands += b.CalledWorkerCount;
                        row.Places += CraftingQueue.MAX_WORKERS;
                    }
                    break;
                case LaborJob.Building:
                    row.Places = 0;
                    foreach (ConstructionSite s in ConstructionSite.All)
                    {
                        if (s == null || !Belongs(s.transform.position, hall)) continue;
                        row.Buildings++;
                        row.Hands += s.BuilderCount;
                        row.Places += ConstructionSite.MaxBuilders;
                    }
                    break;
                default:
                    GatherDirective d = DirectiveOf(job);
                    foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers)
                        if (Counts(w, hall) && !w.IsStationed && !w.AtPlace && w.CurrentDirective == d) row.Hands++;
                    break;
            }
            return row;
        }

        private static bool Counts(NpcResourceGatherer w, TownHall hall)
        {
            if (w == null || !w.gameObject.activeInHierarchy || !Belongs(w.transform.position, hall)) return false;
            RtsUnit u = w.GetComponent<RtsUnit>();
            return u != null && u.IsAlive && !u.IsHero;
        }

        /// <summary>Those working on their own, not at any building: the ones to give out.</summary>
        public static int FreeCount(TownHall hall)
        {
            int n = 0;
            foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers)
                if (Counts(w, hall) && !w.IsStationed && !w.AtPlace && w.CurrentDirective == GatherDirective.Auto) n++;
            return n;
        }

        /// <summary>Everyone of this settlement.</summary>
        public static int PeopleCount(TownHall hall)
        {
            int n = 0;
            foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers) if (Counts(w, hall)) n++;
            return n;
        }

        // ------------------------------------------------------------------ one more, one less

        /// <summary>The person to give a new job to: a free one, one who came to a place by themselves, then one on a job with no building; the nearest of them.</summary>
        private static NpcResourceGatherer Take(TownHall hall, Vector3 near, GatherDirective notFrom, bool orStanding = false)
        {
            NpcResourceGatherer best = null;
            int bestKind = int.MaxValue;
            float bestSqr = float.MaxValue;
            foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers)
            {
                if (!Counts(w, hall) || w.IsStationed) continue;
                int kind;
                if (w.CurrentDirective == GatherDirective.Auto) kind = w.AtPlace ? 1 : 0;
                else if (w.CurrentDirective == GatherDirective.Idle) { if (!orStanding) continue; kind = 3; }
                else kind = 2;
                if (w.CurrentDirective == notFrom) continue;
                float d = (w.transform.position - near).sqrMagnitude;
                if (kind < bestKind || (kind == bestKind && d < bestSqr)) { best = w; bestKind = kind; bestSqr = d; }
            }
            return best;
        }

        private static string NoOneFree => UI.Loc.Pick("Вільних людей немає: приберіть когось з іншої роботи", "No free workers: remove someone from another job");

        /// <summary>One more on this job in this settlement; what went wrong in words, or null.</summary>
        public static string Add(LaborJob job, TownHall hall)
        {
            Vector3 near = hall != null ? hall.transform.position : Vector3.zero;
            switch (job)
            {
                case LaborJob.Fields:
                case LaborJob.Forest:
                    WorkPlace place = null;
                    foreach (WorkPlace p in WorkPlace.All)
                        if (p != null && IsMine(job, p) && !BuildGhost.Is(p) && Belongs(p.transform.position, hall) && p.WorkerCount < p.Slots && (place == null || p.WorkerCount < place.WorkerCount)) place = p;
                    if (place == null) return RowOf(job, hall).Buildings == 0 ? $"{NameOf(job)}: {UI.Loc.Pick("спершу збудуйте", "build one first")}" : $"{NameOf(job)}: {UI.Loc.Pick("усі місця зайняті", "all slots full")}";
                    return AddTo(place.gameObject, hall, job);
                case LaborJob.Mining:
                    MineNode mine = null;
                    foreach (MineNode m in MineNode.ActiveMines)
                        if (m != null && !BuildGhost.Is(m) && Belongs(m.transform.position, hall) && !m.IsFullWorkers && (mine == null || m.AssignedWorkerCount < mine.AssignedWorkerCount)) mine = m;
                    if (mine == null) return RowOf(job, hall).Buildings == 0 ? UI.Loc.Pick("Шахти: спершу збудуйте", "Mines: build one first") : UI.Loc.Pick("Шахти: усі місця зайняті", "Mines: all slots full");
                    return AddTo(mine.gameObject, hall, job);
                case LaborJob.Crafts:
                    WorkbenchNode bench = null;
                    foreach (WorkbenchNode b in WorkbenchNode.ActiveWorkbenches)
                        if (b != null && !BuildGhost.Is(b) && Belongs(b.transform.position, hall) && !b.IsWorkerFull && (bench == null || b.CalledWorkerCount < bench.CalledWorkerCount)) bench = b;
                    if (bench == null) return RowOf(job, hall).Buildings == 0 ? UI.Loc.Pick("Верстаки: спершу збудуйте", "Workbenches: build one first") : UI.Loc.Pick("Верстаки: усі місця зайняті", "Workbenches: all slots full");
                    return AddTo(bench.gameObject, hall, job);
                case LaborJob.Building:
                    ConstructionSite site = null;
                    foreach (ConstructionSite s in ConstructionSite.All)
                        if (s != null && Belongs(s.transform.position, hall) && s.NeedsHands && (site == null || s.BuilderCount < site.BuilderCount)) site = s;
                    if (site == null) return RowOf(job, hall).Buildings == 0 ? UI.Loc.Pick("Зараз нічого не будується", "Nothing is being built right now") : UI.Loc.Pick("На ділянках усі місця зайняті", "All construction slots are full");
                    return AddTo(site.gameObject, hall, job);
                default:
                    GatherDirective d = DirectiveOf(job);
                    NpcResourceGatherer w = Take(hall, near, d, orStanding: true);
                    if (w == null) return NoOneFree;
                    w.LeavePlace(null);
                    w.SetGatherDirective(d);
                    return null;
            }
        }

        /// <summary>One more at this very building.</summary>
        public static string AddTo(GameObject building, TownHall hall, LaborJob job)
        {
            NpcResourceGatherer w = Take(hall, building.transform.position, GatherDirective.Idle);
            if (w == null) return NoOneFree;
            bool done;
            ConstructionSite site = building.GetComponent<ConstructionSite>();
            if (site != null)
            {
                if (!site.NeedsHands) return UI.Loc.Pick("На ділянці всі місця зайняті", "All builder slots on this site are full");
                w.LeavePlace(null);
                done = w.AssignToSite(site);
            }
            else if (building.TryGetComponent(out WorkPlace place))
            {
                w.LeavePlace(null);
                done = w.AssignToPlace(place, true);
            }
            else if (building.TryGetComponent(out MineNode mine))
            {
                if (mine.IsFullWorkers) return UI.Loc.Pick("У шахті всі місця зайняті", "This mine is full");
                w.LeavePlace(null);
                w.AssignToWorkInMine(mine);
                done = true;
            }
            else if (building.TryGetComponent(out WorkbenchNode bench))
            {
                w.LeavePlace(null);
                done = bench.AssignWorker(w.GetComponent<RtsUnit>());
            }
            else return UI.Loc.Pick("Тут людям нема чого робити", "There is nothing to do here");
            if (!done) return UI.Loc.Pick("Тут усі місця зайняті", "All slots here are full");
            if (HasLimit(job) && limits.TryGetValue(Key(hall, job), out int max)) limits[Key(hall, job)] = max + 1;
            return null;
        }

        /// <summary>One less on this job in this settlement; the person goes back to working on their own.</summary>
        public static string Remove(LaborJob job, TownHall hall)
        {
            switch (job)
            {
                case LaborJob.Fields:
                case LaborJob.Forest:
                    WorkPlace place = null;
                    foreach (WorkPlace p in WorkPlace.All)
                        if (p != null && IsMine(job, p) && Belongs(p.transform.position, hall) && p.WorkerCount > 0 && (place == null || p.WorkerCount > place.WorkerCount)) place = p;
                    return place != null ? RemoveFrom(place.gameObject, hall, job) : null;
                case LaborJob.Mining:
                    MineNode mine = null;
                    foreach (MineNode m in MineNode.ActiveMines)
                        if (m != null && Belongs(m.transform.position, hall) && m.AssignedWorkerCount > 0 && (mine == null || m.AssignedWorkerCount > mine.AssignedWorkerCount)) mine = m;
                    return mine != null ? RemoveFrom(mine.gameObject, hall, job) : null;
                case LaborJob.Crafts:
                    WorkbenchNode bench = null;
                    foreach (WorkbenchNode b in WorkbenchNode.ActiveWorkbenches)
                        if (b != null && Belongs(b.transform.position, hall) && b.CalledWorkerCount > 0 && (bench == null || b.CalledWorkerCount > bench.CalledWorkerCount)) bench = b;
                    return bench != null ? RemoveFrom(bench.gameObject, hall, job) : null;
                case LaborJob.Building:
                    ConstructionSite site = null;
                    foreach (ConstructionSite s in ConstructionSite.All)
                        if (s != null && Belongs(s.transform.position, hall) && s.BuilderCount > 0 && (site == null || s.BuilderCount > site.BuilderCount)) site = s;
                    return site != null ? RemoveFrom(site.gameObject, hall, job) : null;
                default:
                    GatherDirective d = DirectiveOf(job);
                    foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers)
                    {
                        if (!Counts(w, hall) || w.IsStationed || w.AtPlace || w.CurrentDirective != d) continue;
                        w.SetGatherDirective(GatherDirective.Auto);
                        return null;
                    }
                    return null;
            }
        }

        /// <summary>One less at this very building; the district takes no more than what is left by itself.</summary>
        public static string RemoveFrom(GameObject building, TownHall hall, LaborJob job)
        {
            ConstructionSite site = building.GetComponent<ConstructionSite>();
            if (site != null)
            {
                if (site.BuilderCount == 0) return null;
                NpcResourceGatherer builder = site.Builders[site.BuilderCount - 1];
                if (builder != null) builder.LeaveSite(site);
                return null;
            }
            if (building.TryGetComponent(out WorkPlace place))
            {
                if (place.WorkerCount == 0) return null;
                NpcResourceGatherer w = place.Workers[place.WorkerCount - 1];
                if (w != null)
                {
                    w.LeavePlace(place);
                    if (w.CurrentDirective == GatherDirective.Idle) w.SetGatherDirective(GatherDirective.Auto);
                }
            }
            else if (building.TryGetComponent(out MineNode mine)) { if (!mine.ReleaseOneWorker()) return null; }
            else if (building.TryGetComponent(out WorkbenchNode bench)) { if (!bench.UnassignLastWorker()) return null; }
            else return null;
            if (HasLimit(job)) limits[Key(hall, job)] = RowOf(job, hall).Hands;
            return null;
        }

        /// <summary>Everyone of this settlement on a job with no building goes to work on their own (those at a building stay).</summary>
        public static void AllOnTheirOwn(TownHall hall)
        {
            foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers)
                if (Counts(w, hall) && !w.IsStationed && w.CurrentDirective != GatherDirective.Auto) w.SetGatherDirective(GatherDirective.Auto);
        }

        /// <summary>The district a building belongs to, if people work there.</summary>
        public static bool DistrictOf(GameObject building, out LaborJob job)
        {
            job = LaborJob.Building;
            if (building.GetComponent<ConstructionSite>() != null) return true;
            if (building.GetComponent<FieldNode>() != null) { job = LaborJob.Fields; return true; }
            if (building.GetComponent<SawmillNode>() != null) { job = LaborJob.Forest; return true; }
            if (building.GetComponent<MineNode>() != null) { job = LaborJob.Mining; return true; }
            if (building.GetComponent<WorkbenchNode>() != null) { job = LaborJob.Crafts; return true; }
            return false;
        }

        /// <summary>How many work at this building, of how many it takes (false: nobody works there).</summary>
        public static bool HandsAt(GameObject building, out int hands, out int places)
        {
            hands = places = 0;
            ConstructionSite site = building.GetComponent<ConstructionSite>();
            if (site != null) { hands = site.BuilderCount; places = ConstructionSite.MaxBuilders; return true; }
            if (building.TryGetComponent(out WorkPlace p) && p.enabled) { hands = p.WorkerCount; places = p.Slots; return true; }
            if (building.TryGetComponent(out MineNode m)) { hands = m.AssignedWorkerCount; places = MineNode.MAX_WORKERS; return true; }
            if (building.TryGetComponent(out WorkbenchNode b)) { hands = b.CalledWorkerCount; places = CraftingQueue.MAX_WORKERS; return true; }
            return false;
        }

        // ------------------------------------------------------------------ saves

        public static List<LaborLimitSave> Save()
        {
            var list = new List<LaborLimitSave>(limits.Count);
            foreach (KeyValuePair<(int, int, LaborJob), int> kv in limits)
                list.Add(new LaborLimitSave { HallX = kv.Key.Item1, HallZ = kv.Key.Item2, Job = (int)kv.Key.Item3, Max = kv.Value });
            return list;
        }

        public static void Load(List<LaborLimitSave> saved)
        {
            limits.Clear();
            if (saved == null) return;
            foreach (LaborLimitSave s in saved) limits[(s.HallX, s.HallZ, (LaborJob)s.Job)] = s.Max;
        }
    }

    /// <summary>Pulling a building down: part of its price comes back, all people there go back to work.</summary>
    public static class BuildingDemolition
    {
        /// <summary>Whether it can go, and if not, why.</summary>
        public static bool CanDemolish(BuildingNode node, out string why)
        {
            why = null;
            if (node == null) { why = UI.Loc.Pick("Її вже немає", "It is already gone"); return false; }
            if (node.GetComponent<ConstructionSite>() != null) return true;
            if (node.Type == BuildingType.TownHall) { why = UI.Loc.Pick("Ратушу не знести: це серце поселення", "Cannot demolish Town Hall: it is the heart of the settlement"); return false; }
            if (node.Type == BuildingType.StorageChest && Resources.StorageChestNode.ActiveChests.Count <= 1) { why = UI.Loc.Pick("Це остання скриня: людям нікуди носити", "Cannot demolish the last storage chest"); return false; }
            return true;
        }

        /// <summary>What comes back: of a site all that was not built yet, of a building a share of its price.</summary>
        public static void Refund(BuildingNode node, out int wood, out int ore, out int tier)
        {
            BuildingDefinition def = BuildingData.GetDefinition(node.Type);
            ConstructionSite site = node.GetComponent<ConstructionSite>();
            float share = site != null ? 1f - site.Progress : Combat.GameBalance.DemolishRefund;
            wood = Mathf.FloorToInt(def.WoodCost * share);
            ore = Mathf.FloorToInt(def.OreCost * share);
            WallSegment wall = node.GetComponent<WallSegment>();
            tier = wall != null ? wall.OreTier : def.OreTierRequired;
        }

        public static void Run(BuildingNode node)
        {
            if (!CanDemolish(node, out string why)) { UI.StrategyHud.Notify(why); return; }
            Refund(node, out int wood, out int ore, out int tier);
            GameObject go = node.gameObject;
            string name = BuildingData.GetDefinition(node.Type).DisplayName;

            ConstructionSite site = go.GetComponent<ConstructionSite>();
            if (site != null)
                while (site.BuilderCount > 0)
                {
                    NpcResourceGatherer b = site.Builders[site.BuilderCount - 1];
                    if (b != null) b.LeaveSite(site);
                    else break;
                }
            if (go.TryGetComponent(out MineNode mine)) while (mine.ReleaseOneWorker()) { }
            if (go.TryGetComponent(out WorkbenchNode bench)) while (bench.UnassignLastWorker()) { }

            if (!Combat.CentralEmpire.Active)
            {
                TownHall hall = TownHall.Nearest(go.transform.position);
                Resources.ResourceManager rm = Resources.ResourceManager.Instance;
                Resources.SettlementStock stock = hall != null ? hall.Stock : rm != null ? rm.Main : null;
                if (stock != null)
                {
                    if (wood > 0) stock.AddResource(Resources.ResourceType.Wood, wood);
                    if (ore > 0) stock.AddOre(tier, ore);
                }
            }
            bool zoned = World.WorldSettings.Bounded && World.ZoneMap.At(go.transform.position) != World.ZoneType.None;
            Destroy(go);
            UI.GameSound.At(UI.GameSound.Kind.Place, go.transform.position);
            string back = wood > 0 || ore > 0 ? UI.Loc.Pick($": повернуто {wood} дерева й {ore} руди", $": refunded {wood} wood and {ore} ore") : "";
            UI.StrategyHud.Notify(zoned ? UI.Loc.Pick($"{name} знесено{back}. Зітріть розмітку, інакше тут зведуть нову", $"{name} demolished{back}. Erase the zone or workers will rebuild here")
                                        : UI.Loc.Pick($"{name} знесено{back}", $"{name} demolished{back}"));
        }

        private static void Destroy(GameObject go)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(go);
            else UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
