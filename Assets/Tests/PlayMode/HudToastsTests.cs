using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using BattleOfAges.UI;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>A long toast (the address to give to other players) stays inside its plate and does not cover its neighbours.</summary>
    public class HudToastsTests
    {
        private const string Address = "Гру вже відкрито: адреса 192.168.0.101, порт 7777";

        private GameObject canvas;

        [SetUp]
        public void Setup()
        {
            canvas = new GameObject("ToastCanvas", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        }

        [TearDown]
        public void Teardown() => Object.Destroy(canvas);

        [UnityTest]
        public IEnumerator ALongMessage_StaysInsideItsPlate_AndClearOfTheOthers()
        {
            HudToasts toasts = HudToasts.Create((RectTransform)canvas.transform);
            toasts.Show("older", 1, n => "+2 деревини");
            toasts.Show("address", 1, n => Address);
            toasts.Show("newer", 1, n => "+3 руди");
            for (int i = 0; i < 40; i++) yield return null;
            Canvas.ForceUpdateCanvases();

            RectTransform older = Plate(toasts, 0), address = Plate(toasts, 1), newer = Plate(toasts, 2);
            Text label = address.GetComponentInChildren<Text>();
            Assert.AreEqual(Address, label.text);

            TextGenerator shaped = label.cachedTextGenerator;
            Assert.GreaterOrEqual(shaped.lineCount, 2, "too long for one line of the plate");
            float left = float.MaxValue, right = float.MinValue;
            foreach (UIVertex v in shaped.verts)
            {
                left = Mathf.Min(left, v.position.x);
                right = Mathf.Max(right, v.position.x);
            }
            Assert.LessOrEqual(right - left, address.rect.width, "every letter inside the plate");
            Assert.GreaterOrEqual(address.rect.height, label.preferredHeight, "every line inside the plate");

            Assert.AreEqual(42f, older.rect.height, 0.01f, "a short message keeps its usual plate");
            Assert.AreEqual(42f, newer.rect.height, 0.01f);
            Assert.Greater(address.anchoredPosition.y, newer.anchoredPosition.y + newer.rect.height, "above the newer one, not over it");
            Assert.Greater(older.anchoredPosition.y, address.anchoredPosition.y + address.rect.height, "and below the older one");
        }

        private static RectTransform Plate(HudToasts toasts, int index) => (RectTransform)toasts.transform.GetChild(index);
    }
}
