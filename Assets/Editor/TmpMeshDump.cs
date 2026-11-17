#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Temporary: dumps the vertex data of a converted model (deleted after the check)
public static class TmpMeshDump
{
    public static void Dump()
    {
        var m = AssetDatabase.LoadAssetAtPath<BattleOfAges.Utils.ImportedModel>("Assets/Resources/Models/Weapons/ImportCheck.asset");
        var lines = new List<string>();
        foreach (var p in m.Parts)
        {
            var s = new List<Vector3>(); p.Mesh.GetUVs(2, s);
            Color[] c = p.Mesh.colors; Vector3[] v = p.Mesh.vertices; Vector3[] n = p.Mesh.normals;
            for (int i = 0; i < v.Length; i++) lines.Add($"{p.Name} v{v[i]} n{n[i]} c{c[i]} s{s[i]}");
        }
        var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Weapons/ImportCheck.fbx");
        foreach (var r in src.GetComponentsInChildren<MeshRenderer>())
        {
            var mf = r.GetComponent<MeshFilter>();
            string mats = string.Join(",", System.Array.ConvertAll(r.sharedMaterials, x => x.name + " " + x.shader.name + " base " + (x.HasProperty("_BaseColor") ? x.GetColor("_BaseColor").ToString() : "-")));
            lines.Add($"SRC {r.name} sub {mf.sharedMesh.subMeshCount} mats {mats}");
            for (int k = 0; k < mf.sharedMesh.subMeshCount; k++) lines.Add($"  sub{k} tris {mf.sharedMesh.GetTriangles(k).Length / 3}");
        }
        File.WriteAllLines("Logs/tmp_mesh_dump.txt", lines);
    }
}
#endif
