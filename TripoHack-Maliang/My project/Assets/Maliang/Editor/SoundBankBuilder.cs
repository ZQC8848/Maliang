using System.Collections.Generic;
using System.IO;
using Maliang.Core;
using UnityEditor;
using UnityEngine;

namespace Maliang.EditorTools
{
    /// <summary>
    /// Builds Resources/SoundBank.asset from the WAVs in Audio/SFX (made by Tools/sfx/generate_sfx.py) and sets their
    /// import settings: mono for the 3D sounds (they are placed in the room), stereo for the ambience; short sounds
    /// decompressed on load, loops kept compressed in memory. The mix volumes below balance the set.
    /// </summary>
    public static class SoundBankBuilder
    {
        const string SfxDir = "Assets/Maliang/Audio/SFX";
        const string BankPath = "Assets/Maliang/Resources/SoundBank.asset";

        static readonly Dictionary<string, float> Mix = new Dictionary<string, float>
        {
            [SfxId.ScrollRoll] = 0.5f,
            [SfxId.BrushTouch] = 0.35f,
            [SfxId.BrushStrokeLoop] = 0.45f,
            [SfxId.InkDip] = 0.6f,
            [SfxId.SealStamp] = 0.9f,
            [SfxId.ScrollRise] = 0.6f,
            [SfxId.Ignite] = 0.8f,
            [SfxId.BurnLoop] = 0.8f,
            [SfxId.EmberPop] = 0.5f,
            [SfxId.RodFall] = 0.7f,
            [SfxId.Fizzle] = 0.8f,
            [SfxId.Deflate] = 0.7f,
            [SfxId.ScrollThud] = 0.8f,
            [SfxId.Crumble] = 0.6f,
            [SfxId.Materialize] = 0.8f,
            [SfxId.DrawerSlideLoop] = 0.5f,
            [SfxId.DrawerKnock] = 0.6f,
            [SfxId.DeskAmbience] = 0.35f,
        };

        [MenuItem("Maliang/Build/Sound Bank")]
        public static void Build()
        {
            var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(BankPath);
            if (bank == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Maliang/Resources")) AssetDatabase.CreateFolder("Assets/Maliang", "Resources");
                bank = ScriptableObject.CreateInstance<SoundBank>();
                AssetDatabase.CreateAsset(bank, BankPath);
            }
            bank.sounds.Clear();
            foreach (var path in Directory.GetFiles(SfxDir, "*.wav"))
            {
                string assetPath = path.Replace('\\', '/');
                string id = Path.GetFileNameWithoutExtension(assetPath);
                Import(assetPath, id);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
                bank.sounds.Add(new SoundBank.Entry { id = id, clip = clip, volume = Mix.TryGetValue(id, out var v) ? v : 0.7f });
            }
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Sfx] Sound bank: {bank.sounds.Count} sounds -> {BankPath}");
        }

        static void Import(string path, string id)
        {
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            bool ambience = id == SfxId.DeskAmbience;
            bool loop = id.EndsWith("_loop") || ambience;
            importer.forceToMono = !ambience;
            importer.loadInBackground = ambience;
            var s = importer.defaultSampleSettings;
            s.loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;
            importer.defaultSampleSettings = s;
            importer.SaveAndReimport();
        }
    }
}
