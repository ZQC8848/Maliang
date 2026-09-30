using System;
using System.IO;
using UnityEngine;

namespace Maliang.Core
{
    public enum LogLevel { Debug = 0, Info = 1, Warning = 2, Error = 3 }

    /// <summary>
    /// Thin logger: forwards to the Unity console and appends to maliang.log under persistentDataPath.
    /// API keys registered via <see cref="AddSecret"/> are masked in both outputs.
    /// </summary>
    public static class MaliangLog
    {
        public static LogLevel MinLevel = LogLevel.Info;
        public static bool WriteToFile = true;

        static readonly object Gate = new object();
        static readonly System.Collections.Generic.List<string> Secrets = new System.Collections.Generic.List<string>();
        static string _filePath;

        public static string FilePath => _filePath ??= Path.Combine(Application.persistentDataPath, "maliang.log");

        public static void AddSecret(string secret)
        {
            if (string.IsNullOrEmpty(secret) || secret.Length < 6) return;
            lock (Gate) if (!Secrets.Contains(secret)) Secrets.Add(secret);
        }

        public static void Debug(string tag, string message) => Write(LogLevel.Debug, tag, message);
        public static void Info(string tag, string message) => Write(LogLevel.Info, tag, message);
        public static void Warn(string tag, string message) => Write(LogLevel.Warning, tag, message);
        public static void Error(string tag, string message) => Write(LogLevel.Error, tag, message);

        public static void Error(string tag, Exception e) => Write(LogLevel.Error, tag, e.ToString());

        static void Write(LogLevel level, string tag, string message)
        {
            if (level < MinLevel) return;
            string line = Redact($"[{tag}] {message}");

            switch (level)
            {
                case LogLevel.Error: UnityEngine.Debug.LogError(line); break;
                case LogLevel.Warning: UnityEngine.Debug.LogWarning(line); break;
                default: UnityEngine.Debug.Log(line); break;
            }

            if (!WriteToFile) return;
            try
            {
                lock (Gate)
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-7} {line}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never break the ritual.
            }
        }

        static string Redact(string text)
        {
            lock (Gate)
                foreach (var s in Secrets)
                    text = text.Replace(s, "***");
            return text;
        }
    }
}
