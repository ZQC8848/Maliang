using System;
using System.IO;
using UnityEngine;

namespace Maliang.Core
{
    [Serializable] public class WorldLabsConfig { public string apiKey = ""; public string model = "marble-1.1"; public string splat = "full_res"; }
    [Serializable] public class TripoConfig { public string apiKey = ""; }
    [Serializable] public class VisionConfig { public string provider = "openai"; public string apiKey = ""; public string model = ""; }
    [Serializable] public class ImageGenConfig { public bool enabled = true; }
    [Serializable]
    public class LimitsConfig
    {
        public int maxGenerationsPerSession = 6;
        public int worldTimeoutSec = 600;
        public int objectTimeoutSec = 240;
    }
    [Serializable] public class FallbackConfig { public bool enabled = true; public bool fastMode = false; }

    /// <summary>
    /// Runtime configuration loaded from maliang.config.json (see TechPlan §10.4).
    /// The file holds API keys, so it is git-ignored and shipped next to the executable.
    /// </summary>
    [Serializable]
    public class MaliangConfig
    {
        public const string FileName = "maliang.config.json";

        /// <summary>"online" calls real APIs; "offline" always uses the fallback library.</summary>
        public string mode = "offline";
        public WorldLabsConfig worldLabs = new WorldLabsConfig();
        public TripoConfig tripo = new TripoConfig();
        public VisionConfig vision = new VisionConfig();
        public ImageGenConfig imageGen = new ImageGenConfig();
        public LimitsConfig limits = new LimitsConfig();
        public FallbackConfig fallback = new FallbackConfig();

        public bool IsOnline => string.Equals(mode, "online", StringComparison.OrdinalIgnoreCase);
        public bool HasObjectKeys => !string.IsNullOrWhiteSpace(tripo.apiKey) && !string.IsNullOrWhiteSpace(vision.apiKey);
        public bool HasWorldKeys => !string.IsNullOrWhiteSpace(worldLabs.apiKey) && !string.IsNullOrWhiteSpace(vision.apiKey);

        static MaliangConfig _current;

        /// <summary>Loaded lazily; a missing or broken file yields defaults (offline, fallback on).</summary>
        public static MaliangConfig Current => _current ??= Load();

        public static void Reload() => _current = Load();

        /// <summary>Editor: project root. Player: folder containing the executable.</summary>
        public static string PrimaryPath =>
            Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), FileName);

        public static string SecondaryPath => Path.Combine(Application.persistentDataPath, FileName);

        static MaliangConfig Load()
        {
            var config = new MaliangConfig();
            foreach (var path in new[] { PrimaryPath, SecondaryPath })
            {
                if (!File.Exists(path)) continue;
                try
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(path), config);
                    MaliangLog.Info("Config", $"Loaded {path} (mode={config.mode})");
                    config.RegisterSecrets();
                    return config;
                }
                catch (Exception e)
                {
                    MaliangLog.Warn("Config", $"Failed to parse {path}: {e.Message}");
                }
            }

            MaliangLog.Warn("Config", $"No {FileName} found; running offline with fallback content.");
            return config;
        }

        void RegisterSecrets()
        {
            MaliangLog.AddSecret(worldLabs.apiKey);
            MaliangLog.AddSecret(tripo.apiKey);
            MaliangLog.AddSecret(vision.apiKey);
        }
    }
}
