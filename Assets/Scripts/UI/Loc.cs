using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Language management and UI translation (Ukrainian and English).
    /// Defaults to Ukrainian in batchmode / automated tests unless explicitly set.
    /// Stores player choice in PlayerPrefs ("Settings_Language").
    /// </summary>
    public static class Loc
    {
        public enum Lang { Uk = 0, En = 1 }

        public const string PrefKey = "Settings_Language";

        public static Lang Current { get; private set; } = Lang.Uk;
        public static bool En => Current == Lang.En;
        public static bool HasChosen => PlayerPrefs.HasKey(PrefKey);
        public static string Name => En ? "English" : "Українська";

        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = Lang.Uk;
            Changed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadSaved()
        {
            if (Application.isBatchMode) return;
            if (!PlayerPrefs.HasKey(PrefKey)) return;
            Current = PlayerPrefs.GetInt(PrefKey, 0) == (int)Lang.En ? Lang.En : Lang.Uk;
        }

        public static void Set(Lang lang, bool save = true)
        {
            bool diff = Current != lang;
            Current = lang;
            if (save)
            {
                PlayerPrefs.SetInt(PrefKey, (int)lang);
                PlayerPrefs.Save();
            }
            if (diff) Changed?.Invoke();
        }

        public static void Cycle() => Set(En ? Lang.Uk : Lang.En);

        public static string Pick(string uk, string en) => En ? en : uk;

        public static bool HasCyrillic(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '\u0400' && c <= '\u04FF') return true;
            }
            return false;
        }

        /// <summary>Translates a Ukrainian UI string or phrase into English when Loc.En is active.</summary>
        public static string Tr(string text)
        {
            if (!En || string.IsNullOrEmpty(text)) return text;
            if (UkToEn.TryGetValue(text, out string direct)) return direct;
            if (!HasCyrillic(text)) return text;
            return TranslateComposite(text);
        }

        private static string TranslateComposite(string text)
        {
            // Line-by-line lookup first when multiline text has independently translatable lines
            if (text.Contains("\n"))
            {
                string[] lines = text.Split('\n');
                bool changed = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (UkToEn.TryGetValue(lines[i], out string mapped))
                    {
                        lines[i] = mapped;
                        changed = true;
                    }
                    else
                    {
                        string p = TryPatterns(lines[i]);
                        if (p != lines[i]) { lines[i] = p; changed = true; }
                    }
                }
                if (changed)
                {
                    string joined = string.Join("\n", lines);
                    if (!HasCyrillic(joined)) return joined;
                    text = joined;
                }
            }

            string pat = TryPatterns(text);
            if (!HasCyrillic(pat)) return pat;

            // Fallback ordered phrase replacement for rich-text / composite strings
            foreach (var pair in PhraseMap)
            {
                if (pat.Contains(pair.uk)) pat = pat.Replace(pair.uk, pair.en);
            }
            return pat;
        }

        private static string TryPatterns(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            foreach (var rule in PatternRules)
            {
                if (rule.regex.IsMatch(s)) return rule.regex.Replace(s, rule.replace);
            }
            return s;
        }

        private static readonly Dictionary<string, string> UkToEn = new Dictionary<string, string>
        {
            // Main Menu
            { "Збудуй поселення  ·  спорядь військо  ·  вистоїш проти набігів", "Build a settlement  ·  equip your army  ·  survive the raids" },
            { "Продовжити", "Continue" },
            { "Нова гра", "New Game" },
            { "Завантажити", "Load" },
            { "Гра в мережі", "Multiplayer" },
            { "Налаштування", "Settings" },
            { "НАЛАШТУВАННЯ", "SETTINGS" },
            { "Вихід", "Quit" },
            { "Збережень ще немає", "No saves yet" },
            { "Назад", "Back" },
            { "Мова", "Language" },
            { "Мова: Українська", "Language: English" },

            // Main Menu & Pause Settings
            { "Чутливість миші (від першої особи)", "Mouse Sensitivity (First Person)" },
            { "Чутливість камери мапи", "Map Camera Sensitivity" },
            { "Колесо миші на мапі (швидкість зуму)", "Map Scroll Wheel (Zoom Speed)" },
            { "Гучність", "Master Volume" },
            { "Загальна гучність:", "Master Volume:" },
            { "Музика", "Music" },
            { "Ефекти світу", "World SFX" },
            { "Оточення та інтерфейс", "Ambience & UI" },
            { "Графіка", "Graphics" },
            { "Масштаб інтерфейсу", "UI Scale" },
            { "Повний екран", "Fullscreen" },
            { "Роздільність", "Resolution" },
            { "Ліміт кадрів", "Frame Cap" },
            { "Клавіші можна перепризначити в грі: Esc → Налаштування", "Keys can be rebound in-game: Esc → Settings" },
            { "Низька", "Low" },
            { "Середня", "Medium" },
            { "Висока", "High" },
            { "так", "Yes" },
            { "вікно", "Windowed" },
            { "увімк.", "On" },
            { "вимк.", "Off" },
            { "без обмеження", "Unlimited" },
            { "ні", "No" },

            // Pause Menu
            { "ПАУЗА", "PAUSED" },
            { "Пауза", "Paused" },
            { "Відновити гру", "Resume Game" },
            { "Зберегти", "Save" },
            { "Головне меню", "Main Menu" },
            { "Вийти з гри", "Quit Game" },
            { "Керування", "Controls" },
            { "Звук", "Audio" },
            { "Клавіші", "Keybinds" },
            { "Гра", "Game" },
            { "Миша від першої особи", "First-Person Mouse" },
            { "Перетяг мапи", "Map Pan Speed" },
            { "Зум колесом на мапі", "Map Wheel Zoom" },
            { "Поради «Перші кроки»", "First Steps Advisor" },
            { "Автобіг", "Auto-Run" },
            { "сховано", "Hidden" },
            { "показано", "Shown" },
            { "Скинути клавіші", "Reset Keys" },
            { "Чутливість миші", "Mouse Sensitivity" },
            { "Чутливість / швидкість", "Sensitivity / Speed" },
            { "(Натисніть ESC або P)", "(Press ESC or P)" },

            // Join / Multiplayer / Lobby / Campaign Setup / Save Slots
            { "Адреса хоста: він бачить її у своєму лобі. Ви потрапите в лобі, оберете героя, а гра почнеться, коли хост натисне «Почати».", "Host address: shown in their lobby. You will enter the lobby, choose your hero, and the match begins once the host presses Start." },
            { "Приєднатися", "Join" },
            { "Або створіть гру самі, і інші зайдуть до вас:", "Or host a game yourself so others can join:" },
            { "Клас і стартовий артефакт кожен гравець, і ви теж, обирає в лобі.\n\nВи отримаєте адресу, яку скажете іншим. Коли всі будуть готові, натисніть «Почати».", "Each player—including you—chooses their class and starting artifact in the lobby.\n\nYou will receive an address to share with others. Once everyone is ready, press Start." },
            { "Нова кампанія", "New Campaign" },
            { "Нова мережева кампанія", "New Network Campaign" },
            { "Зі збереження", "From Save" },
            { "Вкажіть адресу хоста", "Enter host address" },
            { "З'єднання…", "Connecting…" },
            { "Адреса не схожа на IP, наприклад 192.168.0.5", "Address doesn't look like an IP, e.g. 192.168.0.5" },
            { "Збереження не читається", "Save file cannot be read" },
            { "Хост повертає всіх у лобі…", "Host is returning everyone to the lobby…" },
            { "Світ", "World" },
            { "Сид світу", "World Seed" },
            { "Випадковий", "Random" },
            { "випадковий", "random" },
            { "Розмір світу", "World Size" },
            { "Героїв у світі", "Heroes in World" },
            { "Королів серед них", "Kings Among Them" },
            { "Поради радника", "Advisor Tips" },
            { "Герої", "Heroes" },
            { "Ваш герой", "Your Hero" },
            { "Клас", "Class" },
            { "Стартовий артефакт", "Starting Artifact" },
            { "У лобі", "To Lobby" },
            { "Почати", "Start" },
            { "Лобі", "Lobby" },
            { "Гравці", "Players" },
            { "Ім'я", "Name" },
            { "ваше ім'я", "your name" },
            { "Артефакт", "Artifact" },
            { "Готовий", "Ready" },
            { "Не готовий", "Not Ready" },
            { "Вийти з лобі", "Leave Lobby" },
            { "Почати без них", "Start Anyway" },
            { "Зберегти гру", "Save Game" },
            { "Завантажити гру", "Load Game" },
            { "Мережева гра зі збереження", "Multiplayer from Save" },
            { "Порожньо", "Empty" },
            { "Точно?", "Sure?" },
            { "Перезаписати?", "Overwrite?" },
            { "назва збереження", "save name" },
            { "F5 — швидке збереження  ·  F9 — швидке завантаження  ·  автозбереження кожні 5 хв", "F5 — Quick Save  ·  F9 — Quick Load  ·  Autosave every 5 min" },

            // HUD / Windows / Toolbar / Help / Advisor
            { "Тривога: усі до зброї", "Alarm: Everyone to Arms" },
            { "Усі робітники при ділі", "All workers are busy" },
            { "Порада", "Tip" },
            { "Сховати поради", "Hide Tips" },
            { "Повернути — у налаштуваннях", "Re-enable in Settings" },
            { "Ціль виконано!", "Goal completed!" },
            { "Довідка", "Help" },
            { "Будівництво", "Build" },
            { "Стерти", "Erase" },
            { "Люди", "People" },
            { "Скинути вигляд", "Reset View" },
            { "До героя", "To Hero" },
            { "До бази", "To Base" },
            { "Споруди", "Buildings" },
            { "Готово\n<size=12>Esc</size>", "Done\n<size=12>Esc</size>" },
            { "Мапа", "Map" },
            { "Мапа світу", "World Map" },
            { "Ринок", "Market" },
            { "Вивести", "Release" },
            { "Верстак", "Workbench" },
            { "+ Покликати", "+ Call" },
            { "− Відпустити", "− Release" },
            { "Без робітника верстак стоїть — покличте когось", "Workbench is idle without a worker — call someone" },
            { "Далі в черзі", "Up Next" },
            { "Переплавка руди", "Ore Smelting" },
            { "Усе", "All" },
            { "Виготовити", "Craft" },
            { "Спорядження", "Equipment" },
            { "Склад", "Armory" },
            { "Найкраще зі складу", "Best from Armory" },
            { "Зброя", "Weapon" },
            { "Щит", "Shield" },
            { "Шолом", "Helmet" },
            { "Нагр.", "Chest" },
            { "Рукав.", "Gloves" },
            { "Чоботи", "Boots" },
            { "Герой", "Hero" },
            { "+5 сили", "+5 Strength" },
            { "Взяти", "Take" },
            { "Зцілитися", "Heal" },
            { "Блискавка", "Lightning" },
            { "Об'єднати осколки", "Combine Shards" },
            { "Райони: люди при спорудах", "Districts: Building Workers" },
            { "Робота без споруд", "Unassigned Jobs" },
            { "Усім: самостійно", "All: Autonomous" },
            { "Зняти обмеження", "Clear Limits" },
            { "Кінець гри", "Game Over" },
            { "Герой загинув, і нікому взяти владу.", "The hero has fallen, and no one remains to rule." },
            { "Поставити бота", "Assign Bot" },
            { "Перейти в лобі", "Return to Lobby" },
            { "Чекати", "Wait" },
            { "Зняти паузу (F6)", "Unpause (F6)" },
            { "Гравець вийшов", "Player Disconnected" },
            { "Редактор (F8)", "Dev Editor (F8)" },
            { "Безкінечні ресурси", "Infinite Resources" },
            { "Свій юніт перед героєм", "Spawn Ally in Front of Hero" },
            { "Людина", "Villager" },
            { "Воїн", "Warrior" },
            { "Лицар", "Knight" },
            { "Генерал", "General" },
            { "Ворог (найближче королівство)", "Enemy (Nearest Kingdom)" },
            { "Перейти на інший бік", "Switch Side" },
            { "Обрані на мапі → до ворога", "Selected on Map → Enemy" },
            { "Найближчий ворог → мій", "Nearest Enemy → Mine" },
            { "Посадити дерево", "Plant Tree" },
            { "Знести", "Demolish" },
            { "Відкрити", "Open" },
            { "Торгівля", "Trade" },
            { "Дерево", "Wood" },
            { "Колоди", "Logs" },
            { "Стій", "Stop" },
            { "Спорядж.", "Gear" },
            { "Гілки", "Branches" },
            { "Рубати", "Chop" },
            { "Руда", "Ore" },
            { "Шахта", "Mine" },
            { "Камінці", "Pebbles" },
            { "Носій", "Haul" },
            { "Райони", "Districts" },
            { "Авто", "Auto" },
            { "Скасувати", "Cancel" },
            { "Склад порожній — речі виготовляє верстак,\nтрофеї падають з переможених ворогів", "Armory is empty — craft items at the workbench\nor loot trophies from defeated enemies" },

            // Gameplay alerts, refusals & network messages
            { "Центральна імперія", "Central Empire" },
            { "Королівство", "Kingdom" },
            { "Редактор", "Editor" },
            { "Північ", "North" },
            { "Південь", "South" },
            { "зі сходу", "from the east" },
            { "з півночі", "from the north" },
            { "із заходу", "from the west" },
            { "з півдня", "from the south" },
            { "надто круто", "too steep" },
            { "вода", "water" },
            { "зайнято", "occupied" },
            { "чужа земля", "foreign land" },
            { "бракує ресурсів", "not enough resources" },
            { "Тут надто круто для будівлі", "Too steep for building here" },
            { "На воді не збудувати", "Cannot build on water" },
            { "Тут зайнято: навколо споруди потрібен прохід для людей", "Occupied: units need space around the building" },
            { "Ратуша вже є: її можна покращити", "Town Hall already exists: upgrade it instead" },
            { "Ратуша вже будується", "Town Hall is already under construction" },
            { "Найвищий рівень стін", "Walls are at max level" },
            { "Спершу збудуйте стіну", "Build a wall first" },
            { "Чужа земля: тут живуть люди короля", "Foreign land: a rival king rules here" },
            { "Поруч табір розбійників: спершу розбийте його або оберіть інше місце", "Bandit camp nearby: clear it first or pick another spot" },
            { "Нове поселення! Робітників сюди посилайте наказом; склад у нього свій", "New settlement! Send workers by command; it keeps its own stock" },
            { "Найвищий рівень", "Max level" },
            { "ратушу зруйновано", "town hall destroyed" },
            { "імператор загинув", "emperor slain" },
            { "нікого не лишилось", "no one left" },
            { "Два світозмінні артефакти — герой вибухнув!", "Two world-changing artifacts — the hero exploded!" },
            { "Немає ворога поблизу", "No enemy nearby" },
            { "Не вдалося", "Failed" },
            { "Тут усі місця зайняті", "All spots here are taken" },
            { "На стіну не піднятися: потрібна стіна зі сходами (і вільні місця)", "Cannot climb the wall: need a wall with stairs and free space" },
            { "Усі 4 місця біля верстака зайняті", "All 4 workbench spots are taken" },
            { "Серед обраних немає робітників", "No workers among selected units" },
            { "Не вистачає ресурсів", "Not enough resources" },
            { "Усі місця біля верстака зайняті", "All workbench spots are taken" },
            { "Немає вільних робітників", "No idle workers available" },
            { "Їжі лишилось менш ніж на день", "Less than a day of food remaining" },
            { "Їжа скінчилась: приріст населення зупинився", "Out of food: population growth stopped" },
            { "Бійців ще немає: воїнами стають у боях", "No fighters yet: villagers become warriors in combat" },
            { "Гра вже починається", "Game is already starting" },
            { "Гравець без імені", "Player must have a name" },
            { "Цей гравець уже в лобі (той самий комп'ютер)", "This player is already in the lobby (same machine)" },
            { "Такого класу немає", "Unknown hero class" },
            { "Такого артефакту немає", "Unknown artifact" },
            { "Центральну імперію в мережевій грі може взяти лише хост", "Only the host can choose the Central Empire in multiplayer" },
            { "Хост повертає всіх у лобі", "Host is returning everyone to the lobby" },
            { "Хост", "Host" },
            { "Гість", "Guest" },
            { "Хост прибрав вас із лобі", "Host removed you from the lobby" },
            { "Лобі немає", "No lobby active" },
            { "Гра почалася", "Game started" },
            { "стара", "legacy" },
            { "Гра вже йде: нові гравці заходять лише через лобі", "Game is in progress: new players can only join from the lobby" },
            { "Цей гравець уже в грі (той самий комп'ютер)", "This player is already in the game (same machine)" },
            { "Зв'язок із хостом втрачено", "Connection to host lost" },
            { "Хост закрив лобі", "Host closed the lobby" },
            { "Не вдалося приєднатися", "Failed to join" },
            { "Хост не відповідає", "Host is not responding" },
            { "працює в полі", "working in field" },
            { "працює на лісопилці", "working at sawmill" },

            // NPC kingdom names
            { "Вовчий край", "Wolf Reach" },
            { "Залізні пагорби", "Iron Hills" },
            { "Ясенова долина", "Ash Valley" },
            { "Кам'яний брід", "Stone Ford" },
            { "Сива рівнина", "Gray Plain" },
            { "Орлине гніздо", "Eagle Nest" },
            { "Дубовий гай", "Oak Grove" },
            { "Червоний яр", "Red Ravine" },
            { "Мідна гора", "Copper Mount" },
            { "Тихі заводі", "Still Waters" },
            { "Соколиний бір", "Falcon Pine" },
            { "Вітряний кряж", "Windy Ridge" },
            { "Срібний потік", "Silver Stream" },
            { "Ведмежий кут", "Bear Hollow" },
            { "Золоте поле", "Golden Field" },
            { "Туманна гать", "Misty Causeway" },
            { "Кленова балка", "Maple Gulch" },
            { "Високий вал", "High Rampart" },
            { "Березовий луг", "Birch Meadow" },
            { "Чорна скеля", "Black Crag" },
            { "Лисячі нори", "Fox Burrows" },
            { "Білий камінь", "White Stone" },
            { "Зелена діброва", "Greenwood" },
            { "Старий шлях", "Old Road" },
            { "Грозова вершина", "Storm Peak" },
            { "Медова пасіка", "Honey Apiary" },
            { "Кремінний брід", "Flint Ford" },
            { "Озерний край", "Lakeland" },
            { "Північна застава", "North Outpost" },
            { "Сонячний схил", "Sun Slope" },
            { "Артефакт не знімається", "Artifact cannot be unequipped" },
            { "Цей слот тримає артефакт", "This slot holds an artifact" },
            { "Їжа", "Food" },
            { "Провізія", "Provisions" },
        };

        private static readonly (Regex regex, string replace)[] PatternRules =
        {
            (new Regex(@"^Порт (\d+) зайнятий$"), "Port $1 is already in use"),
            (new Regex(@"^Мова: Українська$"), "Language: English"),
            (new Regex(@"^Гру збережено \(авто\)$"), "Game saved (auto)"),
            (new Regex(@"^Гру збережено: (.+)$"), "Game saved: $1"),
            (new Regex(@"^Гру завантажено$"), "Game loaded"),
            (new Regex(@"^Не вдалося зберегти гру$"), "Failed to save game"),
            (new Regex(@"^Цю гру зберігає хост$"), "Only the host can save this game"),
            (new Regex(@"^Збудовано: (.+)$"), "Built: $1"),
            (new Regex(@"^Виготовлено: (.+)$"), "Crafted: $1"),
            (new Regex(@"^Підібрано: (.+)$"), "Picked up: $1"),
            (new Regex(@"^Отримано: (.+)$"), "Received: $1"),
            (new Regex(@"^Трофей: (.+)$"), "Trophy: $1"),
            (new Regex(@"^Осколок: (.+)$"), "Shard: $1"),
            (new Regex(@"^Артефакт: (.+)$"), "Artifact: $1"),
            (new Regex(@"^Переплавлено: руда (.+) ×(\d+)$"), "Smelted: ore $1 ×$2"),
            (new Regex(@"^Група (\d+): (\d+)$"), "Group $1: $2"),
            (new Regex(@"^Групу (\d+) розпущено$"), "Group $1 cleared"),
            (new Regex(@"^\+(\d+) деревини$"), "+$1 wood"),
            (new Regex(@"^\+(\d+) руди$"), "+$1 ore"),
            (new Regex(@"^\+(\d+) їжі$"), "+$1 food"),
            (new Regex(@"^Нові робітники: (\d+)$"), "New workers: $1"),
            (new Regex(@"^Новий робітник$"), "New worker"),
            (new Regex(@"^Робота загартувала: новий воїн$"), "Work hardened a villager: new warrior"),
            (new Regex(@"^Голод: померла людина$"), "Starvation: a person died"),
            (new Regex(@"^Напад: (.+)$"), "Attack: $1"),
            (new Regex(@"^Склад і спорядження\s+\[(.+)\]$"), "Armory & Equipment  [$1]"),
            (new Regex(@"^Переплавити (\d+)$"), "Smelt $1"),
            (new Regex(@"^Безкінечні ресурси: (так|ні)$"), "Infinite resources: $1"),
            (new Regex(@"^День (\d+) · (\d\d:\d\d)$"), "Day $1 · $2"),
            (new Regex(@"^Тривога: до зброї \((.+)\)$"), "Alarm: to arms ($1)"),
            (new Regex(@"^Тривога! Вороги біля: (.+)$"), "Alarm! Enemies near: $1"),
            (new Regex(@"^Відбій \((.+)\)$"), "Stand down ($1)"),
            (new Regex(@"^Без діла: (\d+)  \((.+)\)$"), "Idle: $1  ($2)"),
            (new Regex(@"^Рівень ([IVX\d]+)$"), "Level $1"),
            (new Regex(@"^Рів\. (\d+)$"), "Lvl $1"),
            (new Regex(@"^Бандити йдуть на поселення: (\d+), (.+)!$"), "Bandits are marching on the settlement: $1, $2!"),
            (new Regex(@"^(.+) впало далеко звідси$"), "$1 fell far from here"),
            (new Regex(@"^(.+) іде в набіг на вас: (\d+) бійців!$"), "$1 is raiding you: $2 fighters!"),
            (new Regex(@"^(.+) іде в набіг на «(.+)»: (\d+) бійців!$"), "$1 is raiding «$2»: $3 fighters!"),
            (new Regex(@"^(.+) іде в набіг: (\d+) бійців!$"), "$1 is launching a raid: $2 fighters!"),
            (new Regex(@"^(.+) відступає з набігу$"), "$1 retreats from the raid"),
            (new Regex(@"^Героя (.+) вбито$"), "Hero of $1 was slain"),
            (new Regex(@"^Король (.+) загинув! Міжцарів'я$"), "King of $1 died! Interregnum"),
            (new Regex(@"^(.+): новий король$"), "$1: a new king rises"),
            (new Regex(@"^(.+) впало: (.+)$"), "$1 has fallen: $2"),
            (new Regex(@"^Нове поселення — від ратуші (.+)$"), "New settlement unlocks at Town Hall $1"),
            (new Regex(@"^Населення (\d+)/(\d+): збудуйте Ратушу$"), "Population $1/$2: build a Town Hall"),
            (new Regex(@"^Населення (\d+)/(\d+): покращте Ратушу$"), "Population $1/$2: upgrade your Town Hall"),
            (new Regex(@"^Потрібно (\d+) деревини$"), "Requires $1 wood"),
            (new Regex(@"^Потрібно (\d+) руди (.+) \(або переплавте нижчу на верстаці\)$"), "Requires $1 ore $2 (or smelt lower ore at the workbench)"),
            (new Regex(@"^Стіни (.+): \+(\d+)% шкоди зі стін$"), "Walls $1: +$2% wall damage bonus"),
            (new Regex(@"^Надто близько до поселення: треба щонайменше (\d+) м від ратуші$"), "Too close to another settlement: need at least $1 m from Town Hall"),
            (new Regex(@"^Ратуша (.+): населення до (\d+)$"), "Town Hall $1: population cap $2"),
            (new Regex(@"^(.+): на «(.+)» треба (\d+) дерева й (\d+) руди$"), "$1: «$2» needs $3 wood and $4 ore"),
            (new Regex(@"^Центральну імперію знищено — настала нова епоха світу \((\d+)\)$"), "Central Empire destroyed — a new world epoch begins ($1)"),
            (new Regex(@"^Біля будівництва всі (\d+) місця зайняті$"), "All $1 builder spots are taken"),
            (new Regex(@"^Не вистачає руди (.+): потрібно (\d+)$"), "Not enough ore $1: need $2"),
            (new Regex(@"^Усі обрані вже мають (.+)$"), "All selected already have $1"),
            (new Regex(@"^(.+): видано (\d+), ще (\d+) без — на складі скінчилось$"), "$1: equipped $2, $3 left without — armory out of stock"),
            (new Regex(@"^(.+): видано (\d+)$"), "$1: equipped $2"),
            (new Regex(@"^Лобі повне \((\d+) гравців\)$"), "Lobby is full ($1 players)"),
            (new Regex(@"^(.+) поставив паузу$"), "$1 paused the game"),
            (new Regex(@"^(.+) вийшов із гри: його королівство стереже бот$"), "$1 disconnected: their kingdom is guarded by a bot"),
            (new Regex(@"^(.+) вийшов із гри$"), "$1 disconnected"),
            (new Regex(@"^Вийшов: (.+)$"), "Disconnected: $1"),
            (new Regex(@"^Королівства стереже бот: (.+)$"), "Kingdoms guarded by bot: $1"),
            (new Regex(@"^Не готові: (.+)$"), "Not ready: $1"),
            (new Regex(@"^(.+) зайшов у гру$"), "$1 joined the game"),
            (new Regex(@"^Різні версії гри: у хоста (.+), у вас (.+)$"), "Version mismatch: host has $1, you have $2"),
            (new Regex(@"^Гравець не зайшов: у нього версія (.+), у вас (.+)$"), "Player rejected: their version is $1, yours is $2"),
            (new Regex(@"^(.+) не зайшов: гра вже йде$"), "$1 could not join: game already in progress"),
            (new Regex(@"^Ви в грі як гравець (\d+)\. (.+) — хто ще тут$"), "Joined as Player $1. Press $2 to view players"),
            (new Regex(@"^Уміння перезаряджається: (.+) с$"), "Skill on cooldown: $1 s"),
        };

        private static readonly (string uk, string en)[] PhraseMap =
        {
            ("Оберіть споруду; ЛКМ ставить, ПКМ скасовує", "Select a building; LMB places, RMB cancels"),
            ("Будівництво", "Building"),
            ("Звичайна руда", "Common Ore"),
            ("Мідно-залізна руда", "Copper-Iron Ore"),
            ("Срібно-золота руда", "Silver-Gold Ore"),
            ("Платинова руда", "Platinum Ore"),
            ("Мітрилова руда", "Mithril Ore"),
            ("Адамантитова руда", "Adamantite Ore"),
            ("Небесна руда", "Celestial Ore"),
            ("Драконова руда", "Dragon Ore"),
            ("Давня реліктова руда", "Ancient Relic Ore"),
            ("Рідкісна руда", "Rare Ore"),
            ("Центральна імперія", "Central Empire"),
            ("Перемога! Усі сусідні королівства впали", "Victory! All rival kingdoms have fallen"),
            ("Поразка: королівство гравця впало", "Defeat: your kingdom has fallen"),
            ("Тривога! Усі до зброї", "Alarm! Everyone to arms"),
            ("Відбій: усі до роботи", "Stand down: back to work"),
            ("Тривога! Вороги біля поселення", "Alarm! Enemies near the settlement"),
            ("Маг атакує лише з посохом (зробіть на верстаті)", "The mage only attacks with a staff (craft one at the workbench)"),
            ("Блискавці нема в кого влучити", "No enemy in range for Lightning"),
            ("Уміння перезаряджається:", "Skill on cooldown:"),
            ("Розміщуємо:", "Placing:"),
            ("ЛКМ — побудувати", "LMB — build"),
            ("ПКМ — скасувати", "RMB — cancel"),
            ("Z / X — поворот", "Z / X — rotate"),
            ("Tab — руда", "Tab — ore"),
            ("Робітники:", "Workers:"),
            ("ще йдуть:", "on the way:"),
            ("швидкість", "speed"),
            ("Верстак вільний", "Workbench idle"),
            ("Виготовляється:", "Crafting:"),
            ("Черга порожня", "Queue empty"),
            ("ще ", "more "),
            (" у черзі", " in queue"),
            ("На складі поки порожньо", "Armory is empty"),
            ("На складі:", "In armory:"),
            ("Спорядження", "Equipment"),
            ("У героя цей слот тримає артефакт", "An artifact occupies this hero slot"),
            ("У героя слот вільний", "Hero slot is empty"),
            ("У героя зараз:", "Hero currently wears:"),
            ("Герою, якщо надягти:", "For hero if equipped:"),
            ("Здоров'я", "Health"),
            ("Шкода", "Damage"),
            ("Дальність", "Range"),
            ("Поглинання шкоди", "Damage Reduction"),
            ("Поглинання", "Armor"),
            ("Сила", "Strength"),
            ("швидкість", "speed"),
            ("удар раз на", "attack every"),
            ("Слот вільний", "Slot empty"),
            ("Замість:", "Replaces:"),
            ("Клік — надягти", "Click — equip"),
            ("Клік — зняти на склад", "Click — store in armory"),
            ("Якщо зняти:", "If removed:"),
            ("Тримає цей слот, не знімається", "Locked in this slot, cannot be removed"),
            ("Порожньо — оберіть річ на складі", "Empty — choose an item from the armory"),
            ("Видати: усім обраним", "Issue: all selected"),
            ("Видати: лише цьому", "Issue: this unit only"),
            ("Клік по речі — по одній кожному обраному", "Click an item — one to each selected unit"),
            ("Клік по речі — надягти", "Click an item — equip"),
            ("клік по слоту — зняти на склад", "click a slot — store in armory"),
            ("закрити", "close"),
            ("дерева", "wood"),
            ("дерево", "wood"),
            ("їжі", "food"),
            ("руди", "ore"),
            ("руда", "ore"),
            ("осколки", "shards"),
            ("Осколки:", "Shards:"),
            ("Артефакти:", "Artifacts:"),
            ("Стартовий артефакт:", "Starting Artifact:"),
            ("Клас обирається один раз.", "Class is chosen once."),
            ("ратушу зруйновано", "town hall destroyed"),
            ("імператор загинув", "emperor slain"),
            ("нікого не лишилось", "no one left"),
            ("зі сходу", "from the east"),
            ("з півночі", "from the north"),
            ("із заходу", "from the west"),
            ("з півдня", "from the south"),
            ("Столиця", "Capital"),
            ("Поселення ", "Settlement "),
            ("Королівство", "Kingdom"),
            ("Редактор", "Editor"),
            ("Гравець ", "Player "),
            ("Хост", "Host"),
            ("Вовчий край", "Wolf Reach"),
            ("Залізні пагорби", "Iron Hills"),
            ("Ясенова долина", "Ash Valley"),
            ("Кам'яний брід", "Stone Ford"),
            ("Сива рівнина", "Gray Plain"),
            ("Орлине гніздо", "Eagle Nest"),
            ("Дубовий гай", "Oak Grove"),
            ("Червоний яр", "Red Ravine"),
            ("Мідна гора", "Copper Mount"),
            ("Тихі заводі", "Still Waters"),
            ("Соколиний бір", "Falcon Pine"),
            ("Вітряний кряж", "Windy Ridge"),
            ("Срібний потік", "Silver Stream"),
            ("Ведмежий кут", "Bear Hollow"),
            ("Золоте поле", "Golden Field"),
            ("Туманна гать", "Misty Causeway"),
            ("Кленова балка", "Maple Gulch"),
            ("Високий вал", "High Rampart"),
            ("Березовий луг", "Birch Meadow"),
            ("Чорна скеля", "Black Crag"),
            ("Лисячі нори", "Fox Burrows"),
            ("Білий камінь", "White Stone"),
            ("Зелена діброва", "Greenwood"),
            ("Старий шлях", "Old Road"),
            ("Грозова вершина", "Storm Peak"),
            ("Медова пасіка", "Honey Apiary"),
            ("Кремінний брід", "Flint Ford"),
            ("Озерний край", "Lakeland"),
            ("Північна застава", "North Outpost"),
            ("Сонячний схил", "Sun Slope"),
            ("Рівень ", "Level "),
            ("людей", "people"),
            ("юнітів", "units"),
            ("будівель", "buildings"),
            ("у грі", "played"),
            ("год", "h"),
            ("хв", "m"),
            ("м/с", "m/s"),
            ("мс", "ms"),
            (" м", " m"),
            (" км", " km"),
            (" с", " s"),
        };
    }

    /// <summary>
    /// Automatically keeps a UI Text translated to Loc.Current and refreshes whenever Loc.Changed fires.
    /// Stores the original Ukrainian string when attached so toggling Uk <-> En is reversible without restarting.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocText : MonoBehaviour
    {
        private TMP_Text label;
        private string ukSource;
        private string lastSeenText;
        private bool updating;

        public static LocText Attach(TMP_Text t)
        {
            if (t == null) return null;
            LocText lt = t.GetComponent<LocText>();
            if (lt == null) lt = t.gameObject.AddComponent<LocText>();
            lt.Bind(t);
            return lt;
        }

        public static void SetSource(TMP_Text t, string uk)
        {
            if (t == null) return;
            LocText lt = Attach(t);
            if (lt == null) return;
            lt.ukSource = uk;
            lt.Apply();
        }

        public static void Detach(TMP_Text t)
        {
            if (t == null) return;
            LocText lt = t.GetComponent<LocText>();
            if (lt == null) return;
            lt.Unbind();
            if (Application.isPlaying) Destroy(lt);
            else DestroyImmediate(lt);
        }

        private void Unbind()
        {
            if (label == null) return;
            Loc.Changed -= OnLanguageChanged;
            label = null;
        }

        private void Bind(TMP_Text t)
        {
            if (label != null) return;
            label = t;
            ukSource = t.text;
            lastSeenText = t.text;
            Loc.Changed += OnLanguageChanged;
            if (Loc.En) Apply();
        }

        private void OnDestroy() => Unbind();

        private void OnLanguageChanged() => Apply();

        private void Apply()
        {
            if (label == null || ukSource == null) return;
            string want = Loc.En ? Loc.Tr(ukSource) : ukSource;
            if (label.text == want)
            {
                lastSeenText = want;
                return;
            }
            updating = true;
            label.text = want;
            lastSeenText = want;
            updating = false;
        }

        private void LateUpdate()
        {
            if (updating || label == null) return;
            string current = label.text;
            if (current == lastSeenText) return;
            if (!Loc.En)
            {
                ukSource = current;
                lastSeenText = current;
                return;
            }
            if (Loc.HasCyrillic(current))
            {
                ukSource = current;
                string tr = Loc.Tr(current);
                if (tr != current)
                {
                    updating = true;
                    label.text = tr;
                    updating = false;
                    lastSeenText = tr;
                    return;
                }
            }
            lastSeenText = current;
        }
    }
}
