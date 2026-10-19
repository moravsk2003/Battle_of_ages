#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using BattleOfAges.Utils;

namespace BattleOfAges.EditorTools
{
    /// <summary>
    /// Prepares the rigged FBX characters (Assets/Models) for the game: renames and loops their clips, builds an
    /// Animator controller per model (Speed-driven Idle/Walk/Run blend tree, Attack trigger, Dead bool) and writes
    /// Resources/CharacterModelLibrary.asset, which HumanoidModelBuilder uses at runtime.
    /// </summary>
    public static class CharacterModelSetupTool
    {
        private const string KnightPath = "Assets/Models/KnightGuard.fbx";
        private const string VillagerPath = "Assets/Models/Villager.fbx";
        private const string ControllerDir = "Assets/Characters";
        private const string LibraryPath = "Assets/Resources/" + CharacterModelLibrary.ResourcePath + ".asset";

        // FBX take name -> (clip name, loop)
        private static readonly Dictionary<string, (string, bool)> KnightClips = new Dictionary<string, (string, bool)>
        {
            { "HumanArmature|Idle_swordRight", ("Idle", true) },
            { "HumanArmature|Walking", ("Walk", true) },
            { "HumanArmature|Run_swordRight", ("Run", true) },
            { "HumanArmature|Run_swordAttack", ("Attack", false) },
            { "HumanArmature|Death", ("Death", false) },
        };

        private static readonly Dictionary<string, (string, bool)> VillagerClips = new Dictionary<string, (string, bool)>
        {
            { "HumanArmature|Man_Idle", ("Idle", true) },
            { "HumanArmature|Man_Walk", ("Walk", true) },
            { "HumanArmature|Man_Run", ("Run", true) },
            { "HumanArmature|Man_SwordSlash", ("Attack", false) },
            { "HumanArmature|Man_Death", ("Death", false) },
        };

        [MenuItem("Battle of Ages/Setup Rigged Character Models")]
        public static void Setup()
        {
            ConfigureImport(KnightPath, KnightClips);
            ConfigureImport(VillagerPath, VillagerClips);

            Directory.CreateDirectory(ControllerDir);
            Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));

