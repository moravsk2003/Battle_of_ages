# Battle of Ages — довідник розробника

> Як додати річ, споруду, модель, роль персонажа, наказ чи мережеве повідомлення так, щоб нічого не загубилось.
> Що гра робить — [GAME_DESIGN.md](GAME_DESIGN.md), карта коду — [ARCHITECTURE.md](ARCHITECTURE.md). Оновлено 2026-11-15.
> Тут лише точки входу: деталі — у коментарях `///` самих класів.

## 0. Загальні правила

- **Числа балансу** — лише в `Combat/GameBalance` (або в таблицях `UnitRanks`, `ItemStatCalculator`), з посиланням на
  розділ `GAME_DESIGN.md`.
- **Нові значення переліків — у кінець.** `ItemType`, `BuildingType`, `ZoneType`, `ArtifactType`, `CommandCodec.Types`
  і мережеві номери лежать у сейвах і на дроті номером: вставка в середину ламає старі збереження й гравців іншої збірки.
- **Дія гравця — наказом** (`GameCommand`, §4), а не прямим викликом з інтерфейсу: так вона працює й у мережі.
- **Без пошуку по сцені щокадру:** `GameContext`, реєстри (`UnitRegistry`, `BuildingNode.Active`, `ResourceGrid` …).
- **Кожна механіка — з тестом** (EditMode для правил, PlayMode для поведінки), візуал — зі знімком (§6).

## 1. Нова річ (зброя, броня, інструмент)

1. `Crafting/ItemData.cs`: значення в кінець `ItemType`; у `ItemTypeNames` — `Display`, `SlotOf` (який із 6 слотів
   `EquipSlot`), `MaterialName`, `Describe`, і в `All`, якщо її кують на верстаті (`WorkbenchUI` будує картки з `All`).
2. `ItemStatCalculator` (той самий файл): що вона дає (`WeaponDamageMultiplier`, `CooldownMultiplier`,
   `DamageReduction`, `HealthBonus`) і ціна (`CalculateWoodCost`, `CalculateOreCost`, `CalculateRequiredOreTier`).
3. Вигляд на персонажі — `Utils/GearMeshBuilder` (злиття в один меш тіла); від першої особи —
   `ViewmodelArmsBuilder.BuildWeapon` / `BuildShield`, підставляє `Player/FpsViewmodelGear`.
   Лицар, генерал і герой ліплені: спорядження в них змінює лише числа (`CharacterGear.Sculpted`).
4. Тести: `ItemScalingTests`, `CraftedItemInteractionTests` (EditMode); знімок — `GearCapture`.

## 2. Нова споруда

