# Battle of Ages — архітектура коду

> Що гра робить і чому — у [GAME_DESIGN.md](GAME_DESIGN.md) (джерело правди). Цей файл — карта коду: де що лежить, як
> частини знаходять одна одну, правила, яких тримаємося. Оновлено 2026-11-15. Як додати нове — [SYSTEMS_GUIDE.md](SYSTEMS_GUIDE.md).
>
> Unity 6 (`6000.3.6f1`), URP, C# 9. Збірки: `BattleOfAges.Runtime` (`Assets/Scripts`), редакторські інструменти
> (`Assets/Editor`), тести `BattleOfAges.Tests` (EditMode) і PlayMode (`Assets/Tests`).

## 1. Папки

| Папка | Що там |
|---|---|
| `AI` | вороги й звірі: `AIBaseController` (стани), `HostileMobAI` (бандити, облоги, драбини), `NpcKingdom` / `HostileSettlementLeader` (королі-ШІ, набіги), `PeacefulAnimalAI`; далекий світ даними — `KingdomRealm`, `FarWorld`, `KingdomEconomy`, `AbstractBattle`; рух усіх ШІ — `CrowdSim` (§4.1) |
| `RTS` | свої юніти: `RtsUnit` (рух, бій, повідець, стіни), `NpcResourceGatherer` (робота), `RtsUnitCommander` (виділення, накази), `SettlementAlarm`, реєстри `UnitRegistry` / `WorkerRegistry` / `UnitSpatialGrid` |
| `Combat` | `HealthComponent`, `CombatUnit`, ранги й прокачка (`UnitProgress`, `HeroProgress`, `HeroSkills`, `HeroClass`), артефакти, `WorldClock`, **`GameBalance`** — усі числа балансу |
| `Building` | будівлі (`BuildingNode`, `TownHall`, `MineNode`), стіни (`WallSegment`, `WallBarriers`, `SiegeLadder`, `WallWalk`, `Fortifications`), `BuildingPlacementSystem`, `BuildGhost` |
| `Resources` | дерева, камінці руди, скриня, `ResourceManager` (склад), `ResourceGrid` (дерева й камінці по клітинках) |
| `Crafting` | речі, `Equipment` (6 слотів), `Armory` (спільний склад), верстак і черга крафту |
| `World` | скінченний світ шматками (`InfiniteWorldManager`, `ProceduralBiomeChunk`, `WorldSettings`), груба карта й стоянки (`CoarseWorldMap`, `StartSites`), озера, трава, далекий рельєф, зони (`ZoneMap`), пул дерев і каміння (`EnvironmentPoolManager`) |
| `Player` | гравець від першої особи: рух, бій, збір, руки й зброя в кадрі |
| `Managers` | `GameContext`, `GameModeManager` (перша особа / мапа), налаштування (`SettingsManager`, `DisplaySettings`, `GraphicsQuality`) |
| `Input` | `UniversalInput` (стара й нова система вводу), `KeybindsManager` (перепризначення клавіш) |
| `SaveLoad` | `SaveSystem` (слоти, F5/F9), `WorldPersistence` (зміни світу), `WorldState` (стан королівств, армій, героїв даними), `CampaignSetup` |
| `Net` | мережа (§6.2) |
| `Camera` | `RtsMapCameraController` (ортографічна камера мапи) |
| `UI` | §5 |
| `Utils` | будівники моделей (`MeshKit`, SDF-істоти, персонажі на ригу, ліплені лицар / генерал / герой — `KnightModelBuilder`), GPU-скінінг далеких (`GpuSkinnedCharacter`, `GpuSkinBake`), `FootIK`, допоміжне |

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

### 4.1 Натовп (`AI/CrowdSim`, 2026-11-10)

- У `AIBaseController` немає `Update`: усіх ШІ-юнітів веде один `CrowdSim`. Рух на відкритому ґрунті рахує Burst-задача на всіх
  ядрах (ціль, поворот, розштовхування з сітки юнітів, рельєф із плиток висот, обхід стовбурів і каменів, «застряг»), вона ж каже,
  кому час думати, кому перевірити шлях попереду (проби йдуть одним пакетом `CapsulecastCommand`) і чий вигляд змінився.
- Біля будівель, стін, кілець міст, озер і краю світу (клітинки ґрунту 8 м класу 2), під час підйому драбиною чи дороги в обхід
  кільця юніта рухає старий код (`ExecuteMovement` + `GroundMotor`): правила лишаються в одному місці. Нова перешкода має
  викликати `CrowdSim.GroundChanged` або потрапити в `Classify`.
