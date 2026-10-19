using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BattleOfAges.EditorTools
{
    /// <summary>
    /// Builds the game for Windows (64-bit) from the scenes in Build Settings, into BUILD_DIR (or Builds/Windows).
    /// Batch use: -executeMethod BattleOfAges.EditorTools.BuildGameTool.BuildWindows
    /// </summary>
    public static class BuildGameTool
    {
        [MenuItem("Battle of Ages/Build Windows")]
        public static void BuildWindows()
        {
            string dir = Environment.GetEnvironmentVariable("BUILD_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Directory.GetCurrentDirectory(), "Builds", "Windows");
            Directory.CreateDirectory(dir);

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(dir, "BattleOfAges.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary s = report.summary;
            Debug.Log($"[Battle of Ages] Build {s.result}: {s.outputPath}, {s.totalSize / (1024f * 1024f):0} MB, {s.totalTime.TotalSeconds:0} s, errors {s.totalErrors}");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
