using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Maliang.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Maliang.Api
{
    /// <summary>
    /// World Labs Marble API (checked against docs.worldlabs.ai and its OpenAPI, 2026-10-01): upload the reference
    /// image as a media asset (signed URL), generate a world from it plus a text prompt, poll the operation, and read
    /// the world's splat URLs and scale metadata. Header <c>WLT-Api-Key</c>. Operation polling and uploads are free;
    /// a marble-1.1 world from an image costs 1,580 credits, a draft (marble-1.0-draft) 230.
    /// </summary>
    public class WorldLabsClient
    {
        const string Api = "https://api.worldlabs.ai/marble/v1";
        readonly MaliangConfig _config;

        public WorldLabsClient(MaliangConfig config) => _config = config;

        Dictionary<string, string> Auth => new Dictionary<string, string> { ["WLT-Api-Key"] = _config.worldLabs.apiKey };

        /// <summary>The model for this run: the cheap draft while developing, the full model for the demo.</summary>
        public string Model => _config.worldLabs.draft ? "marble-1.0-draft" : _config.worldLabs.model;

        /// <summary>Uploads a PNG; returns its media asset id.</summary>
        public async Task<string> UploadAsync(byte[] png, string fileName, CancellationToken cancel = default)
        {
            var body = new JObject { ["file_name"] = fileName, ["kind"] = "image", ["extension"] = "png" };
            var reply = await PostAsync("/media-assets:prepare_upload", body, cancel);
            string id = (string)reply["media_asset"]?["media_asset_id"];
            var info = reply["upload_info"];
            string url = (string)info?["upload_url"];
            if (id == null || url == null) throw new ApiException(FailReason.Collapsed, "World Labs upload: no upload URL");
            var headers = new Dictionary<string, string>();
            if (info["required_headers"] is JObject required)
                foreach (var h in required) headers[h.Key] = (string)h.Value;
            await Http.PutAsync(url, headers, png, 120, cancel);
            MaliangLog.Info("WorldLabs", $"Uploaded {fileName} ({png.Length / 1024} KB) as {id}");
            return id;
        }

        /// <summary>Starts a world from an uploaded image and a text prompt; returns the operation id.</summary>
        public async Task<string> GenerateAsync(string mediaAssetId, string textPrompt, string displayName, CancellationToken cancel = default)
        {
            var prompt = new JObject
            {
                ["type"] = "image",
                ["image_prompt"] = new JObject { ["source"] = "media_asset", ["media_asset_id"] = mediaAssetId },
            };
            if (!string.IsNullOrWhiteSpace(textPrompt)) prompt["text_prompt"] = textPrompt;
            var body = new JObject
            {
                ["display_name"] = Clip(displayName, 64),
                ["model"] = Model,
                ["world_prompt"] = prompt,
                ["tags"] = new JArray("maliang"),
            };
            var reply = await PostAsync("/worlds:generate", body, cancel);
            string op = (string)reply["operation_id"];
            if (op == null) throw new ApiException(FailReason.Collapsed, "World Labs generate: no operation id");
            MaliangLog.Info("WorldLabs", $"Generating ({Model}): operation {op}");
            return op;
        }

        /// <summary>
        /// Polls an operation until it is done; returns the world (<c>response</c>). <paramref name="progress"/> gets a
        /// rough 0..1 by elapsed time against <paramref name="expectedSec"/> (the API reports only a status).
        /// </summary>
        public async Task<JObject> WaitAsync(string operationId, Action<float> progress, float expectedSec, int timeoutSec,
            CancellationToken cancel = default)
        {
            float start = Time.realtimeSinceStartup;
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                var op = JObject.Parse(System.Text.Encoding.UTF8.GetString(await Http.GetAsync($"{Api}/operations/{operationId}", Auth, 60, cancel)));
                float elapsed = Time.realtimeSinceStartup - start;
                if ((bool?)op["done"] == true)
                {
                    if (op["error"] is JObject err && err.HasValues)
                        throw new ApiException(FailReason.Collapsed, $"World Labs generation failed: {err.ToString(Formatting.None)}");
                    MaliangLog.Info("WorldLabs", $"Operation {operationId} done in {elapsed:F0}s, cost {op["cost"]?["total_credits"]}");
                    return (JObject)op["response"];
                }
                progress?.Invoke(1f - Mathf.Exp(-elapsed / Mathf.Max(1f, expectedSec)));
                if (elapsed > timeoutSec) throw new ApiException(FailReason.Unreachable, $"World Labs operation {operationId} timed out after {elapsed:F0}s");
                await Task.Delay(4000, cancel);
            }
        }

        /// <summary>Picks the configured splat size from a world (falling back to the next one down).</summary>
        public string SpzUrl(JObject world, out string size)
        {
            var urls = world?["assets"]?["splats"]?["spz_urls"] as JObject;
            foreach (var s in new[] { _config.worldLabs.splat, "full_res", "500k", "100k" })
            {
                string url = (string)urls?[s];
                if (string.IsNullOrEmpty(url)) continue;
                size = s;
                return url;
            }
            size = null;
            return null;
        }

        public static async Task DownloadAsync(string url, string path, CancellationToken cancel = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, await Http.GetAsync(url, null, 600, cancel));
        }

        async Task<JObject> PostAsync(string path, JObject body, CancellationToken cancel) =>
            JObject.Parse(await Http.PostJsonTextAsync(Api + path, Auth, body.ToString(Formatting.None), 120, cancel));

        static string Clip(string s, int max) => string.IsNullOrEmpty(s) ? "Maliang world" : s.Length <= max ? s : s.Substring(0, max);
    }
}
