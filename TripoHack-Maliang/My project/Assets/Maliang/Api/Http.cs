using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Maliang.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Maliang.Api
{
    /// <summary>
    /// Shared HTTP helper for the agent clients: timeouts, a few retries with back-off for transient errors
    /// (network, 5xx, 429), and classification of final errors into <see cref="FailReason"/>. Runs on the main thread
    /// (UnityWebRequest), awaited from async code.
    /// </summary>
    public static class Http
    {
        public const int DefaultTimeoutSec = 120;
        const int MaxAttempts = 3;

        public static Task<byte[]> PostJsonAsync(string url, Dictionary<string, string> headers, string json,
            int timeoutSec = DefaultTimeoutSec, CancellationToken cancel = default) =>
            SendAsync(() =>
            {
                var req = new UnityWebRequest(url, "POST")
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                    downloadHandler = new DownloadHandlerBuffer(),
                };
                req.SetRequestHeader("Content-Type", "application/json");
                return req;
            }, headers, timeoutSec, cancel);

        public static Task<byte[]> PostFormAsync(string url, Dictionary<string, string> headers, List<IMultipartFormSection> form,
            int timeoutSec = DefaultTimeoutSec, CancellationToken cancel = default) =>
            SendAsync(() => UnityWebRequest.Post(url, form), headers, timeoutSec, cancel);

        public static Task<byte[]> GetAsync(string url, Dictionary<string, string> headers = null,
            int timeoutSec = DefaultTimeoutSec, CancellationToken cancel = default) =>
            SendAsync(() => UnityWebRequest.Get(url), headers, timeoutSec, cancel);

        public static async Task<string> PostJsonTextAsync(string url, Dictionary<string, string> headers, string json,
            int timeoutSec = DefaultTimeoutSec, CancellationToken cancel = default) =>
            Encoding.UTF8.GetString(await PostJsonAsync(url, headers, json, timeoutSec, cancel));

        static async Task<byte[]> SendAsync(System.Func<UnityWebRequest> make, Dictionary<string, string> headers,
            int timeoutSec, CancellationToken cancel)
        {
            for (int attempt = 1; ; attempt++)
            {
                cancel.ThrowIfCancellationRequested();
                using var req = make();
                req.timeout = timeoutSec;
                if (headers != null)
                    foreach (var h in headers) req.SetRequestHeader(h.Key, h.Value);

                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    if (cancel.IsCancellationRequested) { req.Abort(); cancel.ThrowIfCancellationRequested(); }
                    await Task.Yield();
                }

                long status = req.responseCode;
                if (req.result == UnityWebRequest.Result.Success) return req.downloadHandler.data;

                string body = req.downloadHandler?.text ?? "";
                bool transient = req.result == UnityWebRequest.Result.ConnectionError || status == 429 || status >= 500;
                string what = $"{req.method} {Redact(req.url)} -> {status} {req.error} {Trim(body)}";
                if (transient && attempt < MaxAttempts)
                {
                    float wait = Mathf.Pow(2f, attempt);  // 2 s, 4 s
                    MaliangLog.Warn("Http", $"{what}; retry {attempt}/{MaxAttempts - 1} in {wait:F0}s");
                    await Task.Delay((int)(wait * 1000), cancel);
                    continue;
                }
                throw new ApiException(Classify(req.result, status, body), what, status);
            }
        }

        /// <summary>Maps a final HTTP failure to the reason shown to the player.</summary>
        public static FailReason Classify(UnityWebRequest.Result result, long status, string body)
        {
            if (result == UnityWebRequest.Result.ConnectionError || status == 0 || status == 408 || status == 504)
                return FailReason.Unreachable;
            if (status == 401 || status == 402 || status == 403 || status == 429) return FailReason.Exhausted;
            string b = body.ToLowerInvariant();
            if (b.Contains("moderation") || b.Contains("safety") || b.Contains("content_policy") || b.Contains("banned"))
                return FailReason.Forbidden;
            return status >= 500 ? FailReason.Unreachable : FailReason.Collapsed;
        }

        static string Trim(string s) => s.Length > 300 ? s.Substring(0, 300) + "..." : s;

        static string Redact(string url)
        {
            int q = url.IndexOf('?');
            return q < 0 ? url : url.Substring(0, q);
        }
    }
}
