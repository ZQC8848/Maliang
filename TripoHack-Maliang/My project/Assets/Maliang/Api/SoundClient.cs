using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Maliang.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Maliang.Api
{
    /// <summary>
    /// ElevenLabs sound effects (Phase3Design 5.4): text to MP3, and runtime MP3 loading into an AudioClip.
    /// Sound never blocks the pipeline; callers treat any error as "silent".
    /// </summary>
    public class SoundClient
    {
        const string Endpoint = "https://api.elevenlabs.io/v1/sound-generation?output_format=mp3_44100_128";
        readonly MaliangConfig _config;

        public SoundClient(MaliangConfig config) => _config = config;

        public bool Enabled => _config.sound.enabled && !string.IsNullOrWhiteSpace(_config.sound.apiKey);

        public async Task<byte[]> GenerateAsync(SoundPlan plan, CancellationToken cancel = default)
        {
            var body = new JObject
            {
                ["text"] = plan.Prompt,
                ["duration_seconds"] = plan.DurationS ?? 3f,
                ["loop"] = plan.Loop,
                ["prompt_influence"] = 0.5f,
                ["model_id"] = "eleven_text_to_sound_v2",
            };
            var headers = new Dictionary<string, string> { ["xi-api-key"] = _config.sound.apiKey };
            var started = Time.realtimeSinceStartup;
            var mp3 = await Http.PostJsonAsync(Endpoint, headers, body.ToString(Formatting.None), 60, cancel);
            MaliangLog.Info("Sound", $"{mp3.Length / 1024} KB in {Time.realtimeSinceStartup - started:F1}s ({plan.Kind}): {plan.Prompt}");
            return mp3;
        }

        /// <summary>Decodes an MP3 file into an AudioClip (fully loaded, so it can loop and play instantly).</summary>
        public static async Task<AudioClip> LoadClipAsync(string path, CancellationToken cancel = default)
        {
            using var req = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(path).AbsoluteUri, AudioType.MPEG);
            ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = false;
            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                cancel.ThrowIfCancellationRequested();
                await Task.Yield();
            }
            if (req.result != UnityWebRequest.Result.Success)
            {
                MaliangLog.Warn("Sound", $"Could not decode {path}: {req.error}");
                return null;
            }
            var clip = DownloadHandlerAudioClip.GetContent(req);
            clip.name = System.IO.Path.GetFileNameWithoutExtension(path);
            return clip;
        }
    }
}
