using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Net;
using BattleOfAges.SaveLoad;
using BattleOfAges.UI;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>The global map (§5.4): the relief as a picture, every kingdom still standing on it, and a guest told what only the host knows.</summary>
    public class WorldMapTests
    {
        [TearDown]
        public void Teardown()
        {
            if (WorldMapUI.IsVisible) WorldMapUI.Instance.Close();
            WorldMapData.Tell(new List<WorldMapEntry>());
            NpcKingdom.RemoveAll();
            KingdomRealm.Clear();
            WorldSettings.Set(0, 0f);
        }

        private static KingdomState State(string name, Vector3 at, bool free = false, bool empire = false, bool fallen = false)
        {
            var s = new KingdomState { Name = name, Free = free, Empire = empire, Defeated = fallen, Home = at, Hall = free ? 0 : 2 };
            s.Subjects.Add(new UnitState());
            return s;
        }

        [Test]
        public void ThePicture_ShowsTheRelief_ByTheSameMapAsTheGround()
        {
            WorldSettings.Set(4242, 4000f);
            CoarseWorldMap map = CoarseWorldMap.Current;
            Texture2D picture = WorldMapPicture.Draw(map);
            try
            {
                Assert.AreEqual(map.Cells * WorldMapPicture.PixelsPerCell, picture.width);
                Assert.AreEqual(new Vector2(0.5f, 0.5f), WorldMapPicture.Place(map, new Vector3(map.Origin + map.Cells * CoarseWorldMap.CellSize * 0.5f, 0f, map.Origin + map.Cells * CoarseWorldMap.CellSize * 0.5f)), "the world's middle, the picture's");

                Assert.IsTrue(map.TryCell(new Vector3(-1990f, 0f, 1990f), out int wx, out int wz) && map.Wall(wx, wz));
                Color corner = picture.GetPixel(wx * WorldMapPicture.PixelsPerCell + 2, wz * WorldMapPicture.PixelsPerCell + 2);
                var shades = new HashSet<int>();
                Color middle = picture.GetPixel(picture.width / 2, picture.height / 2);
                for (int y = 0; y < picture.height; y += 7)
                    for (int x = 0; x < picture.width; x += 7)
                        shades.Add(Mathf.RoundToInt(picture.GetPixel(x, y).grayscale * 40f));
                Assert.Greater(shades.Count, 8, "hills and valleys, not a flat colour");
                Assert.Greater(Vector3.Distance((Vector4)corner, (Vector4)middle), 0.1f, "the wall round the world stands out");

                for (int z = 0; z < map.Cells; z++)
                    for (int x = 0; x < map.Cells; x++)
                    {
                        if (!map.Water(x, z) || !map.Water(Mathf.Min(x + 1, map.Cells - 1), z) || !map.Water(x, Mathf.Min(z + 1, map.Cells - 1))) continue;
                        Color wet = picture.GetPixel(x * WorldMapPicture.PixelsPerCell + 2, z * WorldMapPicture.PixelsPerCell + 2);
                        Assert.Greater(wet.b, wet.r, "a lake is blue");
                        return;
                    }
            }
            finally { Object.Destroy(picture); }
        }

        [Test]
        public void ThePicture_IsDrawnALittleAtATime_ThenIsReady()
        {
            WorldSettings.Set(4243, 3000f);
            Assert.IsFalse(WorldMapPicture.Ready, "a new world's is not drawn yet");
            WorldMapPicture.Advance(0.01f);
            Assert.IsFalse(WorldMapPicture.Ready, "a moment does not draw it all");
            int frames = 0;
            while (!WorldMapPicture.Ready && frames++ < 100000) WorldMapPicture.Advance(2f);
            Assert.IsTrue(WorldMapPicture.Ready);
            Assert.AreSame(WorldMapPicture.Current, WorldMapPicture.Current, "and once only");
            TestContext.WriteLine($"3 km: {frames} slices of 2 ms");
        }

        [Test]
        public void Standing_ShowsEveryKingdom_ButNotTheFallen()
        {
            var far = new List<KingdomState>
            {
                State("Перше", new Vector3(100f, 0f, 0f)),
                State("Ватага: Друга", new Vector3(200f, 0f, 0f), free: true),
                State("Центральна імперія", new Vector3(300f, 0f, 0f), empire: true),
                State("Впале", new Vector3(400f, 0f, 0f), fallen: true),
            };
            List<WorldMapEntry> shown = WorldMapData.Standing(far, new List<NpcKingdom>());
            Assert.AreEqual(3, shown.Count);
            Assert.AreEqual(WorldMapKind.Kingdom, shown[0].Kind);
            Assert.AreEqual(WorldMapKind.FreeBand, shown[1].Kind);
            Assert.AreEqual(WorldMapKind.Empire, shown[2].Kind);
            Assert.AreEqual(2, shown[0].Hall);
            Assert.AreEqual(1, shown[0].People);
        }

        [Test]
        public void AKingdom_InWords_SaysHowFarAndHowBig()
        {
            string words = WorldMapUI.Describe(new WorldMapEntry { Name = "Перше", At = new Vector3(2500f, 0f, 0f), Kind = WorldMapKind.Kingdom, Hall = 2, People = 30 }, Vector3.zero);
            StringAssert.Contains("Перше", words);
            StringAssert.Contains("рівень 2", words);
            StringAssert.Contains("30", words);
            StringAssert.Contains("2", WorldMapUI.Far(new Vector3(2500f, 0f, 0f), Vector3.zero));
            StringAssert.Contains("км", WorldMapUI.Far(new Vector3(2500f, 0f, 0f), Vector3.zero));
            StringAssert.Contains("300 м", WorldMapUI.Far(new Vector3(300f, 0f, 0f), Vector3.zero));
        }

        [Test]
        public void WhatTheHostTells_ComesThroughWhole()
        {
            var told = new NetWorldMap();
            told.Kingdoms.Add(new WorldMapEntry { Name = "Перше", At = new Vector3(1f, 2f, 3f), Kind = WorldMapKind.Empire, Hall = 3, People = 40 });
            told.Players.Add(new NetPlayerAt { Number = 2, At = new Vector3(4f, 5f, 6f) });
            NetWorldMap back = JsonUtility.FromJson<NetWorldMap>(JsonUtility.ToJson(told));
            Assert.AreEqual("Перше", back.Kingdoms[0].Name);
            Assert.AreEqual(WorldMapKind.Empire, back.Kingdoms[0].Kind);
            Assert.AreEqual(40, back.Kingdoms[0].People);
            Assert.AreEqual(2, back.Players[0].Number);
            Assert.AreEqual(new Vector3(4f, 5f, 6f), back.Players[0].At);
        }

        [UnityTest]
        public IEnumerator TheWindow_ShowsThemAll_AndCloses()
        {
            WorldSettings.Set(4242, 4000f);
            KingdomRealm.Load(new List<KingdomState>
            {
                State("Перше", new Vector3(1500f, 0f, 1500f)),
                State("Центральна імперія", new Vector3(-1500f, 0f, 1500f), empire: true),
                State("Ватага: Друга", new Vector3(1500f, 0f, -1500f), free: true),
            });
            yield return null;
            WorldMapUI ui = WorldMapUI.Instance;
            Assert.IsNotNull(ui, "it starts by itself");
            ui.Open();
            yield return null;
            Assert.IsTrue(GameWindows.AnyModalOpen, "it holds the game's input like any window");
            Assert.GreaterOrEqual(ui.MarksShown, 3, "each of them marked");
            StringAssert.Contains("Королівств: 2", ui.ListText, "a free band is not a kingdom");
            StringAssert.Contains("Перше", ui.ListText);
            StringAssert.DoesNotContain("Ватага", ui.ListText);
            GameWindows.CloseAllModals();
            Assert.IsFalse(WorldMapUI.IsVisible, "Esc closes it with the rest");
        }
    }
}
