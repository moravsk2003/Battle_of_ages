#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using BattleOfAges.Managers;
using BattleOfAges.UI;

namespace BattleOfAges.EditorTools
{
    public class SetupPauseMenuTool
    {
        [MenuItem("Battle of Ages/Auto Setup Pause Menu & Settings")]
        public static void SetupPauseMenu()
        {
            // 1. Ensure SettingsManager in scene
            GameObject settingsMgrObj = GameObject.Find("SettingsManager");
            if (settingsMgrObj == null)
            {
                settingsMgrObj = new GameObject("SettingsManager");
                settingsMgrObj.AddComponent<SettingsManager>();
                Undo.RegisterCreatedObjectUndo(settingsMgrObj, "Create SettingsManager");
            }

            // 2. Ensure EventSystem with InputSystemUIInputModule
            EventSystem eventSys = Object.FindFirstObjectByType<EventSystem>();
            if (eventSys == null)
            {
                GameObject eventSysObj = new GameObject("EventSystem");
                eventSys = eventSysObj.AddComponent<EventSystem>();
                Undo.RegisterCreatedObjectUndo(eventSysObj, "Create EventSystem");
            }

            // Add InputSystemUIInputModule or StandaloneInputModule
#if ENABLE_INPUT_SYSTEM
            InputSystemUIInputModule inputModule = eventSys.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null)
            {
                StandaloneInputModule oldModule = eventSys.GetComponent<StandaloneInputModule>();
                if (oldModule != null) Object.DestroyImmediate(oldModule);
                eventSys.gameObject.AddComponent<InputSystemUIInputModule>();
            }
#else
            if (eventSys.GetComponent<StandaloneInputModule>() == null)
            {
                eventSys.gameObject.AddComponent<StandaloneInputModule>();
            }
#endif

            // 3. Ensure UI Canvas
            GameObject canvasObj = GameObject.Find("PauseCanvas");
            if (canvasObj == null)
            {
                canvasObj = new GameObject("PauseCanvas");
                Canvas canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;
                
                CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);

                canvasObj.AddComponent<GraphicRaycaster>();
                Undo.RegisterCreatedObjectUndo(canvasObj, "Create PauseCanvas");
            }
            canvasObj.SetActive(true);

            // 4. Create PauseMenuController
            PauseMenuController pauseController = canvasObj.GetComponent<PauseMenuController>();
            if (pauseController == null)
            {
                pauseController = canvasObj.AddComponent<PauseMenuController>();
            }

            // 5. Create Pause Panel
            Transform pausePanelTr = canvasObj.transform.Find("PauseMenuPanel");
            GameObject pausePanelObj;
            if (pausePanelTr == null)
            {
                pausePanelObj = CreatePanel(canvasObj.transform, "PauseMenuPanel", new Color(0.05f, 0.05f, 0.1f, 0.88f));
            }
            else
            {
                pausePanelObj = pausePanelTr.gameObject;
            }

            // 6. Create Settings Panel
            Transform settingsPanelTr = canvasObj.transform.Find("SettingsPanel");
            GameObject settingsPanelObj;
            if (settingsPanelTr == null)
            {
                settingsPanelObj = CreatePanel(canvasObj.transform, "SettingsPanel", new Color(0.08f, 0.08f, 0.14f, 0.95f));
            }
            else
            {
                settingsPanelObj = settingsPanelTr.gameObject;
            }

            // Clear old children to rebuild UI cleanly
            foreach (Transform child in pausePanelObj.transform) Object.DestroyImmediate(child.gameObject);
            foreach (Transform child in settingsPanelObj.transform) Object.DestroyImmediate(child.gameObject);

            // Populate Pause Panel UI
            Text titleText = CreateText(pausePanelObj.transform, "Title", "ПАУЗА", 42, new Vector2(0, 150));
            Text tipText = CreateText(pausePanelObj.transform, "Tip", "(Натисніть ESC або P для згортання)", 16, new Vector2(0, 100));
            tipText.color = new Color(0.7f, 0.7f, 0.7f, 1f);

            Button resumeBtn = CreateButton(pausePanelObj.transform, "ResumeButton", "Відновити гру", new Vector2(0, 30));
            Button settingsBtn = CreateButton(pausePanelObj.transform, "SettingsButton", "Налаштування", new Vector2(0, -40));
            Button quitBtn = CreateButton(pausePanelObj.transform, "QuitButton", "Вийти з гри", new Vector2(0, -110));

            // Populate Settings Panel UI
            Text settingsTitle = CreateText(settingsPanelObj.transform, "SettingsTitle", "НАЛАШТУВАННЯ", 36, new Vector2(0, 180));
            
            Text fpsLabel = CreateText(settingsPanelObj.transform, "FpsLabel", "Чутливість миші (1-ша особа):", 20, new Vector2(0, 90));
            Slider fpsSlider = CreateSlider(settingsPanelObj.transform, "FpsSensSlider", new Vector2(0, 55));

