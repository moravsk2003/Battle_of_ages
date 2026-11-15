using System.Text;
using BattleOfAges.Building;
using BattleOfAges.InputSystem;
using BattleOfAges.Resources;
using BattleOfAges.RTS;

namespace BattleOfAges.UI
{
    /// <summary>Tooltip texts of the HUD and the command panels (live: read when the tooltip shows and while it stays).</summary>
    public static class HudTips
    {
        public static string Key(KeyAction a) => KeybindsManager.GetKeyDisplayString(KeybindsManager.GetKey(a));

        public static string Wood()
        {
            ResourceManager res = ResourceManager.Instance;
            int wood = res != null ? res.WoodCount : 0;
            return TipText.Title(Loc.Pick($"Деревина: {wood}", $"Wood: {wood}"), UiTheme.Gold) +
                   Loc.Pick("\nНа будівлі, крафт і покращення.", "\nFor buildings, crafting, and upgrades.") +
                   $"\n{TipText.Dim(Loc.Pick($"Сам: гілки {TipText.Key(Key(KeyAction.InteractGather))}, рубка {TipText.Key(Key(KeyAction.ChopTree))}. Робітники: накази «Гілки» й «Рубати».", $"By hand: sticks {TipText.Key(Key(KeyAction.InteractGather))}, chop {TipText.Key(Key(KeyAction.ChopTree))}. Workers: \"Sticks\" & \"Chop\" orders."))}";
        }

        public static string Food()
        {
            ResourceManager res = ResourceManager.Instance;
            int pop = TownHall.Viewed != null ? TownHall.Viewed.People : TownHall.Population;
            float perDay = pop * Combat.GameBalance.FoodPerPersonPerDay;
            float days = res != null && perDay > 0f ? res.Food / perDay : 0f;
            int food = res != null ? res.FoodCount : 0;
            return TipText.Title(Loc.Pick($"Їжа: {food}", $"Food: {food}"), res != null && res.Hungry ? UiTheme.Bad : UiTheme.Gold) +
                   Loc.Pick($"\nЛюдина їсть {Combat.GameBalance.FoodPerPersonPerDay:0.#} на ігровий день: зараз {perDay:0.#} на день, запасу на {days:0.#} дн.", $"\nEach person eats {Combat.GameBalance.FoodPerPersonPerDay:0.#} per game day: currently {perDay:0.#}/day, stock for {days:0.#} d.") +
                   (res != null && res.Hungry ? "\n" + TipText.Bad(Loc.Pick($"Без їжі приріст стоїть, а кожні {Combat.GameBalance.StarveEveryHours:0} год гри гине людина.", $"Without food, growth stops and 1 person dies every {Combat.GameBalance.StarveEveryHours:0} game hours.")) : "") +
                   "\n" + TipText.Dim(Loc.Pick("Їжу дають поля.", "Fields produce food."));
        }

        public static string Ore()
        {
            ResourceManager res = ResourceManager.Instance;
            int ore = res != null ? res.OreCount : 0;
            var sb = new StringBuilder(TipText.Title(Loc.Pick($"Руда: {ore}", $"Ore: {ore}"), UiTheme.Gold));
            if (res != null)
                for (int t = res.HighestOreTier; t >= 0; t--)
                {
                    int n = res.GetOreTierCount(t);
                    if (n > 0) sb.Append($"\n{OreTierHelper.GetTierName(t)}  <b>{n}</b>");
                }
            sb.Append("\n").Append(TipText.Dim(Loc.Pick("Будівлі й речі беруть руду свого рівня або вищу; стіна з вищої — міцніша. Переплавка на верстаці: 10 → 1 рівнем вище.", "Buildings and gear accept their tier of ore or higher; higher-tier walls are stronger. Smelt at workbench: 10 → 1 of next tier.")));
            sb.Append("\n").Append(TipText.Dim(Loc.Pick("Камінці руди на землі, шахта (до 10 робітників) і носії до скрині.", "Ore stones on the ground, mines (up to 10 workers), and carriers to the chest.")));
            return sb.ToString();
        }

        public static string Workers()
        {
            int pop = TownHall.Population, cap = TownHall.PopulationCap;
            string hall = TownHall.Current != null
                ? Loc.Pick($"Ратуша {UiThemeRoman(TownHall.Current.Level)} дає стелю {cap}.", $"Town Hall {UiThemeRoman(TownHall.Current.Level)} gives a cap of {cap}.")
                : Loc.Pick($"Без ратуші стеля {cap} — збудуйте Ратушу.", $"Without a Town Hall the cap is {cap} — build a Town Hall.");
            int idle = IdleWorkers.Count;
            return TipText.Title(Loc.Pick($"Населення: {pop} / {cap}", $"Population: {pop} / {cap}"), pop >= cap ? UiTheme.Bad : UiTheme.Gold) +
                   $"\n{hall}" +
                   Loc.Pick($"\nПриріст сам по собі ~{TownHall.GrowthPerHour(pop):0.#} за ігрову годину, поки є місце.", $"\nPassive growth is ~{TownHall.GrowthPerHour(pop):0.#} per game hour while space remains.") +
                   Loc.Pick($"\nПрацюють: {WorkerRegistry.WorkingCount}, у шахтах: {WorkerRegistry.MinerCount}", $"\nWorking: {WorkerRegistry.WorkingCount}, in mines: {WorkerRegistry.MinerCount}") +
                   (idle > 0 ? "\n" + TipText.Bad(Loc.Pick($"Без діла: {idle}", $"Idle: {idle}")) + TipText.Dim(Loc.Pick("  — кнопка над мінімапою або ", "  — button above minimap or ")) + TipText.Key(Key(KeyAction.IdleWorker)) : "");
        }

        public static string Army() =>
            TipText.Title(Loc.Pick("Військо", "Army"), UiTheme.Gold) +
            Loc.Pick("\nСвої бійці, що не працюють: воїни, лицарі, генерали.", "\nYour idle fighters: warriors, knights, generals.") +
            $"\n{TipText.Dim(Loc.Pick($"Воїном стають у боях і зрідка від праці, лицарем і генералом — лише в боях. Тривога {TipText.Key(Key(KeyAction.Alarm))} кличе всіх до зброї.", $"Workers become warriors in battle and rarely through labor; knights and generals only in battle. Alarm {TipText.Key(Key(KeyAction.Alarm))} calls everyone to arms."))}";

        public static string Alarm() =>
            TipText.Title(SettlementAlarm.Active ? Loc.Pick("Тривога триває", "Alarm Active") : Loc.Pick("Тривога", "Alarm"), SettlementAlarm.Active ? UiTheme.Bad : UiTheme.Gold) +
            Loc.Pick("\nУсі робітники кидають роботу й беруть зброю; лучники йдуть на стіни.", "\nAll workers drop work and take up arms; archers man the walls.") +
            $"\n{TipText.Dim(Loc.Pick("Ще раз — відбій, усі повертаються до роботи.", "Press again to stand down; everyone returns to work."))}  {TipText.Key(Key(KeyAction.Alarm))}";

        /// <summary>"День 2 · 14:00" / "Day 2 · 14:00" from the world clock.</summary>
        public static string ClockShort()
        {
            int day = Combat.WorldClock.Day, hour = (int)Combat.WorldClock.HourOfDay;
            return Loc.Pick($"День {day} · {hour:00}:00", $"Day {day} · {hour:00}:00");
        }

        public static string Clock() =>
            TipText.Title(Loc.Pick("Ігровий час", "Game Time"), UiTheme.Gold) + "\n" + ClockShort() +
            Loc.Pick($"\nГодина гри — {Combat.GameBalance.GameHourSeconds:0} с. Від годин залежать приріст населення й реген.", $"\n1 game hour = {Combat.GameBalance.GameHourSeconds:0}s. Population growth and regen tick each hour.") +
            "\n" + TipText.Dim(Loc.Pick("Пауза зупиняє час.", "Pause stops time."));

        private static string UiThemeRoman(int level) => Crafting.ItemTypeNames.Roman(level);
    }

    /// <summary>Own workers doing nothing (NpcResourceGatherer.IsIdle), for the HUD counter and the next-idle button.</summary>
    public static class IdleWorkers
    {
        private static int cursor;

        public static int Count
        {
            get
            {
                int n = 0;
                foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers) if (w != null && w.IsIdle) n++;
                return n;
            }
        }

        /// <summary>The next idle worker after the last one returned (cycles), or null.</summary>
        public static NpcResourceGatherer Next()
        {
            var list = WorkerRegistry.ActiveWorkers;
            for (int k = 0; k < list.Count; k++)
            {
                cursor = (cursor + 1) % list.Count;
                NpcResourceGatherer w = list[cursor];
                if (w != null && w.IsIdle) return w;
            }
            return null;
        }
    }
}
