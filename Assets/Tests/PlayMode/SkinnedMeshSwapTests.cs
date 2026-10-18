using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Utils;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Swapping a drawn SkinnedMeshRenderer's mesh between the knight's near and far meshes (SkinnedMeshLod) or the
    /// gear meshes (CharacterGear). A plain sharedMesh swap on an enabled, already drawn renderer makes Unity 6000.3 log
    /// "Rendering stopped because the data for mesh ... does not match the expected mesh data size and vertex stride"
    /// and the character vanishes; the unhandled error fails these tests.
    /// </summary>
    public class SkinnedMeshSwapTests
    {
        private Mesh near, far;
        private Camera cam;
        private RenderTexture rt;
        private GameObject root;
        private SkinnedMeshRenderer smr;

        [SetUp]
        public void SetUp()
        {
            // The baked file names carry a signature hash, so find the knight's pair by prefix
            near = far = null;
            foreach (Mesh m in UnityEngine.Resources.LoadAll<Mesh>(SdfCreature.BakedFolder))
            {
                if (m.name.StartsWith("Sculpted_Knight" + SdfCreature.LodSuffix + "_")) far = m;
                else if (m.name.StartsWith("Sculpted_Knight_")) near = m;
            }
            Assert.IsNotNull(near, "baked knight mesh"); Assert.IsNotNull(far, "baked knight far mesh");
            Assert.AreNotEqual(near.vertexCount, far.vertexCount);
            rt = new RenderTexture(256, 256, 24);
            cam = new GameObject("Cam").AddComponent<Camera>();
            cam.targetTexture = rt;
            cam.transform.position = new Vector3(0f, 1f, 3f);
            cam.transform.LookAt(new Vector3(0f, 1f, 0f));
            root = new GameObject("Root");
            var bones = new Transform[near.bindposes.Length];
            for (int i = 0; i < bones.Length; i++) { bones[i] = new GameObject("B" + i).transform; bones[i].SetParent(root.transform, false); }
            var skin = new GameObject("Skin");
            skin.transform.SetParent(root.transform, false);
            smr = skin.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            smr.bones = bones;
            smr.rootBone = root.transform;
            smr.localBounds = new Bounds(Vector3.up, Vector3.one * 4f);
        }

        [TearDown]
        public void TearDown()
        {
            SkinnedMeshLod.ViewerOverride = null;
            if (root != null) Object.Destroy(root);
            if (cam != null) Object.Destroy(cam.gameObject);
            if (rt != null) rt.Release();
        }

        private IEnumerator Frame() { cam.Render(); yield return null; }

        [UnityTest]
        public IEnumerator SetMeshOnDrawnRendererKeepsRendering()
        {
            smr.sharedMesh = far; yield return Frame(); yield return Frame();
            for (int i = 0; i < 6; i++)
            {
                SkinnedMeshLod.SetMesh(smr, (i & 1) == 0 ? near : far);
                Assert.IsTrue(smr.enabled);
                yield return Frame();
            }
            cam.Render(); SkinnedMeshLod.SetMesh(smr, near); cam.Render(); // twice in one frame
            yield return Frame();
        }

        [UnityTest]
        public IEnumerator SetMeshKeepsDisabledRendererDisabled()
        {
            smr.sharedMesh = far; yield return Frame();
            smr.enabled = false;
            SkinnedMeshLod.SetMesh(smr, near);
            Assert.IsFalse(smr.enabled);
            Assert.AreSame(near, smr.sharedMesh);
            smr.enabled = true; yield return Frame(); yield return Frame();
        }

        [UnityTest]
        public IEnumerator LodSwapsKeepRendering()
        {
            smr.sharedMesh = far;
            var lod = root.AddComponent<SkinnedMeshLod>();
            lod.Target = smr; lod.Near = near; lod.Far = far; lod.Size = 2f; lod.NearScreenHeight = 0.25f;
            SkinnedMeshLod.ViewerOverride = cam;
            for (int i = 0; i < 6; i++)
            {
                cam.transform.position = new Vector3(0f, 1f, (i & 1) == 0 ? 3f : 40f);
                SkinnedMeshLod.RefreshAll(cam);
                Assert.AreSame((i & 1) == 0 ? near : far, smr.sharedMesh);
                Assert.IsTrue(smr.enabled);
                yield return Frame(); yield return Frame();
            }
        }
    }
}
