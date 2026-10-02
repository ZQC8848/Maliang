using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GLTFast;
using Maliang.Core;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maliang.EditorTools
{
    /// <summary>
    /// The Windows build (Phase 8). Two steps, both under Maliang/Build:
    /// <list type="number">
    /// <item><b>Collect glTF Shader Variants</b>: models are loaded at runtime (glTFast), so no material in the build
    /// references the glTF shader graphs and the player would draw them pink. This loads every saved model once
    /// (bundled works and the player's own library), records the shader keywords their materials use into a
    /// shader variant collection and adds it to the preloaded shaders, so exactly those variants are built.</item>
    /// <item><b>Windows Build</b>: the desk scene to Build/Maliang/Maliang.exe (next to the repository), with a
    /// key-less maliang.config.json beside the exe (offline: the bundled works stand in). Put a config with demo
    /// keys there to summon for real; it is read from the exe's folder.</item>
    /// </list>
    /// </summary>
    public static class WindowsBuild
    {
        const string VariantsPath = "Assets/Maliang/Settings/GltfShaderVariants.shadervariants";
        const string DeskScene = "Assets/Maliang/Scenes/M1_Desk.unity";

        public static string OutputDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "Build", "Maliang"));

        [MenuItem("Maliang/Build/Collect glTF Shader Variants")]
        public static async void CollectGltfVariants()
        {
            var files = new List<string>();
            foreach (var root in new[] { Path.Combine(Application.streamingAssetsPath, "Library"), Path.Combine(Application.persistentDataPath, "Library") })
                if (Directory.Exists(root))
                    files.AddRange(Directory.GetFiles(root, "*.glb", SearchOption.AllDirectories)
                        .Where(f => !f.Contains(Path.DirectorySeparatorChar + "_pending") && !f.Contains(Path.DirectorySeparatorChar + "_work")));

            var variants = new HashSet<(Shader shader, string keywords)>();
            foreach (var file in files)
            {
                var gltf = new GltfImport(null, new UninterruptedDeferAgent()); // the default agent needs play mode
                if (!await gltf.Load(file)) { Debug.LogWarning($"[Build] Could not load {file}"); continue; }
                for (int i = 0; i < gltf.MaterialCount; i++)
                {
                    var m = gltf.GetMaterial(i);
                    if (m == null || m.shader == null) continue;
                    variants.Add((m.shader, string.Join(" ", m.shaderKeywords.OrderBy(k => k))));
                }
                gltf.Dispose();
            }

            var collection = new ShaderVariantCollection();
            int added = 0;
            foreach (var (shader, keywords) in variants)
            {
                var kw = keywords.Length > 0 ? keywords.Split(' ') : new string[0];
                foreach (var pass in new[] { PassType.ScriptableRenderPipeline, PassType.ScriptableRenderPipelineDefaultUnlit })
                {
                    try
                    {
                        if (collection.Add(new ShaderVariantCollection.ShaderVariant(shader, pass, kw))) added++;
                    }
                    catch (System.ArgumentException) { /* this pass type does not exist in the shader */ }
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(VariantsPath));
            AssetDatabase.DeleteAsset(VariantsPath);
            AssetDatabase.CreateAsset(collection, VariantsPath);
            AddToPreloaded(AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(VariantsPath));
            AssetDatabase.SaveAssets();
            Debug.Log($"[Build] {files.Count} models, {variants.Count} material variants ({string.Join(", ", variants.Select(v => v.shader.name).Distinct())}), " +
                      $"{added} shader variants -> {VariantsPath} (preloaded)");
        }

        static void AddToPreloaded(ShaderVariantCollection collection)
        {
            var settings = new SerializedObject(AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset"));
            var list = settings.FindProperty("m_PreloadedShaders");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == collection) return;
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = collection;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        const string Readme =
@"Maliang (马良)  VR ritual: draw, seal, burn, and your drawing comes alive.

REQUIREMENTS
- Windows 10/11 64-bit, a GPU with Direct3D 12 or Vulkan.
- A PC VR headset streaming through an OpenXR runtime (PICO Connect, SteamVR or Meta Horizon Link),
  set as the active OpenXR runtime.

RUN
1. Connect the headset first (PICO Connect / SteamVR / Link running).
2. Start Maliang.exe.
   If it closes right away, start ""Maliang (D3D12).bat"" instead (some runtimes fail on Vulkan).

CONFIG (maliang.config.json, next to the exe)
- As shipped it has no API keys: the ritual runs offline and bundled works come in place of generation.
- To summon for real, fill in apiKey for worldLabs, tripo, vision (OpenAI) and sound (ElevenLabs).
  Keep that file private; keys are never part of the build.

IN VR
- Grip: pick up the brush, a seal, the candle, a scroll, or pull a drawer.
- Paint on the scroll, dip the brush in a colour, A / X: undo a stroke.
- Press a seal on the paper: Wu (物) summons an object, Jing (境) a world. The scroll rises.
- Hold the candle flame to the floating scroll until it catches.
- Drawers keep every work as a scroll: lay one on the desk to burn it again. The sky scroll clears the world.
- Left menu button: a new scroll.

CHECK WITHOUT A HEADSET
  Maliang.exe -selftest -force-d3d12
  Replays a bundled object and world, saves selftest_object.png and selftest_world.png here, then quits.

Do not ship the folder Maliang_BackUpThisFolder_ButDontShipItWithYourGame (debug symbols).
";

        [MenuItem("Maliang/Build/Windows Build")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(VariantsPath) == null)
                Debug.LogWarning("[Build] No glTF shader variants collected yet (Maliang/Build/Collect glTF Shader Variants): models may draw pink");

            Directory.CreateDirectory(OutputDir);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { DeskScene },
                locationPathName = Path.Combine(OutputDir, "Maliang.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            if (s.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[Build] {s.result}: {s.totalErrors} errors");
                return;
            }

            // A key-less config beside the exe: offline, the bundled works stand in. Never copy the developer's keys here.
            string example = Path.Combine(Application.streamingAssetsPath, "maliang.config.example.json");
            string config = Path.Combine(OutputDir, MaliangConfig.FileName);
            if (!File.Exists(config) && File.Exists(example)) File.Copy(example, config);
            // Some OpenXR runtimes crash on Vulkan when no headset is ready (seen with Meta Horizon); D3D12 starts idle instead.
            File.WriteAllText(Path.Combine(OutputDir, "Maliang (D3D12).bat"), "@echo off\r\nstart \"\" \"%~dp0Maliang.exe\" -force-d3d12\r\n");
            File.WriteAllText(Path.Combine(OutputDir, "README.txt"), Readme.Replace("\n", "\r\n"));
            foreach (var shot in Directory.GetFiles(OutputDir, "selftest_*.png")) File.Delete(shot);
            string staleExample = Path.Combine(OutputDir, "Maliang_Data", "StreamingAssets", "maliang.config.example.json");
            Debug.Log($"[Build] Succeeded in {s.totalTime.TotalSeconds:F0}s, {s.totalSize / 1048576f:F0} MB -> {options.locationPathName}" +
                      $"{(File.Exists(staleExample) ? " (example config also in StreamingAssets)" : "")}");
        }
    }
}
