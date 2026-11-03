# Battle of Ages — архітектура коду

> Що гра робить і чому — у [GAME_DESIGN.md](GAME_DESIGN.md) (джерело правди). Цей файл — карта коду: де що лежить, як
> частини знаходять одна одну, правила, яких тримаємося. Оновлено 2026-10-27 (після проходу якості коду).
>
> Unity 6 (`6000.3.6f1`), URP, C# 9. Збірки: `BattleOfAges.Runtime` (`Assets/Scripts`), редакторські інструменти
> (`Assets/Editor`), тести `BattleOfAges.Tests` (EditMode) і PlayMode (`Assets/Tests`).

## 1. Папки

| Папка | Що там |
|---|---|
| `AI` | вороги й звірі: `AIBaseController` (стани), `HostileMobAI` (бандити, облоги, драбини), `NpcKingdom` / `HostileSettlementLeader` (королі-ШІ, набіги), `PeacefulAnimalAI` |
| `RTS` | свої юніти: `RtsUnit` (рух, бій, повідець, стіни), `NpcResourceGatherer` (робота), `RtsUnitCommander` (виділення, накази), `SettlementAlarm`, реєстри `UnitRegistry` / `WorkerRegistry` / `UnitSpatialGrid` |
| `Combat` | `HealthComponent`, `CombatUnit`, ранги й прокачка (`UnitProgress`, `HeroProgress`, `HeroSkills`, `HeroClass`), артефакти, `WorldClock`, **`GameBalance`** — усі числа балансу |
| `Building` | будівлі (`BuildingNode`, `TownHall`, `MineNode`), стіни (`WallSegment`, `WallBarriers`, `SiegeLadder`, `WallWalk`, `Fortifications`), `BuildingPlacementSystem`, `BuildGhost` |
| `Resources` | дерева, камінці руди, скриня, `ResourceManager` (склад), `ResourceGrid` (дерева й камінці по клітинках) |
| `Crafting` | речі, `Equipment` (6 слотів), `Armory` (спільний склад), верстак і черга крафту |
| `World` | нескінченний світ шматками (`InfiniteWorldManager`, `ProceduralBiomeChunk`), пул дерев і каміння (`EnvironmentPoolManager`) |
| `Player` | гравець від першої особи: рух, бій, збір, руки й зброя в кадрі |
| `Managers` | `GameContext`, `GameModeManager` (перша особа / мапа), налаштування (`SettingsManager`, `DisplaySettings`, `GraphicsQuality`) |
| `Input` | `UniversalInput` (стара й нова система вводу), `KeybindsManager` (перепризначення клавіш) |
| `SaveLoad` | `SaveSystem`, `WorldPersistence` |
| `UI` | §5 |
| `Utils` | будівники моделей (MeshKit, SDF-істоти, персонажі на ригу), допоміжне |

## 2. Як частини знаходять одна одну

- **`GameContext`** — режим гри, командир, камера мапи, гравець. Вони реєструються самі в `Awake`; інакше один пошук із
  кешем, відсутнього гравця шукають не частіше разу на кадр. **Не шукати по сцені** (`FindFirstObjectByType`,
  `GameObject.Find`) у коді, що працює щокадру.
- **Реєстри замість пошуку:** `UnitRegistry`, `WorkerRegistry`, `UnitSpatialGrid` (вороги поруч), `ResourceGrid`
  (найближче дерево / камінець кільцями клітинок), `BuildingNode.Active`, `MineNode.ActiveMines`, `WallSegment.Active`,
  `SiegeLadder.Active`, `StorageChestNode.ActiveChests`. Об'єкт сам записується при `OnEnable` і виписується при `OnDisable`.
- **Статичні події:** `HealthComponent.AnyDamaged` (сповіщення про напад, числа урону), `Equipment.AnyChanged`,
  `KeybindsManager.OnKeybindsChanged`, `AttackAlerts.Raised`. Хто підписався в `OnEnable`, відписується в `OnDisable`.
- **Хто є хто — не за іменем.** Позначки: `BuildGhost` (примара будівлі), `RtsUnit.IsVillager`. Ім'я об'єкта читаємо
  лише для показу й для старих об'єктів сцени (одне місце в `RtsUnit.Awake`).

## 3. Юніти й світ

- **Свій юніт** = `RtsUnit` (рух через `GroundMotor`, бій, стіни) + `CombatUnit` + `HealthComponent` + `NpcResourceGatherer`
  (робота; без наказу — боєць напоготові). Бій, який юніт почав сам, має повідець (`GameBalance.UnitLeashRadius`).
- **Ворог** = `HostileMobAI` (стани з `AIBaseController`): погоня, облога (`HandleWallInWay`, драбини), бій на хіднику.
- **Світ** стрімиться шматками навколо гравця; дерева й каміння беруться з пулу (місце ставиться до `SetActive(true)`).
- **Час:** `WorldClock` (ігрові години; реген, приріст населення).
- **Числа** — лише в `GameBalance`; рішення з дизайну — з посиланням на розділ `GAME_DESIGN.md`.

## 4. Продуктивність (правила)

