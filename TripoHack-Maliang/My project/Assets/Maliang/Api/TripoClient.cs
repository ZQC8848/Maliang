using System;
using System.Collections.Generic;
using System.IO;
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
    /// Tripo API v3 (Phase3Design 5.2 and 5.3): upload, image-to-model, task polling, download, and the rig chain
    /// (rig-check, rig, retarget). Output URLs expire after about five minutes, so callers download at once.
    /// </summary>
    public class TripoClient
    {
        const string Api = "https://openapi.tripo3d.ai/v3";
        const float PollSeconds = 3f;
        readonly MaliangConfig _config;

        public TripoClient(MaliangConfig config) => _config = config;

        Dictionary<string, string> Auth => new Dictionary<string, string> { ["Authorization"] = "Bearer " + _config.tripo.apiKey };

        // ------------------------------------------------------------------ generation

        public async Task<string> UploadAsync(byte[] png, string fileName, CancellationToken cancel = default)
        {
            var form = new List<IMultipartFormSection> { new MultipartFormFileSection("file", png, fileName, "image/png") };
            var data = Data(await Http.PostFormAsync(Api + "/files", Auth, form, 120, cancel), "upload");
            return (string)data["file_token"];
        }

        /// <summary>Starts image-to-model. <paramref name="faceLimit"/> &lt;= 0 leaves the model's default.</summary>
        public async Task<string> GenerateAsync(string fileToken, string model, int faceLimit, CancellationToken cancel = default)
        {
            var body = new JObject { ["input"] = fileToken, ["model"] = model, ["texture"] = true, ["pbr"] = true };
            if (faceLimit > 0) body["face_limit"] = faceLimit;
            return await StartTaskAsync("/generation/image-to-model", body, "generate", cancel);
        }

        // ------------------------------------------------------------------ rig chain

        public async Task<(bool riggable, string rigType)> RigCheckAsync(string generateTask, CancellationToken cancel = default)
        {
            string id = await StartTaskAsync("/animations/rig-check", new JObject { ["input"] = generateTask }, "rig-check", cancel);
            var output = (await WaitAsync(id, null, 120, cancel))["output"] as JObject;
            return ((bool?)output?["riggable"] ?? false, (string)output?["rig_type"]);
        }

        public Task<string> RigAsync(string generateTask, string rigModel, string rigType, CancellationToken cancel = default) =>
            StartTaskAsync("/animations/rig", new JObject
            {
                ["input"] = generateTask, ["model"] = rigModel, ["rig_type"] = rigType, ["spec"] = "tripo", ["out_format"] = "glb",
            }, "rig", cancel);

        /// <summary>One preset per request: a multi-preset request returns a GLB with only the last clip.</summary>
        public Task<string> RetargetAsync(string rigTask, string preset, CancellationToken cancel = default) =>
            StartTaskAsync("/animations/retarget", new JObject
            {
                ["input"] = rigTask, ["animation"] = preset, ["out_format"] = "glb",
                ["bake_animation"] = true, ["animate_in_place"] = true,
            }, "retarget", cancel);

        // ------------------------------------------------------------------ tasks

        async Task<string> StartTaskAsync(string path, JObject body, string what, CancellationToken cancel)
        {
            var data = Data(await Http.PostJsonAsync(Api + path, Auth, body.ToString(Formatting.None), 60, cancel), what);
            string id = (string)data["task_id"];
            MaliangLog.Info("Tripo", $"{what}: task {id}");
            return id;
        }

        /// <summary>Polls until the task succeeds. <paramref name="progress"/> gets 0..1.</summary>
        public async Task<JObject> WaitAsync(string taskId, Action<float> progress, int timeoutSec, CancellationToken cancel = default)
        {
            float started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < timeoutSec)
            {
                var data = Data(await Http.GetAsync($"{Api}/tasks/{taskId}", Auth, 60, cancel), "poll");
                string status = (string)data["status"];
                progress?.Invoke(((float?)data["progress"] ?? 0f) / 100f);
                switch (status)
                {
                    case "success":
                        MaliangLog.Info("Tripo", $"task {taskId} done in {Time.realtimeSinceStartup - started:F0}s, credits {data["credits_consumed"]}");
                        return data;
                    case "banned":
                        throw new ApiException(FailReason.Forbidden, $"Tripo task {taskId} banned");
                    case "failed":
                    case "cancelled":
                    case "expired":
                    case "unknown":
                        throw new ApiException(FailReason.Collapsed, $"Tripo task {taskId} ended {status}");
                }
                await Task.Delay(TimeSpan.FromSeconds(PollSeconds), cancel);
            }
            throw new ApiException(FailReason.Unreachable, $"Tripo task {taskId} timed out after {timeoutSec}s");
        }

        public static string ModelUrl(JObject taskData) => (string)taskData["output"]?["model_url"];

        public static async Task DownloadAsync(string url, string path, CancellationToken cancel = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, await Http.GetAsync(url, null, 300, cancel));
        }

        static JObject Data(byte[] reply, string what)
        {
            var json = JObject.Parse(System.Text.Encoding.UTF8.GetString(reply));
            if (((int?)json["code"] ?? 0) != 0)
                throw new ApiException(FailReason.Collapsed, $"Tripo {what}: {json.ToString(Formatting.None)}");
            return (JObject)json["data"];
        }
    }
}
