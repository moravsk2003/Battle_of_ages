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
            return TipText.Title($"Деревина: {(res != null ? res.WoodCount : 0)}", UiTheme.Gold) +
                   "\nНа будівлі, крафт і покращення." +
                   $"\n{TipText.Dim($"Сам: гілки {TipText.Key(Key(KeyAction.InteractGather))}, рубка {TipText.Key(Key(KeyAction.ChopTree))}. Робітники: накази «Гілки» й «Рубати».")}";
        }

        public static string Food()
        {
            ResourceManager res = ResourceManager.Instance;
            int pop = TownHall.Viewed != null ? TownHall.Viewed.People : TownHall.Population;
            float perDay = pop * Combat.GameBalance.FoodPerPersonPerDay;
            float days = res != null && perDay > 0f ? res.Food / perDay : 0f;
            return TipText.Title($"Їжа: {(res != null ? res.FoodCount : 0)}", res != null && res.Hungry ? UiTheme.Bad : UiTheme.Gold) +
                   $"\nЛюдина їсть {Combat.GameBalance.FoodPerPersonPerDay:0.#} на ігровий день: зараз {perDay:0.#} на день, запасу на {days:0.#} дн." +
                   (res != null && res.Hungry ? "\n" + TipText.Bad($"Без їжі приріст стоїть, а кожні {Combat.GameBalance.StarveEveryHours:0} год гри гине людина.") : "") +
                   "\n" + TipText.Dim("Їжу дають поля.");
        }

        public static string Ore()
        {
            ResourceManager res = ResourceManager.Instance;
            var sb = new StringBuilder(TipText.Title($"Руда: {(res != null ? res.OreCount : 0)}", UiTheme.Gold));
            if (res != null)
                for (int t = res.HighestOreTier; t >= 0; t--)
                {
                    int n = res.GetOreTierCount(t);
                    if (n > 0) sb.Append($"\n{OreTierHelper.GetTierName(t)}  <b>{n}</b>");
                }
            sb.Append("\n").Append(TipText.Dim("Будівлі й речі беруть руду свого рівня або вищу; стіна з вищої — міцніша. Переплавка на верстаці: 10 → 1 рівнем вище."));
            sb.Append("\n").Append(TipText.Dim("Камінці руди на землі, шахта (до 10 робітників) і носії до скрині."));
            return sb.ToString();
        }

        public static string Workers()
        {
            int pop = TownHall.Population, cap = TownHall.PopulationCap;
            string hall = TownHall.Current != null ? $"Ратуша {UiThemeRoman(TownHall.Current.Level)} дає стелю {cap}." : $"Без ратуші стеля {cap} — збудуйте Ратушу.";
            int idle = IdleWorkers.Count;
            return TipText.Title($"Населення: {pop} / {cap}", pop >= cap ? UiTheme.Bad : UiTheme.Gold) +
                   $"\n{hall}" +
                   $"\nПриріст сам по собі ~{TownHall.GrowthPerHour(pop):0.#} за ігрову годину, поки є місце." +
                   $"\nПрацюють: {WorkerRegistry.WorkingCount}, у шахтах: {WorkerRegistry.MinerCount}" +
                   (idle > 0 ? "\n" + TipText.Bad($"Без діла: {idle}") + TipText.Dim("  — кнопка над мінімапою або ") + TipText.Key(Key(KeyAction.IdleWorker)) : "");
        }

        public static string Army() =>
            TipText.Title("Військо", UiTheme.Gold) +
            "\nСвої бійці, що не працюють: воїни, лицарі, генерали." +
            $"\n{TipText.Dim($"Воїном стають у боях і зрідка від праці, лицарем і генералом — лише в боях. Тривога {TipText.Key(Key(KeyAction.Alarm))} кличе всіх до зброї.")}";

        public static string Alarm() =>
            TipText.Title(SettlementAlarm.Active ? "Тривога триває" : "Тривога", SettlementAlarm.Active ? UiTheme.Bad : UiTheme.Gold) +
            "\nУсі робітники кидають роботу й беруть зброю; лучники йдуть на стіни." +
            $"\n{TipText.Dim("Ще раз — відбій, усі повертаються до роботи.")}  {TipText.Key(Key(KeyAction.Alarm))}";

        /// <summary>"День 2 · 14:00" from the world clock.</summary>
        public static string ClockShort()
        {
            int day = Combat.WorldClock.Day, hour = (int)Combat.WorldClock.HourOfDay;
            return $"День {day} · {hour:00}:00";
        }

        public static string Clock() =>
            TipText.Title("Ігровий час", UiTheme.Gold) + "\n" + ClockShort() +
            $"\nГодина гри — {Combat.GameBalance.GameHourSeconds:0} с. Від годин залежать приріст населення й реген." +
            "\n" + TipText.Dim("Пауза зупиняє час.");

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
