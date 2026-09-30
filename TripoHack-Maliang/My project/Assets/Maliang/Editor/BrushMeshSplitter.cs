using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Maliang.EditorTools
{
    /// <summary>
    /// Splits the Node_Brush pen mesh (one submesh, one material) into two submeshes so the bristles can take the ink colour:
    ///   submesh 0 = handle, submesh 1 = bristles.
    /// A triangle belongs to the bristles when all its vertices lie within <c>hairLength</c> of the tip, measured along the
    /// mesh's long axis in bind pose (the tip is at z = 0). Bones, weights and bind poses are copied unchanged, so the
    /// bristle animation keeps working. The mesh only has vertex rings at about 0 / 1 / 2 / 4 / 7 cm, so the cut snaps to those.
    /// </summary>
    public static class BrushMeshSplitter
    {
        public const string SourceFbx = "Assets/Maliang/Art/NodeBrush/Animations/pen.FBX";
        public const string OutputPath = "Assets/Maliang/Art/Brush/BrushSplit.asset";

        public static Mesh Split(float hairLength)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourceFbx);
            var src = prefab.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;

            var mesh = Object.Instantiate(src); // keeps bind poses, bone weights, UVs, normals
            mesh.name = "BrushSplit";

            var verts = src.vertices;
            var tris = src.triangles;
            var handle = new List<int>(tris.Length);
            var hair = new List<int>(tris.Length / 3);
            const float epsilon = 0.002f;
            for (int i = 0; i < tris.Length; i += 3)
            {
                float maxZ = Mathf.Max(verts[tris[i]].z, Mathf.Max(verts[tris[i + 1]].z, verts[tris[i + 2]].z));
                var list = maxZ <= hairLength + epsilon ? hair : handle;
                list.Add(tris[i]); list.Add(tris[i + 1]); list.Add(tris[i + 2]);
            }

            mesh.subMeshCount = 2;
            mesh.SetTriangles(handle, 0);
            mesh.SetTriangles(hair, 1);
            mesh.RecalculateBounds();

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(OutputPath);
            if (existing != null)
            {
                // Overwrite in place so scene references (GUID) survive a re-split.
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                mesh = existing;
                EditorUtility.SetDirty(mesh);
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, OutputPath);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[BrushMeshSplitter] hair length {hairLength * 100f:F1} cm: {hair.Count / 3} bristle / {handle.Count / 3} handle triangles → {OutputPath}");
            return mesh;
        }
    }
}
