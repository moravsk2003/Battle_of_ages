#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Utils;

namespace BattleOfAges.EditorTools
{
    /// <summary>
    /// Sculpts the animals once in the editor (boar / deer: SdfCreature, rabbit: AnimalSculptor) and saves their meshes
    /// to Resources/Creatures, so the game loads them instead of sculpting on first spawn (about a second per species).
    /// File names carry a signature of the species code; if the code changes, the game sculpts at runtime and warns.
    /// </summary>
    public static class BakeCreatureMeshesTool
    {
        private static readonly (AnimalType type, string key, string prefix)[] Species =
        {
            (AnimalType.Boar, "Boar", "Sculpted_"), (AnimalType.Deer, "Deer", "Sculpted_"), (AnimalType.Rabbit, "Rabbit", "Blockout_")
        };

        [MenuItem("Battle of Ages/Bake Creature Meshes")]
        public static void Bake()
        {
            string folder = "Assets/Resources/" + SdfCreature.BakedFolder;
            Directory.CreateDirectory(folder);
            foreach ((AnimalType type, string key, string prefix) in Species)
            {
                // Never load an old bake while rebuilding (either builder: a species may have moved to the other one)
                foreach (string old in Directory.GetFiles(folder, $"*_{key}_*.asset")) AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
                SdfCreature.ClearCache();
                AnimalSculptor.ClearCache();
                GameObject temp = AnimalModelBuilder.CreateAnimalPrefab(type, "BakeTemp_" + key);
                Mesh mesh = prefix == "Sculpted_" ? SdfCreature.CachedMesh(key) : AnimalSculptor.CachedMesh(key);
                if (mesh != null) Save(mesh, folder);
                else Debug.LogError($"[Battle of Ages] No mesh was built for {key}.");
                Mesh distant = prefix == "Sculpted_" ? SdfCreature.CachedMesh(key + SdfCreature.LodSuffix) : null; // species with a LOD
                if (distant != null) Save(distant, folder);
                Object.DestroyImmediate(temp);
            }
            SdfCreature.ClearCache();
            AnimalSculptor.ClearCache();
            AssetDatabase.SaveAssets();
        }

        private static void Save(Mesh mesh, string folder)
        {
            string path = $"{folder}/{mesh.name}.asset"; // name carries the signature
            Mesh copy = Object.Instantiate(mesh);
            copy.name = mesh.name;
            AssetDatabase.CreateAsset(copy, path);
            Debug.Log($"[Battle of Ages] Baked {mesh.name}: {mesh.vertexCount} vertices -> {path}");
        }
    }
}
#endif
