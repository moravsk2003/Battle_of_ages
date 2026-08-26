# 📘 Battle of Ages — Довідник Розробника та Інструкція з Розширення (Systems Guide)

> Цей документ містить покрокові інструкції щодо додавання нових механік, розширення існуючих підсистем та дотримання стандартів чистого коду (Clean Code & Architecture).

---

## 1. Як Додати Новий Предмет у Систему Крафту

1. **Додайте значення в Enum**:
   У [`ItemData.cs`](file:///c:/Users/olesm/Battle_of_ages/Assets/Scripts/Crafting/ItemData.cs):
   ```csharp
   public enum ItemType
   {
       Sword,
       Bow,
       Armor,
       Axe,
       Pickaxe,
       Shield // <-- Новий предмет
   }
   ```
2. **Налаштуйте математику розрахунку**:
   У класі `ItemStatCalculator`:
   ```csharp
   public static float CalculateShieldDefense(int level, float baseDefense = 12.0f)
   {
       int lvl = ClampLevel(level);
       return baseDefense * (1.0f + 0.15f * (lvl - 1));
   }
   ```
3. **Додайте рядок у UI верстака**:
   У [`WorkbenchUI.cs`](file:///c:/Users/olesm/Battle_of_ages/Assets/Scripts/UI/WorkbenchUI.cs#L360):
   ```csharp
   CreateRecipeRow(itemsContainer.transform, defaultFont, ItemType.Shield, "🛡️ Щит", "+15% захисту за рівень");
   ```
4. **Покрийте нову формулу Unit-тестом**:
   У `Assets/Tests/EditMode/ItemScalingTests.cs`.

---

## 2. Як Додати Нову Споруду

1. **Зареєструйте тип споруди**:
   У [`BuildingData.cs`](file:///c:/Users/olesm/Battle_of_ages/Assets/Scripts/Building/BuildingData.cs):
   ```csharp
   public enum BuildingType
   {
       WorkerHouse,
       StorageChest,
       Mine,
       Workbench,
       CastleWall,
       GuardTower // <-- Нова споруда
   }
   ```
2. **Додайте визначення (`BuildingDefinition`)**:
   ```csharp
   new BuildingDefinition
   {
       Type = BuildingType.GuardTower,
       DisplayName = "Охоронна Вежа",
       Description = "Автоматично обстрілює наближаючихся ворогів",
       WoodCost = 6,
       OreCost = 4,
       OreTierRequired = 0,
       NpcSpawnCount = 0,
       Size = new Vector3(3.0f, 6.0f, 3.0f),
       MaxHealth = 800
   }
   ```
3. **Створіть префаб/генератор 3D-візуалів**:
   У `BuildingNode.cs`:
   `public static GameObject CreateGuardTowerPrefab(string name = "GuardTower") { ... }`
4. **Оновіть примарне прев'ю та кнопку в меню**:
   У `BuildingNode.CreateGhostPreview()` та `BuildingPlacementSystem.TryConfirmPlacement()`.

---

## 3. Як Створити Новий Стан AI (FSM)

1. **Створіть клас, який реалізує `IAIState`**:
   ```csharp
   public class MobAmbushState : IAIState
   {
       public AIStateType StateType => AIStateType.Idle;

       public void Enter(AIBaseController controller)
       {
           controller.StopMovement();
       }

       public void UpdateState(AIBaseController controller, float deltaTime)
       {
           // Логіка засідки: чекаємо гравця на дистанції < 8м
       }

       public void Exit(AIBaseController controller) { }
   }
   ```
2. **Зареєструйте стан у контролері AI**:
   У `RegisterStates()` контролера:
   ```csharp
   states[AIStateType.Idle] = new MobAmbushState();
   ```

---

## 4. Як Додати Новий Біом у Генератор Світу

1. **Додайте біом у `BiomeType`**:
   У [`InfiniteWorldManager.cs`](file:///c:/Users/olesm/Battle_of_ages/Assets/Scripts/World/InfiniteWorldManager.cs):
   ```csharp
   public enum BiomeType
   {
       Steppe,
       PineForest,
       Desert,
       SnowyPeaks,
       Swamp // <-- Новий біом
   }
   ```
2. **Налаштуйте правила вибірки біому в `GetBiomeAtWorldPos()`**:
   Використовуйте Perlin Noise для температури та вологості.
3. **Налаштуйте матеріали та щільність декорацій** у `ProceduralBiomeChunk.ScatterBiomeResources()`.

---

## 5. Стандарти Коду та Перевірка Якості

- **Zero-GC Allocations**: Заборонено створювати `new` масиви або списки у методах `Update()`, `FixedUpdate()`, `LateUpdate()`. Використовуйте `NonAlloc` методи Unity Physics або пулінг об'єктів.
- **SerializeField & Encapsulation**: Усі поля мають бути `private [SerializeField]`, відкритий доступ — лише через ReadOnly властивості (`public float MoveSpeed => moveSpeed;`).
- **Unit Testing**: Будь-яка нова формула, черга чи калькулятор обов'язково супроводжується NUnit-тестами в `Assets/Tests/EditMode/`.
