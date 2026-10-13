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
            library.VillagerModel = AssetDatabase.LoadAssetAtPath<GameObject>(VillagerPath);
            library.VillagerAvatar = LoadSub<Avatar>(VillagerPath);
            library.VillagerController = BuildController($"{ControllerDir}/VillagerAnimator.controller", VillagerPath);

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

            AnimatorState attack = sm.AddState("Attack");
            attack.motion = Clip("Attack");
            AnimatorStateTransition toAttack = sm.AddAnyStateTransition(attack);
            toAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            toAttack.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            toAttack.duration = 0.08f;
            toAttack.canTransitionToSelf = false;
            AnimatorStateTransition fromAttack = attack.AddTransition(locomotion);
            fromAttack.hasExitTime = true;
            fromAttack.exitTime = 0.85f;
            fromAttack.duration = 0.15f;

            AnimatorState death = sm.AddState("Death");
            death.motion = Clip("Death");
            AnimatorStateTransition toDeath = sm.AddAnyStateTransition(death);
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
            toDeath.duration = 0.12f;
            toDeath.canTransitionToSelf = false;

            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        private static T LoadSub<T>(string path) where T : Object =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<T>().FirstOrDefault();
    }
}
#endif
