using Maliang.Ritual;
using UnityEditor;
using UnityEngine;

namespace Maliang.EditorTools
{
    /// <summary>
    /// Sets up burning on the scroll (Phase 4): <see cref="ScrollBurn"/>, <see cref="BurnPacer"/> and the effect
    /// materials (flame and ember on the flame shader with occlusion, ash and smoke on the ash shader). Used by
    /// <see cref="DeskSceneBuilder"/> when it builds the scroll prefab, and by the menu item to add it to the existing
    /// prefab in place (keeps the scene's references).
    /// </summary>
    public static class ScrollBurnBuilder
    {
        const string MatDir = "Assets/Maliang/Art/Materials";
        const string ScrollPrefabPath = "Assets/Maliang/Prefabs/Scroll.prefab";

        public static ScrollBurn Add(GameObject root)
        {
            var ritual = root.GetComponent<ScrollRitual>();
            var burn = root.GetComponent<ScrollBurn>();
            if (burn == null) burn = root.AddComponent<ScrollBurn>();
            else
            {
                // Back to the code defaults, so tuning in ScrollBurn.cs reaches the prefab.
                var fresh = new GameObject("ScrollBurn defaults") { hideFlags = HideFlags.HideAndDontSave };
                EditorUtility.CopySerialized(fresh.AddComponent<ScrollBurn>(), burn);
                Object.DestroyImmediate(fresh);
            }
            burn.ritual = ritual;
            burn.canvas = ritual.canvas;
            burn.unroll = ritual.unroll;
            // The scroll burns against a bright sky: these flames also cover what is behind them (unlike the candle's).
            burn.flameMaterial = Mat("Burn Flame", "Maliang/Flame", ("_Shape", 0f), ("_Intensity", 1.7f), ("_Softness", 1.3f), ("_Occlusion", 0.85f));
            burn.emberMaterial = Mat("Burn Ember", "Maliang/Flame", ("_Shape", 1f), ("_Intensity", 2.2f), ("_Softness", 1.1f), ("_Occlusion", 0.6f));
            burn.ashMaterial = Mat("Burn Ash", "Maliang/Ash", ("_Softness", 0.35f), ("_Ragged", 0.25f));
            burn.smokeMaterial = Mat("Burn Smoke", "Maliang/Ash", ("_Softness", 1f), ("_Ragged", 0.06f));

            var pacer = root.GetComponent<BurnPacer>();
            if (pacer == null) pacer = root.AddComponent<BurnPacer>();
            else
            {
                var fresh = new GameObject("BurnPacer defaults") { hideFlags = HideFlags.HideAndDontSave };
                fresh.AddComponent<ScrollBurn>(); // BurnPacer requires it
                EditorUtility.CopySerialized(fresh.AddComponent<BurnPacer>(), pacer);
                Object.DestroyImmediate(fresh);
            }
            pacer.ritual = ritual;
            pacer.burn = burn;
            return burn;
        }

        static Material Mat(string name, string shaderName, params (string prop, float value)[] values)
        {
            string path = $"{MatDir}/{name}.mat";
            var shader = Shader.Find(shaderName);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader) mat.shader = shader;
            foreach (var (prop, value) in values) mat.SetFloat(prop, value);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        [MenuItem("Maliang/Build/Add Burning To Scroll Prefab")]
        public static void AddToPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(ScrollPrefabPath);
            try
            {
                Add(root);
                PrefabUtility.SaveAsPrefabAsset(root, ScrollPrefabPath);
                Debug.Log("[Burn] ScrollBurn added to " + ScrollPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