- `UnitSpatialGrid` бере місця юнітів натовпу із задачі (`SetFed`), а не з `Transform`. Колайдер юніта вимкнений далі 250 м від
  глядача. `CombatUnit` і `CharacterAnimationDriver` сплять, поки не потрібні. Дим димарів горить лише ближче 170 м.
- Замір: `MassProbe` (100 міст × 100 людей, ~19 600 споруд; `MASS_QUICK=1` — лише перший вид із деревом профайлера).
  2026-11-10 у редакторі: 8 → 37 FPS від першої особи, 7 → 33 у місті, 7 → 38 на мапі. Далі впирається в малювання
  (11–16 тис. пакетів, 25–34 млн трикутників) і в юнітів під старим кодом; `CrowdSim.Enabled = false` вмикає старий шлях для порівняння.

### 4.2 Малювання мас (2026-11-16)

- **Споруди й пропи** малює GPU Resident Drawer (URP, `PC_RPAsset`: `GPU Resident Drawer = Instanced Drawing`; Graphics →
  `BatchRendererGroup Variants = Keep All`, інакше у збірці варіанти зникнуть). Умова для шейдера: пакетний варіант
  (`DOTS.hlsl`), усе з `UnityPerMaterial` оголошене у `Properties`, матриця об'єкта — лише через `GetObjectToWorldMatrix()`.
  Рендерер із `MaterialPropertyBlock` у пакет не йде.
- **Далекі юніти** малює `Utils/CrowdDraw` (один `BatchRendererGroup`): у юніта немає окремого рендерера, він має місце в
  масивах (позиція, поворот, кліп). Юнітам натовпу позицію й кліп (стояти / йти / бігти, темп) пише сама задача `CrowdSim`;
  решту (свої юніти, трупи) щокадру веде `GpuSkinnedCharacter` за трансформом. Відсікання для камери й кожного каскаду тіней
  і вибір моделі — Burst-задача на вид. Дрібний на екрані (нижче `CrowdDraw.LowScreenHeight`) малюється легкою моделлю
  (`GpuSkinBake.LowMesh`, злиття точок по клітинках 8 см).
- **Кільце міських стін** задача знає сама (клітинки класу 3, `CrowdSim.Ring`): обхід, ворота, ковзання вздовж стіни. Стіна
  впала чи піднялась — `KingdomTown.RingsVersion`.
- Замір у збірці: `BattleOfAges.exe -smokeshot shot.png -smokemass 100` (кадри за секунду в `Player.log`, знімки поруч).
  2026-11-16, 10 000 юнітів і 19 600 споруд, RTX 4060 Laptop, 1920×1080: редактор 45 → 74 FPS між містами, 36 → 67 у місті,
  40 → 77 на мапі; збірка — 116 / 95 / 129.

### 4.3 60 кадрів на середньому залізі (2026-11-16)

- `GraphicsQuality` тримає 60 кадрів двома важелями, щосекунди за `FrameTimingManager` (у Player Settings увімкнено
  Frame Timing Stats): **відеокарта** тримає кадр — падає роздільність (`Verdict`, поріг 17,5 мс замість колишніх 30);
  **процесор** тримає кадр — спрощуються тіні сонця (`CpuVerdict`, `CpuRelief` 1: на каскад менше й коротші; 2: один
  короткий каскад, без SSAO, грубіші далекі моделі). Назад — коли головний потік має запас, з подвоєнням паузи, якщо
  повернення знову гальмує.
- Карта з менш як 6 ГБ пам'яті стартує із «Середньої».
- Замір у збірці: `-smokemass 100` пише час головного потоку, потоку малювання й GPU; `-smokequality 0|1|2`,
  `-smokedynamic`, `-smoketarget N` (ціль у кадрах: на швидкій машині показує регулятор у дії). 2026-11-16, 10 000 юнітів
  і 19 600 споруд, Ryzen 9 7945HX + RTX 4060 Laptop: головний потік 7,5–11,5 мс на «Високій» (GPU 3,7–7,7 мс), у місті
  11,5 → 8,8 мс із `CpuRelief` 2. Кількість ядер майже не важить (4 ядра без SMT: 90–110 кадрів), важить швидкість потоку.

### 4.4 До 200 кадрів: 5000 людей і ~9900 споруд (2026-11-16/17)

- **Руки від першої особи** малює `Player/ViewmodelPass` у кадрі головної камери (глибина стирається, своя проєкція й
  `_ProjectionParams`, тоді назад), а не друга камера в стеку: та коштувала ~0,7 мс кожного кадру (своє відсікання всієї
  сцени, свої LOD, своя карта тіней). Об'єкт «FPS Viewmodel Camera» лишився носієм рук і чисел, його `Camera` вимкнена;
  власні проходи рендерерів не бачать шар 7 (Viewmodel: `Opaque/Transparent Layer Mask`), руки не кидають тіні.
