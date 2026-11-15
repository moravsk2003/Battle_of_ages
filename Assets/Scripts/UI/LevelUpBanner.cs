using TMPro;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.InputSystem;

namespace BattleOfAges.UI
{
    /// <summary>
    /// When the player's hero goes up a level: a plate in the top middle of the screen says so, what the level gave by
    /// itself (GameBalance.HeroHealthPerLevel, HeroStrengthPerLevel) and which key opens the hero window to spend the new
    /// points. It stays till that window is opened, or for ShownSeconds.
    /// </summary>
    public sealed class LevelUpBanner : MonoBehaviour
    {
        public const float ShownSeconds = 20f;

        private static LevelUpBanner instance;
        private RectTransform plate;
        private TMP_Text title, line;
        private float until;

        /// <summary>The banner is up (tests).</summary>
        public static bool Shown => instance != null && instance.plate != null && instance.plate.gameObject.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance == null) instance = new GameObject("LevelUpBanner").AddComponent<LevelUpBanner>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Canvas canvas = UiTheme.CreateCanvas("LevelUpCanvas", 120);
            canvas.transform.SetParent(transform, false);
            plate = UiTheme.Rect(canvas.transform, "LevelUp", new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(560f, 92f));
            UiTheme.PanelImage(plate);
            title = UiTheme.Label(UiTheme.Rect(plate, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(520f, 36f)), "Text", "", 26, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            line = UiTheme.Label(UiTheme.Rect(plate, "Line", new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(520f, 40f)), "Text", "", 17, TextAnchor.MiddleCenter, UiTheme.Ink);
            LocText.Detach(title);
            LocText.Detach(line);
            plate.gameObject.SetActive(false);
            HeroProgress.LeveledUp += OnLeveledUp;
        }

        private void OnDestroy()
        {
            HeroProgress.LeveledUp -= OnLeveledUp;
            if (instance == this) instance = null;
        }

        private void OnLeveledUp(HeroProgress hero)
        {
            if (hero == null || hero.gameObject != Managers.GameContext.Player) return; // the player's own hero only
            string key = KeybindsManager.GetKeyDisplayString(KeybindsManager.GetKey(KeyAction.Hero));
            title.text = Loc.Pick($"Новий рівень — {hero.Level}!", $"Level {hero.Level}!");
            line.text = Loc.Pick(
                $"+{GameBalance.HeroHealthPerLevel} HP, +{GameBalance.HeroStrengthPerLevel} сили. Натисніть <color=#F5CC73>[{key}]</color>, щоб розподілити очки",
                $"+{GameBalance.HeroHealthPerLevel} HP, +{GameBalance.HeroStrengthPerLevel} strength. Press <color=#F5CC73>[{key}]</color> to spend your points");
            plate.gameObject.SetActive(true);
            PopIn.On(plate);
            until = Time.unscaledTime + ShownSeconds;
        }

        private void Update()
        {
            if (plate == null || !plate.gameObject.activeSelf) return;
            if (GameWindows.HeroOpen || Time.unscaledTime >= until) plate.gameObject.SetActive(false);
        }
    }
}
