using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace BattleOfAges.EditorTools
{
    /// <summary>
    /// Reads profiler files a development player wrote (BuildSmokeShot "-smokeprofile": *.raw beside the shots) and writes
    /// each one's main-thread tree as text beside it (total ms | self ms | calls, a frame), and the render thread's.
    /// Batch use: PROFILE_DIR=folder, -executeMethod BattleOfAges.EditorTools.ProfileDumpTool.Dump
    /// </summary>
    public static class ProfileDumpTool
    {
        public static void Dump()
        {
            string dir = Environment.GetEnvironmentVariable("PROFILE_DIR");
            foreach (string raw in Directory.GetFiles(dir, "*.raw"))
            {
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.LoadProfile(raw, false);
                var text = new StringBuilder();
                int first = ProfilerDriver.firstFrameIndex + 4, last = ProfilerDriver.lastFrameIndex - 2;
                text.AppendLine($"{Path.GetFileName(raw)}: frames {first}..{last}");
                for (int thread = 0; thread < 3; thread++) Tree(text, first, last, thread);
                File.WriteAllText(raw + ".txt", text.ToString());
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Tree(StringBuilder text, int first, int last, int thread)
        {
            var total = new Dictionary<string, float[]>();
            var order = new List<string>();
            int counted = 0;
            string threadName = null;
            for (int f = first; f <= last; f++)
            {
                using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(f, thread, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
                {
                    if (view == null || !view.valid) continue;
                    counted++;
                    threadName = view.threadName;
                    var kids = new List<int>();
                    void Walk(int id, string path, int depth)
                    {
                        view.GetItemChildren(id, kids);
                        foreach (int kid in kids.ToArray())
                        {
                            float ms = view.GetItemColumnDataAsFloat(kid, HierarchyFrameDataView.columnTotalTime);
                            if (ms < 0.02f) continue;
                            string p = path + "/" + view.GetItemName(kid);
                            if (!total.TryGetValue(p, out float[] v)) { total[p] = v = new float[4]; order.Add(p); }
                            v[0] += ms;
                            v[1] += view.GetItemColumnDataAsFloat(kid, HierarchyFrameDataView.columnSelfTime);
                            v[2] += view.GetItemColumnDataAsFloat(kid, HierarchyFrameDataView.columnCalls);
                            v[3] = depth;
                            if (depth < 14) Walk(kid, p, depth + 1);
                        }
                    }
                    Walk(view.GetRootItemID(), "", 0);
                }
            }
            if (counted == 0) return;
            text.AppendLine();
            text.AppendLine($"== thread {thread} ({threadName}), {counted} frames (total ms | self ms | calls, a frame)");
            order.Sort(string.CompareOrdinal);
            foreach (string p in order)
            {
                float[] v = total[p];
                if (v[0] / counted < 0.05f) continue;
                text.AppendLine($"{new string(' ', (int)v[3] * 2)}{p.Substring(p.LastIndexOf('/') + 1)}: {v[0] / counted:0.00} | {v[1] / counted:0.00} | {v[2] / counted:0}");
            }
        }
    }
}
