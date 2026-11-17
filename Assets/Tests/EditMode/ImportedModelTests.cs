using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Utils;

namespace BattleOfAges.Tests
{
    /// <summary>
    /// Blender models (docs/MODELS.md): ImportedModelConverter on a hierarchy shaped like an imported FBX — parts and
    /// their frames, sockets, surfaces from S_* materials, the distant mesh and its budget — and ImportedModel.Spawn.
    /// </summary>
    [TestFixture]
    public class ImportedModelTests
    {
        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private T Keep<T>(T o) where T : Object { made.Add(o); return o; }

        private static Mesh Primitive(PrimitiveType type)
        {
            GameObject g = GameObject.CreatePrimitive(type);
            Mesh m = g.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(g);
            return m;
        }

        private Material Mat(string name, Color color)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Color");
            var m = Keep(new Material(s) { name = name });
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            return m;
        }

        private GameObject Node(string name, Transform parent, Vector3 pos, Mesh mesh = null, params Material[] mats)
        {
            var g = new GameObject(name);
            if (parent == null) Keep(g); else g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            if (mesh != null)
            {
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                g.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
            return g;
        }

        /// <summary>A cube whose first two faces are one submesh and the rest another.</summary>
        private Mesh TwoMaterialCube()
        {
            Mesh cube = Primitive(PrimitiveType.Cube);
            var m = Keep(new Mesh { name = "TwoMat" });
            m.vertices = cube.vertices;
            m.normals = cube.normals;
            int[] t = cube.triangles;
            m.subMeshCount = 2;
            m.SetTriangles(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Take(t, 12)), 0);
            m.SetTriangles(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Skip(t, 12)), 1);
            return m;
        }

        /// <summary>FBX-like: file root "Gun" → object "Gun" (pivot moved and turned) → body, a loose grip, a slide with a hammer, sockets.</summary>
        private GameObject Gun(bool ownFar = false)
        {
            Mesh cube = Primitive(PrimitiveType.Cube);
            GameObject file = Node("Gun", null, Vector3.zero);
            GameObject root = Node("Gun", file.transform, new Vector3(1f, 2f, 3f));
            root.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            Node("Gun_Body", root.transform, Vector3.zero, TwoMaterialCube(), Mat("S_Metal.001", Color.red), Mat("S_Wood", Color.green));
            Node("Grip", root.transform, new Vector3(0f, -0.5f, 0f), cube, Mat("Plastic", Color.blue));
            GameObject slide = Node("Gun_Slide", root.transform, new Vector3(0f, 0.1f, 0f), cube, Mat("S_Steel", Color.white));
            Node("Socket_Muzzle", slide.transform, new Vector3(0f, 0f, 0.2f));
            Node("Gun_Hammer", slide.transform, new Vector3(0f, 0f, -0.3f), cube, Mat("S_Steel", Color.white));
            Node("Socket_Hand", root.transform, new Vector3(0f, -0.05f, 0f));
            if (ownFar) Node("Gun_Far", root.transform, Vector3.zero, cube, Mat("S_Metal", Color.grey));
            return file;
        }

        private ImportedModel Convert(GameObject source, string category, List<string> report, Quaternion? turn = null)
        {
            ImportedModel model = Keep(ImportedModelConverter.Convert(source, category, report, turn));
            foreach (ImportedModel.Part p in model.Parts) if (p.Mesh != null) Keep(p.Mesh);
            if (model.Far != null) Keep(model.Far);
            return model;
        }

        [Test]
        public void Parts_Sockets_AndFrames_FollowTheBlenderNames()
        {
            var report = new List<string>();
            ImportedModel m = Convert(Gun(), "Weapons", report);

            CollectionAssert.AreEqual(new[] { "Gun_Body", "Gun_Slide", "Gun_Hammer" }, m.Parts.ConvertAll(p => p.Name));
            Assert.AreEqual("", m.FindPart("Gun_Slide").Parent);
            Assert.AreEqual("Gun_Slide", m.FindPart("Gun_Hammer").Parent, "a part inside a part hangs on it");
            Assert.That(Vector3.Distance(new Vector3(0f, 0.1f, 0f), m.FindPart("Gun_Slide").Position), Is.LessThan(1e-4f),
                "pose relative to the model object, not the file root (which is moved and turned)");
            Assert.That(Vector3.Distance(new Vector3(0f, 0f, -0.3f), m.FindPart("Gun_Hammer").Position), Is.LessThan(1e-4f));

            ImportedModel.Socket muzzle = m.Sockets.Find(s => s.Name == "Socket_Muzzle"), hand = m.Sockets.Find(s => s.Name == "Socket_Hand");
            Assert.AreEqual("Gun_Slide", muzzle.Parent);
            Assert.That(Vector3.Distance(new Vector3(0f, 0f, 0.2f), muzzle.Position), Is.LessThan(1e-4f));
            Assert.AreEqual("", hand.Parent);
            Assert.That(Vector3.Distance(new Vector3(0f, -0.05f, 0f), hand.Position), Is.LessThan(1e-4f));

            // The body holds the two-material cube and the loose grip, in the model object's frame (pivot at its origin)
            Mesh body = m.Parts[0].Mesh;
            Assert.AreEqual(24, body.triangles.Length / 3);
            Bounds b = body.bounds;
            Assert.That(b.center.y, Is.EqualTo(-0.25f).Within(1e-3f));
            Assert.That(b.size.x, Is.EqualTo(1f).Within(1e-3f));
            Assert.AreEqual(48, m.NearTriangles);
        }

        [Test]
        public void BlenderFiles_AreTurnedToFaceTheGamesFront()
        {
            ImportedModel m = Convert(Gun(), "Weapons", new List<string>(), ImportedModelConverter.BlenderToGame);
            Assert.That(Vector3.Distance(new Vector3(0f, 0.1f, 0f), m.FindPart("Gun_Slide").Position), Is.LessThan(1e-4f), "up stays up");
            Assert.That(Vector3.Distance(new Vector3(0f, 0f, 0.3f), m.FindPart("Gun_Hammer").Position), Is.LessThan(1e-4f), "back to front");
            ImportedModel.Socket muzzle = m.Sockets.Find(s => s.Name == "Socket_Muzzle");
            Assert.That(Vector3.Distance(new Vector3(0f, 0f, -0.2f), muzzle.Position), Is.LessThan(1e-4f), "in the slide's turned frame too");
            Assert.That(Quaternion.Angle(Quaternion.identity, m.FindPart("Gun_Hammer").Rotation), Is.LessThan(0.01f), "frames turned alike: no relative turn");
            Assert.That(m.Parts[0].Mesh.bounds.center.y, Is.EqualTo(-0.25f).Within(1e-3f));
        }

        [Test]
        public void Surfaces_AndColours_ComeFromTheMaterials()
        {
            var report = new List<string>();
            ImportedModel m = Convert(Gun(), "Weapons", report);
            Mesh body = m.Parts[0].Mesh;
            var surfaces = new List<Vector3>();
            body.GetUVs(2, surfaces);
            Color[] colours = body.colors;
            Assert.AreEqual(body.vertexCount, surfaces.Count);

            bool metalRed = false, woodGreen = false;
            for (int i = 0; i < surfaces.Count; i++)
            {
                if (surfaces[i] == ImportedModelConverter.Surfaces["Metal"] && colours[i].r > 0.99f && colours[i].g < 0.01f) metalRed = true;
                if (surfaces[i] == ImportedModelConverter.Surfaces["Wood"] && colours[i].g > 0.99f && colours[i].r < 0.01f) woodGreen = true;
            }
            Assert.IsTrue(metalRed, "S_Metal.001 → metal, Blender's .001 ignored");
            Assert.IsTrue(woodGreen, "S_Wood → wood");
            Assert.IsTrue(report.Exists(l => l.Contains("\"Plastic\"")), "a material without S_ is reported");

            Assert.IsTrue(ImportedModelConverter.TryGetSurface("S_Wood_Dark", out Vector3 w));
            Assert.AreEqual(ImportedModelConverter.Surfaces["Wood"], w);
            Assert.IsFalse(ImportedModelConverter.TryGetSurface("Material", out _));
        }

        [Test]
        public void FarMesh_IsMergedWithinBudget_OrTheModelsOwn()
        {
            var report = new List<string>();
            ImportedModel small = Convert(Gun(), "Weapons", report);
            Assert.AreEqual(48, small.FarTriangles, "already within the far budget: all parts merged into one mesh");

            GameObject file = Node("Ball", null, Vector3.zero);
            Node("Ball", file.transform, Vector3.zero, Primitive(PrimitiveType.Sphere), Mat("S_Stone", Color.grey));
            ImportedModel ball = Convert(file, "Weapons", report);
            (int near, int far) = ImportedModelConverter.BudgetFor("Weapons");
            Assert.Greater(ball.NearTriangles, near);
            Assert.IsTrue(report.Exists(l => l.Contains("near mesh over budget")));
            Assert.That(ball.FarTriangles, Is.InRange(1, far), "clustered down to the budget");
            Assert.That(ball.Far.bounds.size.x, Is.GreaterThan(0.6f), "still the size of the ball");

            ImportedModel own = Convert(Gun(ownFar: true), "Weapons", report);
            Assert.AreEqual(12, own.FarTriangles, "the _Far object is the distant mesh");
            Assert.AreEqual(48, own.NearTriangles, "and not drawn near");
        }

        [Test]
        public void Spawn_BuildsPartsSockets_AndLod()
        {
            ImportedModel m = Convert(Gun(), "Weapons", new List<string>());
            GameObject g = Keep(m.Spawn(null, "GunInstance"));

            Transform slide = ImportedModel.Find(g, "Gun_Slide"), hammer = ImportedModel.Find(g, "Gun_Hammer"), muzzle = ImportedModel.Find(g, "Socket_Muzzle");
            Assert.IsNotNull(slide);
            Assert.AreEqual(slide, hammer.parent);
            Assert.AreEqual(slide, muzzle.parent);
            Assert.IsNotNull(ImportedModel.Find(g, "Socket_Hand"));
            LODGroup lod = g.GetComponent<LODGroup>();
            Assert.IsNotNull(lod);
            LOD[] lods = lod.GetLODs();
            Assert.AreEqual(2, lods.Length);
            Assert.AreEqual(3, lods[0].renderers.Length, "body, slide, hammer near");
            Assert.AreEqual(MeshKit.PropMaterial(), lods[0].renderers[0].sharedMaterial);
        }
    }
}
