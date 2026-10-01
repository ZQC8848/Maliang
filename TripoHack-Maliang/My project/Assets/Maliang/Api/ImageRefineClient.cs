using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Maliang.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Maliang.Api
{
    /// <summary>
    /// Redraws the ink drawing as a clean reference image for 3D generation (Phase3Design 5.1) with an OpenAI image
    /// edit. The caller skips this step on any error and sends the raw drawing to Tripo instead.
    /// </summary>
    public class ImageRefineClient
    {
        const string Endpoint = "https://api.openai.com/v1/images/edits";
        readonly MaliangConfig _config;

        public ImageRefineClient(MaliangConfig config) => _config = config;

        public bool Enabled => _config.imageGen.enabled;

        public async Task<byte[]> RefineAsync(byte[] inkPng, string prompt, CancellationToken cancel = default)
        {
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("image", inkPng, "ink.png", "image/png"),
                new MultipartFormDataSection("model", _config.imageGen.model),
                new MultipartFormDataSection("prompt", prompt),
                new MultipartFormDataSection("size", "1024x1024"),
                new MultipartFormDataSection("quality", "high"),
            };
            var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + _config.vision.apiKey };
            var started = Time.realtimeSinceStartup;
            var reply = JObject.Parse(System.Text.Encoding.UTF8.GetString(await Http.PostFormAsync(Endpoint, headers, form, 180, cancel)));
            string b64 = (string)reply["data"]?[0]?["b64_json"];
            if (string.IsNullOrEmpty(b64)) throw new ApiException(FailReason.Collapsed, "Image edit returned no image");
            MaliangLog.Info("Refine", $"Refined in {Time.realtimeSinceStartup - started:F1}s");
            return Convert.FromBase64String(b64);
        }
    }
}
