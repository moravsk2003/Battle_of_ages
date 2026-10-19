using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using BattleOfAges.Combat;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The hero's death on screen (docs/GAME_DESIGN.md §7.2): the interregnum countdown, then either a note about the
    /// new hero or the end of the game (single player: load a save or go to the main menu).
    /// </summary>
    public sealed class HeroDeathUI : MonoBehaviour
    {
        private static HeroDeathUI instance;
        private GameObject banner, gameOver;
        private Text bannerText;
        private HeroSuccession watched;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance == null) instance = new GameObject("HeroDeathUI").AddComponent<HeroDeathUI>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            Build();
            DontDestroyOnLoad(gameObject);
            HeroSuccession.HeroDied += OnDied;
            HeroSuccession.NewHero += OnNewHero;
            HeroSuccession.GameOver += OnGameOver;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            HeroSuccession.HeroDied -= OnDied;
            HeroSuccession.NewHero -= OnNewHero;
            HeroSuccession.GameOver -= OnGameOver;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene s, LoadSceneMode m) { banner.SetActive(false); gameOver.SetActive(false); watched = null; }

        private void OnDied(HeroSuccession s, Vector3 where) { watched = s; banner.SetActive(true); }

        private void OnNewHero(HeroSuccession s)
        {
            banner.SetActive(false);
            StrategyHud.Notify("Новий герой узяв владу. Оберіть клас (H)");
        }

        private void OnGameOver(HeroSuccession s)
        {
            banner.SetActive(false);
            gameOver.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            if (watched != null && banner.activeSelf)
                bannerText.text = $"Герой загинув · міжцарів'я: {watched.InterregnumLeft:0} с\nЧастина війська розбіглася";
        }

        private void Build()
        {
            Canvas canvas = UiTheme.CreateCanvas("HeroDeathCanvas", 130);
            canvas.transform.SetParent(transform, false);

            RectTransform b = UiTheme.Rect(canvas.transform, "Interregnum", new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(620f, 84f));
            UiTheme.PanelImage(b);
            bannerText = UiTheme.Label(b, "Text", "", 22, TextAnchor.MiddleCenter, UiTheme.Bad, FontStyle.Bold);
            banner = b.gameObject;
            banner.SetActive(false);

            RectTransform dim = UiTheme.Child(canvas.transform, "GameOver", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
            gameOver = dim.gameObject;
            RectTransform panel = UiTheme.Rect(dim, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 260f));
            UiTheme.PanelImage(panel);
            RectTransform t = UiTheme.Rect(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(520f, 50f));
            UiTheme.Label(t, "Text", "Кінець гри", 34, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            RectTransform m = UiTheme.Rect(panel, "Message", new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(520f, 60f));
            UiTheme.Label(m, "Text", "Герой загинув, і нікому взяти владу.", 20, TextAnchor.MiddleCenter, UiTheme.Ink);
            UiTheme.TextButton(panel, "Load", "Завантажити", new Vector2(0.5f, 0f), new Vector2(-130f, 28f), new Vector2(230f, 54f),
                () => { gameOver.SetActive(false); SaveSlotsUI.Open(false, () => { if (watched != null && watched.IsGameOver) gameOver.SetActive(true); }); }, 22);
            UiTheme.TextButton(panel, "Menu", "Головне меню", new Vector2(0.5f, 0f), new Vector2(130f, 28f), new Vector2(230f, 54f),
                () => { gameOver.SetActive(false); SceneManager.LoadScene(SaveSystem.MenuScene); }, 22);
            gameOver.SetActive(false);
        }
    }
}
