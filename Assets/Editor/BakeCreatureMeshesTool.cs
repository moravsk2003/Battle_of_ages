#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Utils;

namespace BattleOfAges.EditorTools
{
    /// <summary>
    /// Sculpts the animals (SdfCreature) once in the editor and saves their meshes, and the distant LOD meshes where a
    /// species has them, to Resources/Creatures, so the game loads them instead of sculpting on first spawn (about a
    /// second per species). File names carry a signature of the species code; if the code changes, the game sculpts at
    /// runtime and warns.
    /// </summary>
    public static class BakeCreatureMeshesTool
    {
        private static readonly (AnimalType type, string key)[] Species =
        {
            (AnimalType.Boar, "Boar"), (AnimalType.Deer, "Deer"), (AnimalType.Rabbit, "Rabbit")
        };

        [MenuItem("Battle of Ages/Bake Creature Meshes")]
        public static void Bake()
        {
            string folder = "Assets/Resources/" + SdfCreature.BakedFolder;
            Directory.CreateDirectory(folder);
            foreach ((AnimalType type, string key) in Species)
            {
                // Never load an old bake while rebuilding (also older "Blockout_" bakes of the previous sculptor)
                foreach (string old in Directory.GetFiles(folder, $"*_{key}_*.asset")) AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
                SdfCreature.ClearCache();
                GameObject temp = AnimalModelBuilder.CreateAnimalPrefab(type, "BakeTemp_" + key);
                Mesh mesh = SdfCreature.CachedMesh(key);
                if (mesh != null) Save(mesh, folder);
                else Debug.LogError($"[Battle of Ages] No mesh was built for {key}.");
                Mesh distant = SdfCreature.CachedMesh(key + SdfCreature.LodSuffix); // species with a LOD
                if (distant != null) Save(distant, folder);
                Object.DestroyImmediate(temp);
            }
            SdfCreature.ClearCache();
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