            Text mapLabel = CreateText(settingsPanelObj.transform, "MapLabel", "Чутливість / швидкість (2D Карта):", 20, new Vector2(0, -10));
            Slider mapSlider = CreateSlider(settingsPanelObj.transform, "MapSensSlider", new Vector2(0, -45));

            Text volLabel = CreateText(settingsPanelObj.transform, "VolLabel", "Загальна гучність:", 20, new Vector2(0, -110));
            Slider volSlider = CreateSlider(settingsPanelObj.transform, "VolSlider", new Vector2(0, -145));

            Button backBtn = CreateButton(settingsPanelObj.transform, "BackButton", "Назад", new Vector2(0, -220));

            // Assign Serialized references in PauseMenuController
            SerializedObject serController = new SerializedObject(pauseController);
            serController.FindProperty("pauseMenuPanel").objectReferenceValue = pausePanelObj;
            serController.FindProperty("settingsPanel").objectReferenceValue = settingsPanelObj;
            serController.FindProperty("fpsSensSlider").objectReferenceValue = fpsSlider;
            serController.FindProperty("mapSensSlider").objectReferenceValue = mapSlider;
            serController.FindProperty("volumeSlider").objectReferenceValue = volSlider;
            serController.FindProperty("resumeButton").objectReferenceValue = resumeBtn;
            serController.FindProperty("settingsButton").objectReferenceValue = settingsBtn;
            serController.FindProperty("closeSettingsButton").objectReferenceValue = backBtn;
            serController.FindProperty("quitButton").objectReferenceValue = quitBtn;
            serController.ApplyModifiedProperties();

            pausePanelObj.SetActive(false);
            settingsPanelObj.SetActive(false);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

            Debug.Log("[Battle of Ages] Pause Menu & Settings Canvas successfully configured and saved!");
        }

        private static GameObject CreatePanel(Transform parent, string name, Color color)
        {
            GameObject panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image img = panel.AddComponent<Image>();
            img.color = color;

            return panel;
        }

        private static Text CreateText(Transform parent, string name, string content, int fontSize, Vector2 anchoredPos)
        {
            GameObject textObj = new GameObject(name);
            textObj.transform.SetParent(parent, false);

            RectTransform rect = textObj.AddComponent<RectTransform>();
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = new Vector2(500, 50);

            Text txt = textObj.AddComponent<Text>();
            txt.text = content;
            txt.fontSize = fontSize;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            return txt;
        }

        private static Button CreateButton(Transform parent, string name, string labelText, Vector2 anchoredPos)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);

            RectTransform rect = btnObj.AddComponent<RectTransform>();
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = new Vector2(280, 50);

            Image img = btnObj.AddComponent<Image>();
            img.color = new Color(0.2f, 0.4f, 0.7f, 1f);

            Button btn = btnObj.AddComponent<Button>();

            ColorBlock colors = btn.colors;
            colors.highlightedColor = new Color(0.35f, 0.55f, 0.9f, 1f);
            colors.pressedColor = new Color(0.15f, 0.25f, 0.5f, 1f);
            btn.colors = colors;

            GameObject txtObj = new GameObject("Text");
            txtObj.transform.SetParent(btnObj.transform, false);

            RectTransform txtRect = txtObj.AddComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = Vector2.zero;
            txtRect.offsetMax = Vector2.zero;

            Text txt = txtObj.AddComponent<Text>();
            txt.text = labelText;
            txt.fontSize = 20;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            return btn;
        }

        private static Slider CreateSlider(Transform parent, string name, Vector2 anchoredPos)
        {
            GameObject sliderObj = new GameObject(name);
            sliderObj.transform.SetParent(parent, false);

            RectTransform rect = sliderObj.AddComponent<RectTransform>();
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = new Vector2(350, 30);

            Slider slider = sliderObj.AddComponent<Slider>();

            // Background
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(sliderObj.transform, false);
            RectTransform bgRect = bgObj.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.25f);
            bgRect.anchorMax = new Vector2(1, 0.75f);
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImg = bgObj.AddComponent<Image>();
            bgImg.color = new Color(0.2f, 0.2f, 0.25f, 1f);

            // Fill Area
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderObj.transform, false);
            RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1, 0.75f);
            fillAreaRect.offsetMin = Vector2.zero;
            fillAreaRect.offsetMax = Vector2.zero;

            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(fillArea.transform, false);
            RectTransform fillRect = fillObj.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            Image fillImg = fillObj.AddComponent<Image>();
            fillImg.color = new Color(0.3f, 0.65f, 1f, 1f);

            // Handle Area
            GameObject handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(sliderObj.transform, false);
            RectTransform handleAreaRect = handleArea.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = Vector2.zero;
            handleAreaRect.offsetMax = Vector2.zero;

            GameObject handleObj = new GameObject("Handle");
            handleObj.transform.SetParent(handleArea.transform, false);
            RectTransform handleRect = handleObj.AddComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(24, 34);
            Image handleImg = handleObj.AddComponent<Image>();
            handleImg.color = Color.white;

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImg;

            return slider;
        }
    }
}
#endif
