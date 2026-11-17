#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using BattleOfAges.Utils;

namespace BattleOfAges.EditorTools
{
    /// <summary>
    /// Blender models (docs/MODELS.md): FBX files in Assets/Models/&lt;Category&gt;/ are imported with fixed settings
    /// (axes baked, readable, hierarchy kept, no animation, cameras or lights) and converted by ImportedModelConverter into
    /// Resources/Models/&lt;Category&gt;/&lt;Name&gt;.asset (an ImportedModel with its meshes inside; the asset keeps its GUID
    /// when converted again). A changed FBX is converted after its import; ConvertAll does every one and writes
    /// Logs/ModelImport.txt (also for -executeMethod).
    /// </summary>
    public sealed class BlenderMeshImportTool : AssetPostprocessor
    {
        private const string SourceRoot = "Assets/Models/";
        private const string ReportPath = "Logs/ModelImport.txt";

        /// <summary>The category of a model path (Assets/Models/Weapons/X.fbx → Weapons), or null if it is not one.</summary>
        public static string CategoryOf(string assetPath)
        {
            if (!assetPath.StartsWith(SourceRoot, StringComparison.Ordinal) || !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return null;
            string[] parts = assetPath.Substring(SourceRoot.Length).Split('/');
            if (parts.Length != 2) return null;
            return Array.IndexOf(ImportedModelConverter.Categories, parts[0]) >= 0 ? parts[0] : null;
        }

        public static string OutputPathFor(string assetPath) =>
            $"Assets/Resources/{ImportedModel.ResourcesFolder}/{CategoryOf(assetPath)}/{Path.GetFileNameWithoutExtension(assetPath)}.asset";

        public override uint GetVersion() => 1;

        private void OnPreprocessModel()
        {
            if (CategoryOf(assetPath) == null) return;
            var importer = (ModelImporter)assetImporter;
            // The FBX is exported in Blender's own axes (Z up, docs/MODELS.md §3) and Unity bakes the conversion into every
            // node: Unity = (x, z, y) of Blender, nested parts and sockets right. (Blender's "Apply Transform" moved
            // sockets on turned parts; Y-up export left a -90° on the root.)
            importer.bakeAxisConversion = true;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.isReadable = true;
            importer.preserveHierarchy = true; // the root stays <Model>, parts and sockets under it
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription; // names and base colours
            importer.addCollider = false;
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var changed = new List<string>();
            foreach (string p in imported) if (CategoryOf(p) != null) changed.Add(p);
            foreach (string p in moved) if (CategoryOf(p) != null) changed.Add(p);
            if (changed.Count == 0) return;
            EditorApplication.delayCall += () =>
            {
                var report = new List<string>();
                foreach (string p in changed) Convert(p, report);
                Finish(report);
            };
        }

        [MenuItem("Battle of Ages/Models/Convert Blender Models")]
        public static void ConvertAll()
        {
            AssetDatabase.Refresh();
            var report = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Models" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (CategoryOf(path) != null) Convert(path, report);
            }
            if (report.Count == 0) report.Add("No models in Assets/Models/<" + string.Join("|", ImportedModelConverter.Categories) + ">/.");
            Finish(report);
        }

        /// <summary>Converts one FBX and saves (or updates) its ImportedModel asset; null if the file is not a model path.</summary>
        public static ImportedModel Convert(string fbxPath, List<string> report)
        {
            string category = CategoryOf(fbxPath);
            if (category == null) return null;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (source == null) { report.Add($"{fbxPath}: not loaded"); return null; }
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer != null && (!importer.isReadable || !importer.bakeAxisConversion || !importer.preserveHierarchy))
            {
                importer.SaveAndReimport(); // imported before this tool existed
                source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            }

            GameObject instance = UnityEngine.Object.Instantiate(source);
            instance.name = source.name;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            ImportedModel model;
            try { model = ImportedModelConverter.Convert(instance, category, report, ImportedModelConverter.BlenderToGame); }
            finally { UnityEngine.Object.DestroyImmediate(instance); }

            string outPath = OutputPathFor(fbxPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            var existing = AssetDatabase.LoadAssetAtPath<ImportedModel>(outPath);
            ImportedModel target = existing;
            if (existing == null)
            {
                AssetDatabase.CreateAsset(model, outPath);
                target = model;
            }
            else
            {
                foreach (UnityEngine.Object sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(outPath))
                    if (sub is Mesh) AssetDatabase.RemoveObjectFromAsset(sub);
                EditorUtility.CopySerialized(model, existing);
                existing.name = Path.GetFileNameWithoutExtension(outPath);
            }
            foreach (ImportedModel.Part p in target.Parts)
                if (p.Mesh != null) AssetDatabase.AddObjectToAsset(p.Mesh, target);
            if (target.Far != null) AssetDatabase.AddObjectToAsset(target.Far, target);
            EditorUtility.SetDirty(target);
            if (existing != null) UnityEngine.Object.DestroyImmediate(model);
            report.Add($"  -> {outPath}");
            return target;
        }

        private static void Finish(List<string> report)
        {
            AssetDatabase.SaveAssets();
            string text = string.Join("\n", report) + "\n";
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, text);
            bool warn = report.Exists(l => l.StartsWith("  !", StringComparison.Ordinal));
            if (warn) Debug.LogWarning("[Battle of Ages] Model import:\n" + text);
            else Debug.Log("[Battle of Ages] Model import:\n" + text);
        }
    }
}
#endif
