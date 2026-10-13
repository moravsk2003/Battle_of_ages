#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Utils;

namespace BattleOfAges.EditorTools
{
    /// <summary>
    /// Sculpts the code-defined creatures (SdfCreature) once in the editor and saves their meshes to
    /// Resources/Creatures, so the game loads them instead of sculpting on first spawn (about a second per species).
    /// The runtime checks a signature and falls back to sculpting if the species code changed since the bake.
    /// </summary>
    public static class BakeCreatureMeshesTool
    {
        private static readonly (AnimalType type, string key)[] Species = { (AnimalType.Boar, "Boar") };

        [MenuItem("Battle of Ages/Bake Creature Meshes")]
        public static void Bake()
        {
            string folder = "Assets/Resources/" + SdfCreature.BakedFolder;
            Directory.CreateDirectory(folder);
            foreach ((AnimalType type, string key) in Species)
            {
                foreach (string old in Directory.GetFiles(folder, $"Sculpted_{key}_*.asset")) AssetDatabase.DeleteAsset(old.Replace('\\', '/')); // never load an old bake while rebuilding
                SdfCreature.ClearCache();
                GameObject temp = AnimalModelBuilder.CreateAnimalPrefab(type, "BakeTemp_" + key);
                Mesh mesh = SdfCreature.CachedMesh(key);
                if (mesh != null)
                {
                    string path = $"{folder}/{mesh.name}.asset"; // name carries the signature
                    Mesh copy = Object.Instantiate(mesh);
                    copy.name = mesh.name;
                    AssetDatabase.CreateAsset(copy, path);
                    Debug.Log($"[Battle of Ages] Baked {mesh.name}: {mesh.vertexCount} vertices -> {path}");
                }
                Object.DestroyImmediate(temp);
            }
            SdfCreature.ClearCache();
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
