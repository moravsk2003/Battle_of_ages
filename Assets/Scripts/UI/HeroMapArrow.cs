using UnityEngine;
using BattleOfAges.Managers;
using BattleOfAges.Utils;

namespace BattleOfAges.UI
{
    /// <summary>Over the hero on the map, an arrow the way the hero looks, the same size on the screen whatever the zoom.</summary>
    public sealed class HeroMapArrow : MonoBehaviour
    {
        private const float ScreenShare = 0.05f, Lift = 2.5f;
        private GameObject arrow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindFirstObjectByType<HeroMapArrow>() != null) return;
            var go = new GameObject("HeroMapArrow");
            DontDestroyOnLoad(go);
            go.AddComponent<HeroMapArrow>();
        }

        private void Build()
        {
            Shader s = Shader.Find("Sprites/Default");
            if (s == null) return;
            arrow = new GameObject("Arrow");
            arrow.transform.SetParent(transform, false);
            Color gold = UiTheme.Gold, dark = new Color(0.1f, 0.07f, 0.04f);
            // A dark edge under a gold head: seen on any ground
            var v = new[]
            {
                new Vector3(0f, 0f, 1.25f), new Vector3(0.86f, 0f, -0.75f), new Vector3(0f, 0f, -0.25f), new Vector3(-0.86f, 0f, -0.75f),
                new Vector3(0f, 0.02f, 1f), new Vector3(0.62f, 0.02f, -0.56f), new Vector3(0f, 0.02f, -0.12f), new Vector3(-0.62f, 0.02f, -0.56f)
            };
            var mesh = new Mesh { name = "HeroMapArrow", vertices = v, colors = new[] { dark, dark, dark, dark, gold, gold, gold, gold }, triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 } };
            mesh.RecalculateBounds();
            arrow.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = arrow.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(s) { name = "MAT_HeroMapArrow" };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private void LateUpdate()
        {
            GameObject hero = GameContext.Player;
            Camera cam = ViewCamera.Current;
            bool show = GameContext.MapOpen && hero != null && cam != null;
            if (show && arrow == null) Build();
            if (arrow == null) return;
            if (arrow.activeSelf != show) arrow.SetActive(show);
            if (!show) return;
            Vector3 at = hero.transform.position;
            at.y = RTS.RtsUnit.GetTerrainHeight(at) + Lift;
            arrow.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, hero.transform.eulerAngles.y, 0f));
            arrow.transform.localScale = Vector3.one * (cam.orthographicSize * ScreenShare);
        }
    }
}