- **Тіні натовпу** завжди легкою моделлю (`CrowdDraw`, `Light ? low`): обрис той самий, а каскади малювали кожне тіло повністю.
- **Поля міст**: далеко — одна спільна легка модель, нахилена під схил (`ZoneBuildingBuilder.AttachField`); поблизу — своя,
  накинута на рельєф. Було 18 тис. трикутників і окремий виклик на кожне поле на будь-якій відстані.
- **Крони й каміння** (`StylizedWindFoliage`, `ProceduralRock`) мають пакетні варіанти — їх бере GPU Resident Drawer. Устя
  шахт — один матеріал на всі.
- **SSAO** рахувався з глибини після непрозорих (`AfterOpaque`, `Source: Depth`), без окремого проходу глибини й нормалей.
  **Повернуто (2026-11-17):** на траві нормалі з глибини хибні, і на «Високій» з'явилися темні плями й крапчасті пікселі
  вздовж стебел (скарга тестера на 0.3.11; порівняння в `ObjectCapture` з `CAPTURE_GRASS=1`). Знову `Source: DepthNormals`,
  не після непрозорих, сила 1,1, як до оптимізації. Прохід глибини й нормалей коштує кадру лише на «Високій».
- `GpuSkinnedCharacter` переглядає шістнадцяту частину персонажів за кадр (була чверть).
- Перевірено й відкинуто: GPU-відсікання затулених (GPU Resident Drawer, `OcclusionCullingInCameras`) — кадр +0,7 мс.
- Замір у збірці, 1920×1080, RTX 4060 Laptop, `-smokemass 50`: 165 / 118 / 185 / 197 → 228 / 154 / 228 / 245 кадрів (поле /
  місто / мапа / мапа зблизька; «після» на масштабі 0,90). Сама логіка (збірка з `-batchmode`, без малювання) — 2,5 мс:
  у місті тримає відеокарта. Редактор, `MASS_BREAKDOWN=1`, у місті: GPU ~6,8 мс, з них непрозорі ~3,5, тіні сонця ~2,2,
  SSAO ~0,65.
- Інструменти: `MassProbe` з `MASS_BREAKDOWN=1` — трикутники, пакети й час GPU (свої проходи, `Recorder.gpuElapsedNanoseconds`)
  з вимкненою частиною сцени, по видах міста й по шейдерах; `BUILD_DEV=1` — збірка для розробки, `-smokeprofile` і
  `Editor/ProfileDumpTool` (`PROFILE_DIR`) — дерево профайлера самого гравця. Гравець не малює при вимкненому екрані (кадр
  чекає на показ), редактор у batch — малює в текстуру.
- **Розтягнуті й зім'яті тіла в пакеті натовпу (0.3.10–0.3.11, виправлено в 0.3.12):** `Custom/CharacterSurface` брав текстуру
  запікання `_BoneTex`, якої не було у `Properties` (її ставив `SetTexture`). Пакет `BatchRendererGroup` (DOTS) прив'язує лише
  оголошені властивості матеріалу, тож цілий вид тіл отримував текстуру, яку лишило інше малювання, тобто чуже запікання.
  **Правило:** усе, що читає шейдер, який малює пакет (наш `CrowdDraw` чи GPU Resident Drawer), має бути у `Properties`.
  Відтворювалося приблизно в 2 з 3 прогонів `-smokemass 30` у гравці (вид мапи), у знімках редактора — ніколи. Тоді ж:
  час кліпів у шейдері — глобальний `_GpuSkinTime` (`Time.timeSinceLevelLoad`), бо `_Time.y` в URP — це `Time.time`, і в
  зібраній грі він випереджав на час меню (удари й переходи не грали); опорний стовпець і рядок маски беруться з розміру
  текстури; ключ запікання — усі bindpose; реєстрації сіток і матеріалів у пакеті з власним лічильником.
  Інструменти: `-smokemass` пише в журнал `GpuSkinnedCharacter.DumpBatch` для кожного виду (процесорний скінінг кожного місця,
  перевірка кожного виду, порівняння буфера з GPU); ключ гравця `-gpudebug far|low|hide|noswing|noloose|keepkinds` для пошуку.

## 5. Інтерфейс

