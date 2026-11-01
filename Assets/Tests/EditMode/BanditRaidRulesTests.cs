using NUnit.Framework;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.World;

namespace BattleOfAges.Tests
{
    /// <summary>The early threat (0.2.1): bands grow, and a campaign's camps are rarer than M0's and not by the base.</summary>
    [TestFixture]
    public class BanditRaidRulesTests
    {
        [TearDown]
        public void TearDown() => WorldSettings.Set(0, 0f);

        [Test]
        public void EachBand_IsBigger_UpToTheMost()
        {
            Assert.AreEqual(GameBalance.BanditRaidFirst, BanditRaids.BandSize(0));
            Assert.Greater(BanditRaids.BandSize(1), BanditRaids.BandSize(0));
            Assert.AreEqual(GameBalance.BanditRaidMax, BanditRaids.BandSize(1000));
        }

        private static float Share(float size)
        {
            int camps = 0, chunks = 0;
            for (int x = -60; x < 60; x++)
                for (int z = -60; z < 60; z++)
                {
                    if (new Vector2(x, z).magnitude * size < 800f) continue;
                    chunks++;
                    if (ProceduralBiomeChunk.CampChunk(new Vector2Int(x, z), size)) camps++;
                }
            return camps / (float)chunks;
        }

        [Test]
        public void ACampaign_HasRarerCamps_NoneByTheBase_TheOldWorldKeepsItsOwn()
        {
            const float size = 64f;
            WorldSettings.Set(0, 0f);
            float old = Share(size);
            Assert.AreEqual(0.2f, old, 0.05f, "one in five in M0's world");
            WorldSettings.Set(1234, WorldSettings.DefaultSize);
            float campaign = Share(size);
            TestContext.WriteLine($"camps per chunk: old {old:0.000}, campaign {campaign:0.000}");
            Assert.AreEqual(1f / GameBalance.CampaignCampEvery, campaign, 0.02f);
            for (int x = -3; x <= 3; x++)
                for (int z = -3; z <= 3; z++)
                    Assert.IsFalse(ProceduralBiomeChunk.CampChunk(new Vector2Int(x, z), size), "none by the base");
        }
    }
}
