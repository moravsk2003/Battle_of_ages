#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Utils;

namespace BattleOfAges.EditorTools
{
    /// <summary>
    /// Sculpts the SdfCreature models (animals and the knight guard) once in the editor and saves their meshes, and the
    /// distant LOD meshes where a model has them, to Resources/Creatures, so the game loads them instead of sculpting on
    /// first spawn (one to several seconds each). File names carry a signature of the model code; if the code changes,
    /// the game sculpts at runtime and warns.
    /// </summary>
    public static class BakeCreatureMeshesTool
    {
        private static readonly (string key, Func<string, GameObject> create)[] Models =
        {
            ("Boar", n => AnimalModelBuilder.CreateAnimalPrefab(AnimalType.Boar, n)),
            ("Deer", n => AnimalModelBuilder.CreateAnimalPrefab(AnimalType.Deer, n)),
            ("Rabbit", n => AnimalModelBuilder.CreateAnimalPrefab(AnimalType.Rabbit, n)),
            ("Knight", n => HumanoidModelBuilder.CreateHumanoidModel(HumanoidModelBuilder.HumanType.KnightGuard, n)),
        };

        [MenuItem("Battle of Ages/Bake Creature Meshes")]
        public static void Bake()
        {
            string folder = "Assets/Resources/" + SdfCreature.BakedFolder;
            Directory.CreateDirectory(folder);
            foreach ((string key, Func<string, GameObject> create) in Models)
            {
                // Never load an old bake while rebuilding (also older "Blockout_" bakes of the previous sculptor)
                foreach (string old in Directory.GetFiles(folder, $"*_{key}_*.asset")) AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
                SdfCreature.ClearCache();
                GameObject temp = create("BakeTemp_" + key);
                Mesh mesh = SdfCreature.CachedMesh(key);
                if (mesh != null) Save(mesh, folder);
                else Debug.LogError($"[Battle of Ages] No mesh was built for {key}.");
                Mesh distant = SdfCreature.CachedMesh(key + SdfCreature.LodSuffix); // models with a LOD
                if (distant != null) Save(distant, folder);
                UnityEngine.Object.DestroyImmediate(temp);
            }
            SdfCreature.ClearCache();
            AssetDatabase.SaveAssets();
        }

        private static void Save(Mesh mesh, string folder)
        {
            string path = $"{folder}/{mesh.name}.asset"; // name carries the signature
            Mesh copy = UnityEngine.Object.Instantiate(mesh);
            copy.name = mesh.name;
            AssetDatabase.CreateAsset(copy, path);
            Debug.Log($"[Battle of Ages] Baked {mesh.name}: {mesh.vertexCount} vertices -> {path}");
        }
    }
}
#endif