- **Текст — TextMeshPro** (`TMP_Text`, `TextMeshProUGUI`, поля вводу — `TMP_InputField`): `UiTheme.TmpFont` — системна
  шрифтова гарнітура (Palatino / Georgia / Constantia) у динамічному атласі SDF, із запасним LiberationSans (кирилиця),
  тінь — матеріал `UiTheme.TmpShadowed` (ефект `Shadow` uGUI на TMP не діє), `UiTheme.Align` / `Style` / `Dress`.
  Ресурси TMP — у `Assets/TextMesh Pro` (`TmpSetupTool`: імпорт і перетворення старих `Text` у сценах).
- **`UiTheme`** — весь вигляд: кольори, шрифт, спрайти (рамки, кнопки, слоти, іконки, курсори — малюються кодом),
  будівники (`Rect`, `Child`, `Label`, `TextButton`, `PanelImage`, `Divider`, `AddButton`), `CreateCanvas` (полотно з
  масштабом інтерфейсу). Нове вікно будується цими будівниками.
- **HUD:** `StrategyHud` (полотно, панель ресурсів, підказка режиму, кнопки на мапі) збирає частини: `HudFirstPerson`,
  `HudMinimap`, `HudWorldLabels`, `HudToasts`, `Advisor`, `CombatFeedback`. Повідомлення — `StrategyHud.Notify`.
