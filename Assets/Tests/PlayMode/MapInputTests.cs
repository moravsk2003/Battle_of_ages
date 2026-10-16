using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Managers;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// The map ignores clicks over UI (RtsUnitCommander checks the EventSystem), so no invisible UI may cover the
    /// open middle of the screen on the map, or selecting and ordering units would stop working there.
    /// </summary>
    public class MapInputTests
    {
        [UnityTest]
        public IEnumerator MapView_MiddleOfTheScreen_IsNotCoveredByUi()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);
            GameModeManager modes = Object.FindFirstObjectByType<GameModeManager>();
            Assert.IsNotNull(modes);
            modes.SetGameMode(GameMode.RtsMap);
            yield return new WaitForSeconds(0.5f);
            Assert.IsNotNull(EventSystem.current, "UI needs an EventSystem.");

            var hits = new List<RaycastResult>();
            foreach (Vector2 f in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.3f, 0.45f), new Vector2(0.7f, 0.6f), new Vector2(0.5f, 0.35f) })
            {
                var data = new PointerEventData(EventSystem.current) { position = new Vector2(Screen.width * f.x, Screen.height * f.y) };
                EventSystem.current.RaycastAll(data, hits);
                foreach (RaycastResult h in hits)
                    if (!h.gameObject.name.StartsWith("MineLabel") && h.gameObject.transform.parent?.name != "MineLabel")
                        Assert.Fail($"UI '{h.gameObject.name}' covers the map at {f}");
            }
        }
    }
}