1. `Building/BuildingData.cs`: значення в кінець `BuildingType`, `BuildingDefinition` (назва, опис, ціна, розмір
   `Size` — площа під забудову, здоров'я).
2. Об'єкт: статичний `Create…Prefab(string name)` (як `WorkbenchNode.CreateWorkbenchPrefab`): корінь, компонент,
   `BuildingNode`, модель, колайдер. Підключити в `BuildingPlacementSystem` (перемикач за типом, ~рядок 500) і в
   `BuildingNode.CreateGhostPreview` (примара).
3. Модель — будівник на `MeshKit` (§3). Якщо споруда стоїть у `SampleScene` наперед — додати в список
   `Editor/RebuildScenePropsTool` і перебудувати сцену.
4. Розмістити з інтерфейсу — `PlaceBuildingCommand` (уже загальний для всіх типів). Робота в споруді — `WorkPlace`,
   зона — `ZoneGrower` / `ZoneWork`.

## 3. Модель у коді (пропи, будівлі)

- Будівник — статичний клас у `Utils` (`WorkbenchBuilder`, `MineBuilder`, `WorkerHouseBuilder`, `SteppeTreeBuilder` …):
  `MeshKit` (Box із фаскою, Lathe, Grid, Dome) з кольором і поверхнею на вершині, один меш на всіх і легкий далекий
  меш, `MeshKit.AttachWithLod(root, name, near, far, nearScreenHeight)`.
- Поверхня — `Vector3(pattern, metallic, smoothness)`; у шейдері персонажів `Custom/CharacterSurface` візерунки:
  0 шкіра, 1 тканина, 2 шкіра-матеріал, 3 метал, **4 волосся** (не дерево), 5 очі, 6 хутро, 7 кольчуга, 8 камінь.
- Блискучі пласкі поверхні (лезо, вода) робити матовими: сонце в них дає засліплювальний відблиск із bloom.
- Перевірка — `ObjectCapture` (`CAPTURE_FOCUS=workbench|house|chest|mine|wall|dummy|camp` — 14 ракурсів, `CAPTURE_LIST`).
- Зміна `MeshKit` чи `SdfCreature` міняє хеш рук від першої особи: перезапустити
  `SetupFpsViewmodelTool.ConfigureInSampleScene`.

## 4. Наказ гравця (команда)

1. `Net/GameCommands.cs`: `[Serializable] sealed class …Command : GameCommand` з `Execute()`; поля — дані, об'єкти —
   номером (`WorldEntity`), бо об'єкт може зникнути, поки наказ іде.
2. Тип — **у кінець** `CommandCodec.Types`.
3. Інтерфейс викликає `CommandBus.Submit(new …Command { … })`.
4. Тест — `GameCommandTests` (кодек туди й назад і виконання).

## 5. Персонаж, роль, анімація

- Ролі — `HumanoidModelBuilder.HumanType`; модель будує `RiggedCharacterBuilder.TryCreate` на FBX-ригу
  (`CharacterModelLibrary`): колір ролі (`LookFor`), спорядження злите в меш (`CharacterGear`, `GearMeshBuilder`).
- **Ліплені ролі** (`RiggedCharacterBuilder.IsSculpted`: лицар, генерал, герой) будує `KnightModelBuilder` за стилем
  (`KnightStyle`, `GeneralStyle`, `HeroStyle`: сюрко, обробка, щит, плащ, плюмаж, корона). Після змін:
  `BakeCreatureMeshesTool.Bake` (меші в `Resources/Creatures`, список `index.txt`), потім
  `RebuildCharacterVisualsTool.Rebuild` (герой і персонажі сцени). Лук у руках — інша модель (`RankModel`).
- Кліпи — `Editor/CharacterClipTool` (`Assets/Characters/*Clips`), огляд — `AnimationReview` (`CAPTURE_ROLES`,
  `CAPTURE_CLIPS`). Будь-який знімок кліпів чи моделей зблизька вимикає `GpuSkinnedCharacter.Enabled`: GPU-шлях малює
  далеку модель зі своєю камерою.
- Руки від першої особи: модель — `ViewmodelArmsBuilder`, рух — `FpsViewmodelAnimation` (ключі ударів, крок, інерція),
  огляд ударів — `ViewmodelCapture`.
- Нова роль у мережі: як її показати чужим — `UnitSnapshots` (`DressBody`).

## 6. Тести, знімки, заміри

- `tools\run_tests.ps1` — короткий прогін (звичайний, і перед випуском); `-Full` — лише на прохання автора;
  `-Filter Клас` — точною назвою класу (широкий фільтр зачіпає Explicit-заміри на десятки хвилин).
- Тест понад 3 с — `[Category("Slow")]`. PlayMode-тести йдуть одне за одним у тому самому світі: ставити об'єкти на
  `RtsUnit.GetTerrainHeight`, після створення й пересування колайдерів — `Physics.SyncTransforms()`.
- Знімки (`[Explicit]`, `CAPTURE_DIR`): `ObjectCapture`, `ModelCapture`, `KnightCapture` (`CAPTURE_ROLE`), `GearCapture`,
  `RankCapture`, `AnimalCapture`, `ViewmodelCapture`, `AnimationReview`, `UiCapture`, `MenuCapture`, `GraphicsCapture`,
  `GpuSkinCapture`. Змінні середовища `CAPTURE_*` чистити між запусками.
- Заміри: `PerformanceProbe`, `StressTest`, `MassProbe`, `KingdomLoadProbe`, `PaceSim`, `PushProbe`, `BalanceProbe`.
  Порівнювати з базою того самого дня, на вільній відеокарті.

## 7. Мережа

- Нове повідомлення: номер у кінець констант `NetSession` (`…Message`, зараз до 21), відправка `Send` / `BroadcastText`,
  обробка в перемикачі прийому. Велике (десятки КБ і більше) — каналом `NetChannel.Bulk`, щоб не тримати удари й накази;
  ще краще — слати лише змінене, як `GuestSaveParts`. Що міняє стан гри — через хоста. Не зупиняти сесію всередині `NetLink.Poll`
  (відкласти на після опитування).
- Перевірка двома процесами — у збірці (`BUILD_DIR` у тимчасову теку): `-netdemo sessionhost|sessionjoin
  -netdemoport <не 7777> -netdemostep fight|leave|back|knights|duel|lobby` (`Net/NetDemo`).
- Формат удару, знімка чи повідомлення змінився — гравці різних збірок не зіграють: підняти версію гри, хост відмовляє гравцеві іншої версії (`NetSession.SameVersion`).

## 8. Випуск

- Збірка: `BuildGameTool.BuildWindows` (IL2CPP, `BUILD_DIR` або `Builds/Windows`); версія — у налаштуваннях гравця.
- Перед збіркою — короткий прогін тестів; після — запуск exe й перевірка журналу на винятки.
- Зміни описати в `GAME_DESIGN.md` §18 (журнал версій), виправлене прибрати з `KNOWN_ISSUES.md`.
