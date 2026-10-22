using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BattleOfAges.EditorTools
{
    /// <summary>
    /// Builds the game for Windows (64-bit) or macOS (Intel + Apple Silicon, Mono) from the scenes in Build Settings, into
    /// BUILD_DIR (or Builds/Windows, Builds/macOS). Batch use: -executeMethod BattleOfAges.EditorTools.BuildGameTool.BuildWindows
    /// (or .BuildMac; needs the Mac Build Support module). A Mac build made on Windows is unsigned and loses the
    /// executable bit in a zip: on the Mac it needs xattr -cr and chmod +x (see the readme next to the app).
    /// </summary>
    public static class BuildGameTool
    {
        [MenuItem("Battle of Ages/Build Windows")]
        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, "Windows", "BattleOfAges.exe");
        }

        [MenuItem("Battle of Ages/Build macOS")]
        public static void BuildMac()
        {
            // One app for Intel and Apple Silicon Macs
            EditorUserBuildSettings.SetPlatformSettings(BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneOSX), "Architecture", "x64ARM64");
            Build(BuildTarget.StandaloneOSX, "macOS", "BattleOfAges.app");
        }

        private static void Build(BuildTarget target, string folder, string file)
        {
            string dir = Environment.GetEnvironmentVariable("BUILD_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Directory.GetCurrentDirectory(), "Builds", folder);
            Directory.CreateDirectory(dir);

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(dir, file),
                target = target,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary s = report.summary;
            Debug.Log($"[Battle of Ages] Build {s.result}: {s.outputPath}, {s.totalSize / (1024f * 1024f):0} MB, {s.totalTime.TotalSeconds:0} s, errors {s.totalErrors}");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