- **Підказки:** `Tooltip.Set(go, text)` / `TipText` (заголовок, приглушене, клавіша, порівняння статів).
- **Рух і звук:** `PopIn` (вікно з'являється), `ButtonFx` (кнопка відгукується; додає `UiTheme.AddButton`). Звук:
  `UiSound` (інтерфейс), `GameSound` (світ і бій), `GameMusic`, `Ambience`.
- **Окремі вікна:** `HeroUI`, `EquipmentUI`, `WorkbenchUI`, `WorldMapUI`, `LobbyUI` (мережеве лобі), `NetPauseUI`
  (пауза для всіх, F6), `DevEditorUI` (редактор тестувальника, F8, лише одиночна гра), `HelpUI` (F1).
- **Вікна носять тему від побудови.** Меню паузи зі сцени й інвентар, зібрані частково без теми, один раз оформлюють
  себе самі (`UiTheme.RestyleTree` на собі); загального проходу по всіх полотнах (`UiRestyler`) більше немає. `UiBoot`
  лише ставить HUD і масштаб інтерфейсу. Нове будувати будівниками `UiTheme`.
- Вікна — `GameWindows` (що відкрите, `BlockGameplay`, закрити все).

## 6. Тести й інструменти

- **EditMode** (`Assets/Tests/EditMode`) — правила й формули; **PlayMode** — поведінка в сцені. Нова система — з тестами
  обох видів.
- **Запуск у batch** (лише один batch Unity одночасно, проєкт закритий у редакторі): `tools\run_tests.ps1` — скорочений прогін, усі
  правила й PlayMode без тестів `[Category("Slow")]`; це звичайний запуск, у тому числі перед випуском. `-Full` — усе, лише на прохання автора; `-Filter X` — лише потрібні.
  Тест, що йде понад 3 секунди, позначаємо `Slow`, крім кількох головних для перевірки гри (збереження, новий світ, сцена без помилок).
- **Інструменти перегляду** (Explicit, PlayMode): `UiCapture`, `MenuCapture`, `ObjectCapture`, `ModelCapture`, `KnightCapture`,
  `RankCapture`, `AnimalCapture`, `GearCapture`, `GraphicsCapture`, `GpuSkinCapture`, `ViewmodelCapture`, `AnimationReview` — знімки в
  `CAPTURE_DIR`; заміри — `PerformanceProbe`, `StressTest`, `MassProbe`, `KingdomLoadProbe`, `GrassProbe`, `BalanceProbe`, `PaceSim`,
  `PushProbe`. Разові інструменти, що вже дали відповідь, видаляємо: їх легко написати знову.
- Редакторські інструменти (`Assets/Editor`): запікання ліплених моделей (`BakeCreatureMeshesTool`), персонажі сцени
  (`RebuildCharacterVisualsTool`), пропи й дерева сцени (`RebuildScenePropsTool`, `RebuildSceneTreesTool`), руки від першої
  особи (`SetupFpsViewmodelTool`), кліпи (`CharacterClipTool`), головне меню, збірки (`BuildGameTool`).

## 6.1 Поселення, зони, абстрактний шар (M5, M6)

- **Склади:** `SettlementStock` на кожну ратушу (`TownHall.Stock`). `ResourceManager` сам віддає склад поселення на екрані (`Viewed`), а робота на місці йде через `For(place)`.
- **Належність:** усе належить до найближчої ратуші (`TownHall.Nearest`), людей рахує `People`.
- **Земля:** `InfiniteWorldManager.HoldSettlements` тримає чанки навколо кожної ратуші, навіть коли герой далеко.
- **Зони:** `ZoneMap` (клітинки 8 м), `ZonePainter` (пензель), `ZoneToolbar` (кнопки мапи), `ZoneGrower` (ставить ділянки), `ConstructionSite` (будують робітники), `WorkPlace` (поле,
  лісопилка), `Caravans` (між поселеннями).
- **Люди:** `SettlementLabor` (хто де працює, межі районів), вікно `NpcGatherCommandUI`, панель будівлі `SelectedBuildingUI`, снос `BuildingDemolition`; накази через `LaborCommand`, `DemolishCommand`.
- **Рух:** `GroundMotor` (рельєф, озера, стіни, `Footprints` будівель, `SolidProps`: стовбури, валуни й руда); `GameBalance.WalkerSeparation`, `StandingSeparation`, `PressBeforeBypass`.
- **Абстрактний шар:** `KingdomRealm` (далекі королівства даними), `KingdomEconomy`, `AbstractBattle` / `UnitStats` (бій цифрами). Після зміни бойового балансу зіграти заново
  `AbstractBattleCalibration.PlayTheBattles`.
- **Імперія:** `NpcKingdom.IsEmpire` у кожній кампанії, `CentralEmpire.Active` лише коли її тримає гравець.

Заміри: `PaceSim` (темп зі справжнього старту; `SIM_ZONES`: що дає кожна зона), `PushProbe`.

## 6.2 Мережа (M8, M9; `Assets/Scripts/Net`)

- **Транспорт:** `NetLink` на Unity Transport (`DefaultPort` 7777): надійний канал (повідомлення шматками по 16 КБ), швидкий
  і `Bulk` — окрема надійна лінія для великого (сейв гостя), шматками по 4 КБ у темпі 12,8 КБ/с, що не затримує надійний.
  Шматки одного повідомлення не перемежовуються з іншими в межах лінії: велике, що має не заважати, — лише в `Bulk`.
- **Сесія:** `NetSession` (partial: `NetPause.cs`) — один на гру, хост або гість. Повідомлення — байт виду (`…Message`, 2–21) і
  текст JSON або байти; обробка після `NetLink.Poll`, зупиняти сесію всередині опитування не можна. Хост перевіряє версію
  (`SameVersion`), видає номер і стоянку (`SiteFor`), тримає сейви гостей (`GuestBook`).
- **Лобі:** `NetLobby` / `LobbyHost` (місця, клас, артефакт, готовність, повернення за токеном), вікно `LobbyUI`.
- **Накази:** `GameCommands` (`CommandBus`, `CommandCodec`, `WorldEntity` — стабільні номери об'єктів).
- **Юніти:** кожен рахує своє (авторитет клієнта, §14.2). `UnitSnapshotWriter` шле змінені юніти 10 разів на секунду,
  `RemoteUnits` показує чужі тіла (`RemoteBody`) з інтерполяцією; удар по чужому тілу летить власникові (`SendHit`),
  убивство повертається нападникові (`SendKill`).
- **Спільний світ:** `Net/SharedWorld.cs` (`LifeClaims` — чиї табори й тварини, `GuestBook` — записи гостей) і `WorldPersistence`: дерева, табори, тварини, зони, час через хоста.
- **Сейв гостя:** `GuestSaveParts` — частинами, лише змінене (`NetSession.GuestSaveInterval`, замір `GuestSaveProbe`).
- **Гравець вийшов:** `GuestBot` (його королівство стереже бот із сейву), пауза й рішення хоста — `NetPause` / `NetPauseUI`.
- **Перевірка двома процесами:** `NetDemo` (`-netdemo sessionhost|sessionjoin -netdemostep …`) у збірці.
- NPC-королівства рахує хост навіть біля гостя (відхилення від §14.2, `GAME_DESIGN.md` §18.4).

## 7. Відомий борг

1. **Стан і показ змішані** в MonoBehaviour юнітів і будівель. Накази з інтерфейсу вже йдуть командами (`CommandBus`), стан
   королівств, армій і героїв — даними (`WorldState`), але юніт поруч і досі живе в компонентах (`GAME_DESIGN.md` §14.3).
   Нові системи (стосунки §22) робити одразу як дані + команди.
2. Меню паузи живе в сцені (`SetupPauseMenuTool`) і дооформлюється під час гри; зібрати його кодом, як решту вікон.
3. Великі класи: `NpcResourceGatherer`, `RtsUnit`, `SaveSystem`, `NpcKingdom` — ділити на частини, як `StrategyHud`.
4. `AIBaseController.IsTerrain` ще впізнає землю за іменем об'єкта.