- Нічого не шукати по сцені й не алокувати щокадру в гарячих місцях; фізичні запити — `NonAlloc`, краще — сітки.
- Текст в інтерфейсі будувати, лише коли змінилось число (`Shown`); підказки читають текст 4 рази на секунду.
- Тисячі однакових об'єктів — через пули й реєстри; моделі — злиті меші й далекі LOD.
- **Кожну оптимізацію міряти** `PerformanceProbe` (кадр, скрипти, GC за кадр) і `StressTest` (1000 юнітів, 500 на 500) —
  до і після, того самого дня. Приклад: вкладені полотна для HUD поміряли — виграшу при нашому розмірі HUD немає, не
  лишили. На 2026-10-27 скрипти — ~0,5 мс на кадр у звичайній сцені; власний менеджер `Update` потрібен буде лише для
  масових боїв (M10), рішення — за заміром `StressTest`.

## 5. Інтерфейс

- **`UiTheme`** — весь вигляд: кольори, шрифт, спрайти (рамки, кнопки, слоти, іконки, курсори — малюються кодом),
  будівники (`Rect`, `Child`, `Label`, `TextButton`, `PanelImage`, `Divider`, `AddButton`), `CreateCanvas` (полотно з
  масштабом інтерфейсу). Нове вікно будується цими будівниками.
- **HUD:** `StrategyHud` (полотно, панель ресурсів, підказка режиму, кнопки на мапі) збирає частини: `HudFirstPerson`,
  `HudMinimap`, `HudWorldLabels`, `HudToasts`, `Advisor`, `CombatFeedback`. Повідомлення — `StrategyHud.Notify`.
- **Підказки:** `Tooltip.Set(go, text)` / `TipText` (заголовок, приглушене, клавіша, порівняння статів).
- **Рух і звук:** `PopIn` (вікно з'являється), `ButtonFx` (кнопка відгукується; додає `UiTheme.AddButton`), `UiSound`
  (звуки згенеровані кодом).
- **Старі вікна** (інвентар, розподіл робіт, пауза зі сцени) зібрані без теми; `UiRestyler` один раз після завантаження
  сцени дає їм вигляд `UiTheme`, рядки, створені пізніше, — `UiTheme.RestyleTree`. Нове так не робити.
- Вікна — `GameWindows` (що відкрите, `BlockGameplay`, закрити все).

## 6. Тести й інструменти

- **EditMode** (`Assets/Tests/EditMode`) — правила й формули; **PlayMode** — поведінка в сцені. Нова система — з тестами
  обох видів. Запуск у batch — `unity.ps1 -Mode EditMode|PlayMode [-Filter ...]` (лише один batch Unity одночасно).
- **Інструменти перегляду** (Explicit, PlayMode): `UiCapture`, `MenuCapture`, `ObjectCapture`, `KnightCapture`,
  `GearCapture`... — знімки в `CAPTURE_DIR`; заміри — `PerformanceProbe`, `StressTest`, `BalanceProbe`, `SessionSim`.
- Редакторські інструменти перебудови (`Assets/Editor`): моделі персонажів, руки від першої особи, сцена, збірки
  (`BuildGameTool`).

## 6.1 Поселення, зони, абстрактний шар (M5, M6)

- **Склади:** `SettlementStock` на кожну ратушу (`TownHall.Stock`). `ResourceManager` сам віддає склад поселення на екрані (`Viewed`), а робота на місці йде через `For(place)`.
- **Належність:** усе належить до найближчої ратуші (`TownHall.Nearest`), людей рахує `People`.
- **Земля:** `InfiniteWorldManager.HoldSettlements` тримає чанки навколо кожної ратуші, навіть коли герой далеко.
- **Зони:** `ZoneMap` (клітинки 8 м), `ZonePainter` (пензель), `ZoneToolbar` (кнопки мапи), `ZoneGrower` (ставить ділянки), `ConstructionSite` (будують робітники), `WorkPlace` (поле,
  лісопилка), `Caravans` (між поселеннями).
- **Рух:** `GroundMotor` (рельєф, озера, стіни, `Footprints` будівель); `GameBalance.WalkerSeparation`, `StandingSeparation`, `PressBeforeBypass`.
- **Абстрактний шар:** `KingdomRealm` (далекі королівства даними), `KingdomEconomy`, `AbstractBattle` / `UnitStats` (бій цифрами). Після зміни бойового балансу зіграти заново
  `AbstractBattleCalibration.PlayTheBattles`.
- **Імперія:** `NpcKingdom.IsEmpire` у кожній кампанії, `CentralEmpire.Active` лише коли її тримає гравець.

Заміри: `PaceSim` (темп зі справжнього старту; `SIM_ZONES`: що дає кожна зона), `ZoneCapture`, `SettlementCapture`, `PushProbe`.

## 7. Відомий борг

1. **Стан і показ змішані** в MonoBehaviour, дії викликаються напряму — для мережі (M8) потрібні команди й окремий стан
   (`GAME_DESIGN.md` §14.3). Нові системи (стосунки §22) робити одразу як дані + команди.
2. Старі вікна (інвентар, розподіл робіт, пауза зі сцени) — перебудувати на будівниках `UiTheme`, тоді `UiRestyler`
   не потрібен зовсім.
3. Текст — старий `UnityEngine.UI.Text`; TextMeshPro дав би чіткий текст за будь-якого масштабу інтерфейсу.
4. Великі класи: `NpcResourceGatherer`, `RtsUnit`, `SaveSystem`, `NpcKingdom` — ділити на частини, як `StrategyHud`.
5. `AIBaseController.IsTerrain` ще впізнає землю за іменем об'єкта.
