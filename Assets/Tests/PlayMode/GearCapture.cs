using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Crafting;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Visual review tool for worn gear (not a regular test): villagers in full sets of levels I..V (front, back,
    /// close-up), the four weapons, and the knight-skeleton roles (hero, warlord) equipped. CAPTURE_DIR = output folder.
    /// </summary>
    [Explicit("Visual capture tool, run on demand")]
    public class GearCapture
    {
        private static readonly ItemType[] Armour = { ItemType.Helmet, ItemType.Armor, ItemType.Gloves, ItemType.Boots, ItemType.Shield };

        [UnityTest]
        public IEnumerator CaptureGear()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Directory.GetCurrentDirectory(), ".agent_vision", "gear");
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);
            float g = InfiniteWorldManager.GetSampledTerrainHeight(18f, -24f);
            Vector3 c = new Vector3(18f, g, -24f);
            Vector3 cam = c + new Vector3(0f, 1.5f, -6.5f);

            for (int l = 1; l <= 5; l++)
            {
                var items = new List<Item> { new Item(ItemType.Sword, l) };
                foreach (ItemType t in Armour) items.Add(new Item(t, l));
                Spawn(HumanoidModelBuilder.HumanType.Villager, c + new Vector3((l - 3) * 1.4f, 0f, 0f), cam, items);
            }
            Vector3 w = c + new Vector3(0f, 0f, -14f), wCam = w + new Vector3(0f, 1.4f, -5f);
            ItemType[] weapons = { ItemType.Sword, ItemType.Axe, ItemType.Pickaxe, ItemType.Bow };
            for (int i = 0; i < weapons.Length; i++)
                Spawn(HumanoidModelBuilder.HumanType.Bandit, w + new Vector3((i - 1.5f) * 1.5f, 0f, 0f), wCam, new List<Item> { new Item(weapons[i], 2 + i) });
            Vector3 k = c + new Vector3(12f, 0f, 0f), kCam = k + new Vector3(0f, 1.6f, -6f);
            var kit = new List<Item> { new Item(ItemType.Sword, 4) };
            foreach (ItemType t in Armour) kit.Add(new Item(t, 4));
            Spawn(HumanoidModelBuilder.HumanType.HeroPlayer, k + new Vector3(-1f, 0f, 0f), kCam, kit);
            Spawn(HumanoidModelBuilder.HumanType.Warlord, k + new Vector3(1.2f, 0f, 0f), kCam, kit);
            yield return new WaitForSeconds(0.6f);

            var capture = new CaptureRenderer(dir, 6, 40f, drawGrass: true);
            Vector3 mid = c + Vector3.up * 1.0f;
            capture.Shoot(0, "sets_I_to_V_front", cam, mid);
            capture.Shoot(1, "sets_I_to_V_back", c + new Vector3(0f, 1.6f, 6.5f), mid);
            capture.Shoot(2, "close_III_V", c + new Vector3(1.9f, 1.45f, -2.6f), c + new Vector3(1.9f, 1.15f, 0f));
            capture.Shoot(3, "weapons", wCam, w + Vector3.up * 1f);
            capture.Shoot(4, "hero_warlord_IV", kCam, k + Vector3.up * 1.1f);
            capture.Shoot(5, "close_I_II_side", c + new Vector3(-4.8f, 1.4f, -1.6f), c + new Vector3(-2.1f, 1.1f, 0f));
            yield return null;
            capture.Finish();
        }

        private static void Spawn(HumanoidModelBuilder.HumanType role, Vector3 pos, Vector3 faceTowards, List<Item> items)
        {
            GameObject go = HumanoidModelBuilder.CreateHumanoidModel(role, "Gear_" + role);
            go.transform.position = pos;
            Vector3 dir = faceTowards - pos; dir.y = 0f;
            go.transform.rotation = Quaternion.LookRotation(dir);
            if (role == HumanoidModelBuilder.HumanType.Warlord) go.transform.localScale = Vector3.one * 1.25f;
            Equipment e = go.AddComponent<Equipment>();
            foreach (Item i in items) e.Equip(i);
        }
    }
}