            var library = AssetDatabase.LoadAssetAtPath<CharacterModelLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<CharacterModelLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            library.KnightModel = AssetDatabase.LoadAssetAtPath<GameObject>(KnightPath);
            library.KnightAvatar = LoadSub<Avatar>(KnightPath);
            library.KnightController = BuildController($"{ControllerDir}/KnightAnimator.controller", KnightPath);
            library.KnightMesh = ProcessMesh(library.KnightModel, $"{ControllerDir}/Knight_Processed.asset", out _, out _);
            library.VillagerModel = AssetDatabase.LoadAssetAtPath<GameObject>(VillagerPath);
            library.VillagerAvatar = LoadSub<Avatar>(VillagerPath);
            library.VillagerController = BuildController($"{ControllerDir}/VillagerAnimator.controller", VillagerPath);
            library.VillagerMesh = ProcessMesh(library.VillagerModel, $"{ControllerDir}/Villager_Processed.asset", out library.VillagerEyeLeft, out library.VillagerEyeRight);

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Battle of Ages] Rigged characters ready: knight avatar={library.KnightAvatar != null}, villager avatar={library.VillagerAvatar != null}");
        }

        private static void ConfigureImport(string path, Dictionary<string, (string name, bool loop)> map)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.isReadable = true; // meshes are post-processed (smoothed normals, rest positions)

            var clips = new List<ModelImporterClipAnimation>();
            foreach (ModelImporterClipAnimation clip in importer.defaultClipAnimations)
            {
                if (!map.TryGetValue(clip.takeName, out var target)) continue;
                clip.name = target.name;
                clip.loopTime = target.loop;
                clip.lockRootPositionXZ = true;   // in-place: the CharacterController moves the unit
                clip.keepOriginalPositionXZ = false;
                clips.Add(clip);
            }
            importer.clipAnimations = clips.ToArray();
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Locomotion plays faster than its clips when the unit runs faster than the run clip (docs/GAME_DESIGN.md §11.1):
        /// the "LocoRate" parameter (CharacterAnimationDriver) multiplies the locomotion state's speed, so the feet do not slide.
        /// Patches the existing controllers without rebuilding the models.
        /// </summary>
        [MenuItem("Battle of Ages/Add Locomotion Rate To Animators")]
        public static void AddLocomotionRateToAnimators()
        {
            foreach (string path in new[] { $"{ControllerDir}/KnightAnimator.controller", $"{ControllerDir}/VillagerAnimator.controller" })
            {
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (ctrl == null) continue;
                AnimatorState loco = ctrl.layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Locomotion");
                if (loco != null) AddLocomotionRate(ctrl, loco);
                EditorUtility.SetDirty(ctrl);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Battle of Ages] LocoRate added to the animators");
        }

        private static void AddLocomotionRate(AnimatorController ctrl, AnimatorState locomotion)
        {
            if (!ctrl.parameters.Any(p => p.name == "LocoRate"))
                ctrl.AddParameter(new AnimatorControllerParameter { name = "LocoRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            locomotion.speedParameter = "LocoRate";
            locomotion.speedParameterActive = true;
        }

        private static AnimatorController BuildController(string path, string modelPath)
        {
            AnimationClip Clip(string name) => AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);

            AssetDatabase.DeleteAsset(path);
            AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Dead", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine sm = ctrl.layers[0].stateMachine;
            AnimatorState locomotion = ctrl.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(Clip("Idle"), 0f);
            tree.AddChild(Clip("Walk"), 1.4f);
            tree.AddChild(Clip("Run"), 4.2f);
            sm.defaultState = locomotion;
            AddLocomotionRate(ctrl, locomotion);

            // Attack plays on an upper-body layer (torso, arms, head) whose weight CharacterAnimationDriver raises
            // for the swing, so the legs keep walking / running underneath.
            AvatarMask upperBody = new AvatarMask { name = "UpperBodyMask" };
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            upperBody.AddTransformPath(model.transform, true);
            for (int i = 0; i < upperBody.transformCount; i++)
                upperBody.SetTransformActive(i, upperBody.GetTransformPath(i).Contains("/Torso"));
            AssetDatabase.AddObjectToAsset(upperBody, ctrl);

            var upperLayer = new AnimatorControllerLayer
            {
                name = "UpperBody",
                avatarMask = upperBody,
                defaultWeight = 0f,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = new AnimatorStateMachine { name = "UpperBody", hideFlags = HideFlags.HideInHierarchy }
            };
            AssetDatabase.AddObjectToAsset(upperLayer.stateMachine, ctrl);
            AnimatorState attack = upperLayer.stateMachine.AddState("Attack");
            attack.motion = Clip("Attack");
            upperLayer.stateMachine.defaultState = attack;
            ctrl.AddLayer(upperLayer);

            AnimatorState death = sm.AddState("Death");
            death.motion = Clip("Death");
            AnimatorStateTransition toDeath = sm.AddAnyStateTransition(death);
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
            toDeath.duration = 0.12f;
            toDeath.canTransitionToSelf = false;

            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        /// <summary>
        /// Copy of the model's skinned mesh with (a) normals smoothed across faces meeting at less than CreaseAngle
        /// (the FBX is flat-shaded; real creases such as collars and boot rims stay sharp) and (b) the rest-pose vertex
        /// position, in metres at game scale, stored in UV3 for CharacterSurface's procedural textures.
        /// </summary>
        private static Mesh ProcessMesh(GameObject modelPrefab, string assetPath, out Vector3 eyeLeft, out Vector3 eyeRight)
        {
            const float CreaseAngle = 75f;
            eyeLeft = eyeRight = Vector3.zero;
            if (modelPrefab == null) return null;

            GameObject root = new GameObject("MeshBakeRoot");
            GameObject inst = Object.Instantiate(modelPrefab, root.transform, false);
            RiggedCharacterBuilder.FitToHeight(root.transform, inst.transform);
            SkinnedMeshRenderer smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            Mesh src = smr.sharedMesh;

            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            Vector3[] bakedVerts = baked.vertices;
            var rest = new List<Vector3>(bakedVerts.Length);
            // BakeMesh output space depends on the renderer scale handling; pick the mapping that gives human height.
            var unscaled = new List<Vector3>(bakedVerts.Length);
            var scaled = new List<Vector3>(bakedVerts.Length);
            foreach (Vector3 v in bakedVerts)
            {
                unscaled.Add(smr.transform.position + smr.transform.rotation * v);
                scaled.Add(smr.transform.localToWorldMatrix.MultiplyPoint3x4(v));
            }
            rest = Mathf.Abs(HeightOf(scaled) - 1.78f) < Mathf.Abs(HeightOf(unscaled) - 1.78f) ? scaled : unscaled;

            Mesh mesh = Object.Instantiate(src);
            mesh.name = Path.GetFileNameWithoutExtension(assetPath);
            mesh.SetNormals(SmoothNormals(mesh, CreaseAngle));
            mesh.SetUVs(3, rest);

            // Eye centres from the "Eyes" sub-mesh (rest pose, metres): anchors for brows and beards.
            Material[] mats = smr.sharedMaterials;
            for (int s = 0; s < mats.Length && s < mesh.subMeshCount; s++)
            {
                if (mats[s] == null || !mats[s].name.ToLowerInvariant().Contains("eye")) continue;
                Vector3 sumL = Vector3.zero, sumR = Vector3.zero; int nL = 0, nR = 0;
                foreach (int idx in mesh.GetTriangles(s))
                {
                    Vector3 p = rest[idx];
                    if (p.y < 1.5f) continue; // the "Eyes" material also colours the shoe soles
                    if (p.x < 0f) { sumL += p; nL++; } else { sumR += p; nR++; }
                }
                if (nL > 0) eyeLeft = sumL / nL;
                if (nR > 0) eyeRight = sumR / nR;
            }

            Object.DestroyImmediate(baked);
            Object.DestroyImmediate(root);

            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(mesh, assetPath);
            return mesh;
        }

        private static float HeightOf(List<Vector3> points)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (Vector3 p in points) { lo = Mathf.Min(lo, p.y); hi = Mathf.Max(hi, p.y); }
            return hi - lo;
        }

        /// <summary>Angle-limited smoothing over vertices that share a position (flat-shaded meshes split them).</summary>
        private static Vector3[] SmoothNormals(Mesh mesh, float creaseAngle)
        {
            Vector3[] v = mesh.vertices;
            Vector3[] original = mesh.normals;
            int[] tris = mesh.triangles;
            float eps = mesh.bounds.size.magnitude * 1e-5f;

            var groupOf = new int[v.Length];
            var groups = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < v.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(v[i].x / eps), Mathf.RoundToInt(v[i].y / eps), Mathf.RoundToInt(v[i].z / eps));
                if (!groups.TryGetValue(key, out int g)) { g = groups.Count; groups[key] = g; }
                groupOf[i] = g;
            }

            var faceNormals = new Vector3[tris.Length / 3];   // area-weighted
            var facesOfGroup = new List<int>[groups.Count];
            for (int f = 0; f < faceNormals.Length; f++)
            {
                int a = tris[f * 3], b = tris[f * 3 + 1], c = tris[f * 3 + 2];
                faceNormals[f] = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                foreach (int vi in new[] { a, b, c })
                {
                    int g = groupOf[vi];
                    (facesOfGroup[g] ??= new List<int>()).Add(f);
                }
            }

            float cosLimit = Mathf.Cos(creaseAngle * Mathf.Deg2Rad);
            var result = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 own = original != null && original.Length == v.Length ? original[i] : Vector3.up;
                Vector3 sum = Vector3.zero;
                List<int> faces = facesOfGroup[groupOf[i]];
                if (faces != null)
                {
                    foreach (int f in faces)
                    {
                        float len = faceNormals[f].magnitude; // not .normalized: it returns zero below 1e-5, and FBX units are tiny
                        if (len > 0f && Vector3.Dot(faceNormals[f] / len, own) >= cosLimit) sum += faceNormals[f];
                    }
                }
                float sumLen = sum.magnitude;
                result[i] = sumLen > 0f ? sum / sumLen : own;
            }
            return result;
        }

        private static T LoadSub<T>(string path) where T : Object =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<T>().FirstOrDefault();
    }
}
#endif
