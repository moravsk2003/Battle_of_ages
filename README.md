# ⚔️ Battle of Ages

> **Battle of Ages** — гібридна гра відкритого світу (Open-World Colony Sim / First-Person Action RPG / RTS Strategy), розроблена на Unity 6 (URP) за сучасними інженерними стандартами AAA-рівня.

[![Unity](https://img.shields.io/badge/Unity-6000.3.6f1-black?logo=unity)](https://unity.com/)
[![Render Pipeline](https://img.shields.io/badge/Render%20Pipeline-URP%2017.3.0-blue)](https://unity.com/srp/universal-render-pipeline)
[![Language](https://img.shields.io/badge/C%23-9.0%20%2F%20.NET%20Standard%202.1-purple)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![Architecture](https://img.shields.io/badge/Architecture-Modular%20Domain--Driven-success)]()

---

## 🌟 Ключові Особливості Гри

1. **Безшовне перемикання режимів (FPS ⟷ RTS)**:
   - Досліджуйте світ від першої особи (рубка дерев, збір руди, ближній бій).
   - Натисніть **`M`** для переходу на висотну тактичну карту RTS для керування селянами та будівництва.
2. **Процедурний відкритий світ та Чанк-стрімінг**:
   - 4 біоми: Степ, Хвойний Ліс, Пустеля, Сніжні Піки.
   - Zero-GC **Environment Object Pooling** та дворівневий **LOD** (Level of Detail).
3. **Модульний Штучний Інтелект (FSM) та AI LOD**:
   - Мирні тварини (олені, кабани, зайці) з випасом та втечею від загроз.
   - Ворожі моби (розбійники, гобліни) з агро-радіусом, тактиками бою та відступом при пораненні.
   - Ворожі поселення з ватажком, який координує спільні штурми та відступи загону.
   - Троттлінг оновлення AI (від 20 Гц поблизу до 0.5 Гц здалеку).
4. **Верстак та Система Крафту (Рівні 1–5)**:
   - Споруда верстака з підтримкою одночасної роботи **до 4 робітників** (прискорення до 3.0x).
   - 5 базових предметів: Меч (+20% урону/рівень), Броня (+10% захисту/рівень), Лук, Сокира, Кайло.
   - Повноцінний UI черги крафту та призначення робітників.
5. **Система Будівництва та Оборони**:
   - Будинок робітника, Склад ресурсів, Шахта руди, Верстак, Замкові оборонні стіни з 600 HP.
6. **100% Покриття Бізнес-Логіки EditMode Unit-тестами**:
   - Набір тестів Unity Test Framework (NUnit) для математики предметів, черг крафту та бойової системи.

---

## 🗺️ Керування в Грі

| Клавіша | Дія (FPS Режим) | Дія (RTS Режим) |
|---|---|---|
| **`W, A, S, D`** | Рух гравця | Переміщення тактичної камери |
| **`M`** | Перемикання в RTS Режим | Перемикання в FPS Режим |
| **`ЛКМ` (Left Click)** | Атака кулаком/зброєю | Виділення робітників (рамкою або кліком) |
| **`ПКМ` (Right Click)** | — | Наказ руху, видобутку, крафту або атаки ворога |
| **`E` / `F`** | Збір гілок / Рубка дерева / Підбір руди / Вхід у верстак | — |
| **`I`** | Відкрити/закрити Інвентар | Відкрити/закрити Інвентар |
| **`B`** | — | Відкрити меню Будівництва |
| **`Esc`** | Меню паузи / Закрити відкритий UI | Меню паузи / Скасувати будівництво |

---

## 📂 Структура Проекту

```
Battle_of_ages/
├── Assets/
│   ├── Scripts/
│   │   ├── AI/             # FSM контролери (PeacefulAnimalAI, HostileMobAI, HostileSettlementLeader)
│   │   ├── Building/       # Споруди, дані будівництва, розміщення (BuildingNode, MineNode)
│   │   ├── Camera/         # Контролер тактичної RTS камери
│   │   ├── Combat/         # HealthComponent, CombatUnit, DummyTargetNode
│   │   ├── Crafting/       # Верстак, черга крафту, ItemStatCalculator (ItemData, CraftingQueue)
│   │   ├── Input/          # UniversalInput, KeybindsManager
│   │   ├── Managers/       # GameModeManager, SettingsManager
│   │   ├── Player/         # FirstPersonController, PlayerCombatController, PlayerResourceHarvester
│   │   ├── RTS/            # RtsUnit, RtsUnitCommander, NpcResourceGatherer, WorkerRegistry
│   │   ├── Resources/      # ResourceManager, StorageChestNode, OreTier, ResourceNodeTree
│   │   ├── UI/             # InventoryUI, BuildMenuUI, WorkbenchUI, ResourceUI
│   │   ├── Utils/          # ProceduralVisualMaterials, SteppeTreeBuilder, HumanoidModelBuilder
│   │   ├── World/          # InfiniteWorldManager, ProceduralBiomeChunk, EnvironmentPoolManager
│   │   └── BattleOfAges.Runtime.asmdef
│   ├── Editor/             # Інструменти редактора та BattleOfAges.Editor.asmdef
│   ├── Tests/
│   │   └── EditMode/       # Unit-тести (ItemScalingTests, CraftingQueueTests, CombatDamageMathTests)
│   ├── Scenes/             # SampleScene.unity
│   └── Models/             # FBX моделі персонажів
├── docs/
│   ├── GAME_DESIGN.md      # Дизайн гри: бачення, рішення, етапи (джерело правди)
│   ├── ARCHITECTURE.md     # Повний архітектурний опис та Mermaid діаграми
│   └── SYSTEMS_GUIDE.md    # Довідник підсистем та розширення коду
└── README.md
```

---

## 🚀 Як Запустити та Тестувати Проект

### 1. Відкриття в Unity
- Відкрийте **Unity Hub**.
- Додайте проект та запустіть під версією **Unity 6 (6000.3.6f1)** або сумісною.
- Відкрийте сцену: `Assets/Scenes/SampleScene.unity`.
- Натисніть **Play** ▶️.

### 2. Запуск Unit-Тестів (EditMode)
1. У головному меню Unity оберіть:  
   **Window ➔ General ➔ Test Runner**
2. У вкладці **EditMode** натисніть кнопку **Run All**.
3. Усі тести (`ItemScalingTests`, `CraftingQueueTests`, `CombatDamageMathTests`) мають пройти з зеленими відмітками ✅.

---

## 📖 Документація

- [Дизайн гри (GAME_DESIGN.md)](docs/GAME_DESIGN.md)
- [Архитектурний документ (ARCHITECTURE.md)](docs/ARCHITECTURE.md)
- [Гайд по розширенню систем (SYSTEMS_GUIDE.md)](docs/SYSTEMS_GUIDE.md)
