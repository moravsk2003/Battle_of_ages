using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// A model made in Blender, converted for the game (docs/MODELS.md): the body and each moving part as one mesh with
    /// colour and surface per vertex (like MeshKit), empty sockets, and one merged distant mesh. Made in the editor by
    /// BlenderMeshImportTool from Assets/Models/&lt;Category&gt;/&lt;Name&gt;.fbx, saved to
    /// Resources/Models/&lt;Category&gt;/&lt;Name&gt;.asset with the meshes inside; Spawn builds it on the shared prop material.
    /// </summary>
    public sealed class ImportedModel : ScriptableObject
    {
        public const string ResourcesFolder = "Models";

        /// <summary>A mesh with its frame: Parent is the part it hangs on ("" = the model root), the pose is relative to it.</summary>
        [Serializable]
        public sealed class Part
        {
            public string Name;
            public string Parent = "";
            public Vector3 Position;
            public Quaternion Rotation = Quaternion.identity;
            public Mesh Mesh;
        }

        /// <summary>An empty point of action (muzzle, smoke hole, hand); its +Y (transform.up) is the Blender empty's +Z, where the action points.</summary>
        [Serializable]
        public sealed class Socket
        {
            public string Name;
            public string Parent = "";
            public Vector3 Position;
            public Quaternion Rotation = Quaternion.identity;
        }

        public string Category = "";
        /// <summary>Parts[0] is the body (in the model root's frame); the rest move.</summary>
        public List<Part> Parts = new List<Part>();
        public List<Socket> Sockets = new List<Socket>();
        /// <summary>Everything merged in the root frame, simplified (or the model's own _Far object).</summary>
        public Mesh Far;
        public int NearTriangles, FarTriangles;
        /// <summary>Bounds of the near meshes in the root frame, metres.</summary>
        public Bounds Bounds;

        public static ImportedModel Load(string category, string name) => UnityEngine.Resources.Load<ImportedModel>($"{ResourcesFolder}/{category}/{name}");

        public Part FindPart(string name) => Parts.Find(p => p.Name == name);

        /// <summary>
        /// The model under parent: one object per part (named as in Blender, nested as there), sockets as empty transforms,
        /// "Far" with the merged mesh, and a LODGroup that switches to it below nearScreenHeight of the screen height.
        /// </summary>
        public GameObject Spawn(Transform parent, string objectName, float nearScreenHeight = 0.05f)
        {
            var root = new GameObject(objectName);
            root.transform.SetParent(parent, false);
            var frames = new Dictionary<string, Transform> { [""] = root.transform };
            var near = new List<Renderer>();
            Material material = MeshKit.PropMaterial();

            for (int i = 0; i < Parts.Count; i++)
            {
                Part p = Parts[i];
                Transform t;
                if (i == 0) t = root.transform; // the body is drawn on the root itself
                else
                {
                    t = new GameObject(p.Name).transform;
                    t.SetParent(frames.TryGetValue(p.Parent, out Transform up) ? up : root.transform, false);
                    t.localPosition = p.Position;
                    t.localRotation = p.Rotation;
                }
                frames[p.Name] = t;
                if (p.Mesh == null) continue;
                GameObject holder = i == 0 ? new GameObject("Body") : t.gameObject;
                if (i == 0) holder.transform.SetParent(root.transform, false);
                holder.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
                MeshRenderer mr = holder.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                near.Add(mr);
            }

            foreach (Socket s in Sockets)
            {
                var t = new GameObject(s.Name).transform;
                t.SetParent(frames.TryGetValue(s.Parent, out Transform up) ? up : root.transform, false);
                t.localPosition = s.Position;
                t.localRotation = s.Rotation;
            }

            if (Far != null)
            {
                var far = new GameObject("Far");
                far.transform.SetParent(root.transform, false);
                far.AddComponent<MeshFilter>().sharedMesh = Far;
                MeshRenderer fr = far.AddComponent<MeshRenderer>();
                fr.sharedMaterial = material;
                LODGroup lod = root.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(nearScreenHeight, near.ToArray()), new LOD(0.004f, new Renderer[] { fr }) });
                lod.RecalculateBounds();
            }
            return root;
        }

        /// <summary>The transform of a part or socket in a spawned model (by its Blender name).</summary>
        public static Transform Find(GameObject spawned, string name)
        {
            foreach (Transform t in spawned.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
