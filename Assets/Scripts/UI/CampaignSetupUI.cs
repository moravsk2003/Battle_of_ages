using System;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Combat;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The new campaign screen of the main menu (docs/GAME_DESIGN.md §18.2, M1 step 6): the world on the left (its seed —
    /// typed to share a world, or random — its size, how many heroes and kings), the player's hero on the right (its class
    /// and starting artifact, both chosen here, with what they do), and the advisor's tips.
    /// </summary>
    public sealed class CampaignSetupUI
    {
        private readonly CampaignSetup setup = new CampaignSetup();
        private InputField seedField;
        private Text summary, classText, classDescription, artifactText, artifactDescription;
        private int sizeIndex = Array.IndexOf(CampaignSetup.Sizes, CampaignSetup.FirstSize);
        private int heroesIndex = Array.IndexOf(CampaignSetup.HeroCounts, 16), shareIndex = 1;
        private int classIndex, artifactIndex;
        private bool tips = !Advisor.Hidden;

        public RectTransform Panel { get; private set; }

        private static readonly HeroClass[] Classes = { HeroClass.Tank, HeroClass.Archer, HeroClass.Mage, HeroClass.Dd };

        public static CampaignSetupUI Build(RectTransform root, Action back)
        {
            var ui = new CampaignSetupUI();
            ui.Make(root, back);
            return ui;
        }

        private void Make(RectTransform root, Action back)
        {
            Panel = UiTheme.Rect(root, "NewCampaign", new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(1180f, 650f));
            UiTheme.PanelImage(Panel);
            PopIn.On(Panel);
            RectTransform title = UiTheme.Rect(Panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(600f, 44f));
            UiTheme.Label(title, "Text", "Нова кампанія", 30, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            UiTheme.Divider(Panel, -66f, 560f);

            // The world
            Heading(-92f, -290f, "Світ");
            Label(-150f, -440f, "Сид світу");
            seedField = SeedField(new Vector2(-250f, -150f));
            UiTheme.TextButton(Panel, "RandomSeed", "Випадковий", new Vector2(0.5f, 1f), new Vector2(-80f, -150f), new Vector2(150f, 40f), () => seedField.text = "", 16);
            Tooltip.Set(seedField.gameObject, () => TipText.Title("Сид світу", UiTheme.Gold) + "\nЧисло, з якого виростає світ: рельєф, ліси, озера, стоянки. Той самий сид — той самий світ (можна поділитися)."
                                                       + "\n" + TipText.Dim("Порожньо — випадковий."));
            Cycler(-210f, -290f, "Розмір світу", () => $"{CampaignSetup.Sizes[sizeIndex] / 1000f:0} × {CampaignSetup.Sizes[sizeIndex] / 1000f:0} км", d => sizeIndex = Wrap(sizeIndex + d, CampaignSetup.Sizes.Length));
            Cycler(-270f, -290f, "Героїв у світі", () => CampaignSetup.HeroCounts[heroesIndex].ToString(), d => heroesIndex = Wrap(heroesIndex + d, CampaignSetup.HeroCounts.Length));
            Cycler(-330f, -290f, "Королів серед них", () => $"{CampaignSetup.KingShares[shareIndex] * 100f:0}%", d => shareIndex = Wrap(shareIndex + d, CampaignSetup.KingShares.Length));
            Cycler(-390f, -290f, "Поради радника", () => tips ? "так" : "ні", d => tips = !tips);
            RectTransform sum = UiTheme.Rect(Panel, "Summary", new Vector2(0.5f, 1f), new Vector2(-290f, -470f), new Vector2(520f, 90f));
            summary = UiTheme.Label(sum, "Text", "", 16, TextAnchor.UpperLeft, UiTheme.InkDim);
            summary.horizontalOverflow = HorizontalWrapMode.Wrap;

            // The hero
            Heading(-92f, 290f, "Ваш герой");
            Cycler(-150f, 290f, "Клас", () => HeroClassRules.Name(Classes[classIndex]), d => classIndex = Wrap(classIndex + d, Classes.Length), out classText);
            classDescription = Description(-195f, 290f);
            Cycler(-330f, 290f, "Стартовий артефакт", () => Artifacts.Name(Artifacts.All[artifactIndex], Classes[classIndex]), d => artifactIndex = Wrap(artifactIndex + d, Artifacts.All.Length), out artifactText);
            artifactDescription = Description(-375f, 290f);

            UiTheme.TextButton(Panel, "Start", "Почати", new Vector2(0.5f, 0f), new Vector2(130f, 26f), new Vector2(230f, 56f), Start, 22)
                   .GetComponent<Image>().color = new Color(1f, 0.92f, 0.7f);
            UiTheme.TextButton(Panel, "Back", "Назад", new Vector2(0.5f, 0f), new Vector2(-130f, 26f), new Vector2(230f, 56f), () => back(), 20);
            Refresh();
        }

        private static int Wrap(int i, int n) => (i % n + n) % n;

        private void Heading(float y, float x, string text)
        {
            RectTransform h = UiTheme.Rect(Panel, text, new Vector2(0.5f, 1f), new Vector2(x, y), new Vector2(520f, 34f));
            UiTheme.Label(h, "Text", text, 22, TextAnchor.MiddleLeft, UiTheme.Gold, FontStyle.Bold);
        }

        private void Label(float y, float x, string text)
        {
            RectTransform l = UiTheme.Rect(Panel, text, new Vector2(0.5f, 1f), new Vector2(x, y), new Vector2(220f, 40f));
            UiTheme.Label(l, "Text", text, 18, TextAnchor.MiddleLeft, UiTheme.Ink);
        }

        private void Cycler(float y, float x, string label, Func<string> value, Action<int> step) => Cycler(y, x, label, value, step, out _);

        /// <summary>A row: its name on the left, ‹ value › on the right.</summary>
        private void Cycler(float y, float x, string label, Func<string> value, Action<int> step, out Text valueText)
        {
            Label(y, x - 150f, label);
            RectTransform v = UiTheme.Rect(Panel, label + "_Value", new Vector2(0.5f, 1f), new Vector2(x + 120f, y), new Vector2(200f, 40f));
            UiTheme.PanelImage(v, UiTheme.SlotSprite);
            Text t = UiTheme.Label(v, "Text", "", 17, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            valueText = t;
            UiTheme.TextButton(Panel, label + "_Prev", "‹", new Vector2(0.5f, 1f), new Vector2(x - 6f, y), new Vector2(40f, 40f), () => { step(-1); Refresh(); }, 22);
            UiTheme.TextButton(Panel, label + "_Next", "›", new Vector2(0.5f, 1f), new Vector2(x + 246f, y), new Vector2(40f, 40f), () => { step(1); Refresh(); }, 22);
            rows.Add((t, value));
        }

        private readonly System.Collections.Generic.List<(Text text, Func<string> value)> rows = new System.Collections.Generic.List<(Text, Func<string>)>();

        private Text Description(float y, float x)
        {
            RectTransform d = UiTheme.Rect(Panel, "Description", new Vector2(0.5f, 1f), new Vector2(x, y - 40f), new Vector2(520f, 100f));
            Text t = UiTheme.Label(d, "Text", "", 16, TextAnchor.UpperLeft, UiTheme.Ink);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }

        private InputField SeedField(Vector2 at)
        {
            RectTransform f = UiTheme.Rect(Panel, "SeedField", new Vector2(0.5f, 1f), at, new Vector2(170f, 40f));
            UiTheme.PanelImage(f, UiTheme.SlotSprite);
            RectTransform tr = UiTheme.Rect(f, "Input", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150f, 34f));
            Text text = UiTheme.Label(tr, "Text", "", 18, TextAnchor.MiddleCenter, UiTheme.Ink);
            text.supportRichText = false;
            RectTransform pr = UiTheme.Rect(f, "Placeholder", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150f, 34f));
            Text placeholder = UiTheme.Label(pr, "Text", "випадковий", 16, TextAnchor.MiddleCenter, UiTheme.InkDim, FontStyle.Italic);
            InputField input = f.gameObject.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.contentType = InputField.ContentType.IntegerNumber;
            input.characterLimit = 9;
            return input;
        }

        private void Refresh()
        {
            foreach (var (text, value) in rows) text.text = value();
            int others = CampaignSetup.HeroCounts[heroesIndex] - 1;
            int kings = Mathf.RoundToInt(others * CampaignSetup.KingShares[shareIndex]);
            float km = CampaignSetup.Sizes[sizeIndex] / 1000f;
            summary.text = $"Крім вас — {others} героїв: {kings} королів і {others - kings} вільних ватаг, кожен на своїй стоянці. "
                         + $"Світ {km:0} × {km:0} км, гори по краю, озера; ближчі сусіди — від {World.StartSites.ClearanceFor(CampaignSetup.Sizes[sizeIndex]) / 1000f:0.#} км.";
            classDescription.text = HeroClassRules.Description(Classes[classIndex]) + "\n" + TipText.Dim("Клас обирається один раз.");
            artifactDescription.text = Artifacts.Description(Artifacts.All[artifactIndex]);
        }

        private void Start()
        {
            setup.Seed = int.TryParse(seedField.text, out int seed) ? seed : 0;
            setup.Size = CampaignSetup.Sizes[sizeIndex];
            setup.Heroes = CampaignSetup.HeroCounts[heroesIndex];
            setup.KingShare = CampaignSetup.KingShares[shareIndex];
            setup.Class = Classes[classIndex];
            setup.HasArtifact = true;
            setup.Artifact = Artifacts.All[artifactIndex];
            Advisor.Hidden = !tips;
            SaveSystem.NewGame(setup);
        }
    }
}
