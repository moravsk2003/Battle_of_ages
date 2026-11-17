using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Managers;
using BattleOfAges.Utils;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>The map (M) opens with the hero in the middle of the view, not "somewhere in a field" (a tester, 2026-11-17).</summary>
    public class MapOpensOnHeroTests
    {
        [UnityTearDown]
        public IEnumerator LeaveTheScene()
        {
            Scene empty = SceneManager.CreateScene("Empty_" + System.Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded && !scene.name.StartsWith("InitTestScene")) yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        [UnityTest]
        public IEnumerator TheMap_OpensOnTheHero()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            GameObject hero = GameContext.Player;
            Assert.IsNotNull(hero, "the scene has its hero");
            for (int round = 0; round < 2; round++)
            {
                // (the second time after the map was dragged far off and closed: it opens on the hero again)
                if (round == 1)
                {
                    GameContext.MapCamera.FocusOnPosition(hero.transform.position + new Vector3(120f, 0f, -80f));
                    yield return null;
                    GameContext.Modes.SetGameMode(GameMode.FirstPerson);
                    yield return new WaitForSeconds(0.2f);
                }
                GameContext.Modes.SetGameMode(GameMode.RtsMap);
                yield return new WaitForSeconds(0.3f);
                Camera cam = ViewCamera.Current;
                Assert.IsNotNull(cam);
                Vector3 v = cam.WorldToViewportPoint(hero.transform.position);
                Assert.Greater(v.z, 0f, "in front of the camera");
                Assert.That(Mathf.Abs(v.x - 0.5f), Is.LessThan(0.15f), $"round {round}: the hero across the middle of the view ({v})");
                Assert.That(Mathf.Abs(v.y - 0.5f), Is.LessThan(0.15f), $"round {round}: the hero up and down in the middle of the view ({v})");
            }
        }
    }
}
