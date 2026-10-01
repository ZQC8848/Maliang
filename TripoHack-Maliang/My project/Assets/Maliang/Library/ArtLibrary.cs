using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Maliang.Api;
using Maliang.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace Maliang.Library
{
    /// <summary>
    /// The player's works on disk (Phase3Design section 8): every successful summoning is kept whole under
    /// persistentDataPath/Library/&lt;id&gt;/ (drawing and seal layers, model and clips, sound, manifest) so it can be
    /// replayed from the drawer without any API. A work is written into _pending/ first and renamed into place in one
    /// step once complete (D21), so a crash never leaves half a work; failures are deleted.
    /// </summary>
    public static class ArtLibrary
    {
        public const int SchemaVersion = 1;
        const string Pending = "_pending";
        const string Work = "_work";

        public static string Root => Path.Combine(Application.persistentDataPath, "Library");
        static string IndexPath => Path.Combine(Root, "index.json");

        /// <summary>A new work has been committed (the drawer adds its scroll).</summary>
        public static event Action<LibraryEntry> Added;

        // ------------------------------------------------------------------ writing

        /// <summary>Starts a work for a sealed scroll: a pending folder for its files and a scratch folder for the agent.</summary>
        public static PendingWork BeginPending(SealType seal)
        {
            string id = $"{DateTime.Now:yyyy-MM-ddTHH-mm-ss}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";
            var work = new PendingWork(id, seal, Path.Combine(Root, Pending, id), Path.Combine(Root, Work, id));
            Directory.CreateDirectory(work.Dir);
            Directory.CreateDirectory(work.AgentDir);
            return work;
        }

        internal static LibraryEntry Commit(PendingWork work, ObjectJob job)
        {
            var plan = job.Plan;
            var entry = new LibraryEntry
            {
                schemaVersion = SchemaVersion,
                id = work.Id,
                createdAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                seal = work.Seal.ToString(),
                subject = plan?.Subject,
                category = plan?.Category,
                files = new LibraryFiles { ink = "ink.png", seal = "seal.png" },
                @object = new LibraryObject
                {
                    animated = job.Animated,
                    animations = plan?.Animate?.Wanted == true ? plan.Animate.Animations : new string[0],
                    sizeM = plan?.SizeM ?? 0.4f,
                    sound = plan?.Sound?.Wanted == true ? new LibrarySound { kind = plan.Sound.Kind, trigger = plan.Sound.Trigger } : null,
                },
            };

            // Copy what the work needs out of the agent's scratch folder, then move the folder into place in one step.
            Copy(job.ModelPath, work.Dir, entry.files.model = "model.glb");
            var clips = new List<string>();
            for (int i = 0; i < job.ExtraClipPaths.Length; i++)
            {
                string name = $"clip_{i + 1}.glb";
                Copy(job.ExtraClipPaths[i], work.Dir, name);
                clips.Add(name);
            }
            entry.files.clips = clips.ToArray();
            if (Copy(job.SoundPath, work.Dir, "sound.mp3")) entry.files.sound = "sound.mp3";
            if (Copy(Path.Combine(work.AgentDir, "refined.png"), work.Dir, "refined.png")) entry.files.refined = "refined.png";
            if (Copy(Path.Combine(work.AgentDir, "plan.json"), work.Dir, "plan.json")) entry.files.plan = "plan.json";
            WriteEntry(work.Dir, entry);

            string final = Path.Combine(Root, entry.id);
            Directory.Move(work.Dir, final);
            entry.Dir = final;
            AddToIndex(entry);
            MaliangLog.Info("Library", $"Saved \"{entry.subject}\" as {entry.id} ({(entry.@object.animated ? "animated" : "static")}" +
                                       $"{(entry.files.sound != null ? ", sound" : "")})");
            Added?.Invoke(entry);
            return entry;
        }

        /// <summary>A 「境」 work: the layers, the world's splats, its reference image and how to place it.</summary>
        internal static LibraryEntry CommitWorld(PendingWork work, WorldJob job)
        {
            var entry = new LibraryEntry
            {
                schemaVersion = SchemaVersion,
                id = work.Id,
                createdAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                seal = work.Seal.ToString(),
                subject = job.Plan?.Subject,
                category = "world",
                files = new LibraryFiles { ink = "ink.png", seal = "seal.png" },
                world = new LibraryWorld
                {
                    splatSize = job.SpzSize,
                    metricScale = job.MetricScale,
                    groundOffset = job.GroundOffset,
                    worldId = job.WorldId,
                    marbleUrl = job.MarbleUrl,
                    caption = job.Caption,
                    model = job.Model,
                },
            };
            Copy(job.SpzPath, work.Dir, entry.files.world = "world.spz");
            if (Copy(job.RefinedPath, work.Dir, "refined.png")) entry.files.refined = "refined.png";
            if (Copy(job.AmbiencePath, work.Dir, "ambience.mp3")) entry.files.ambience = "ambience.mp3";
            if (Copy(Path.Combine(work.AgentDir, "plan.json"), work.Dir, "plan.json")) entry.files.plan = "plan.json";
            WriteEntry(work.Dir, entry);

            string final = Path.Combine(Root, entry.id);
            Directory.Move(work.Dir, final);
            entry.Dir = final;
            AddToIndex(entry);
            MaliangLog.Info("Library", $"Saved world \"{entry.subject}\" as {entry.id} ({job.SpzSize}, " +
                                       $"{new FileInfo(Path.Combine(final, "world.spz")).Length / 1048576f:F1} MB)");
            Added?.Invoke(entry);
            return entry;
        }

        /// <summary>Gives a saved 「境」 work its ambience loop (works saved before ambience existed).</summary>
        public static void AddAmbience(LibraryEntry entry, byte[] mp3)
        {
            if (entry?.Dir == null || mp3 == null) return;
            File.WriteAllBytes(Path.Combine(entry.Dir, "ambience.mp3"), mp3);
            entry.files.ambience = "ambience.mp3";
            WriteEntry(entry.Dir, entry);
            MaliangLog.Info("Library", $"Ambience added to {entry.id}");
        }

        /// <summary>A sound that arrived after the work was saved: added to it (Phase3Design 8.3).</summary>
        public static void AddSound(LibraryEntry entry, string soundPath)
        {
            if (entry?.Dir == null || entry.files.sound != null || !File.Exists(soundPath)) return;
            File.Copy(soundPath, Path.Combine(entry.Dir, "sound.mp3"), true);
            entry.files.sound = "sound.mp3";
            WriteEntry(entry.Dir, entry);
            MaliangLog.Info("Library", $"Sound added to {entry.id}");
        }

        static bool Copy(string src, string dir, string name)
        {
            if (string.IsNullOrEmpty(src) || !File.Exists(src)) return false;
            File.Copy(src, Path.Combine(dir, name), true);
            return true;
        }

        static void WriteEntry(string dir, LibraryEntry entry)
        {
            string tmp = Path.Combine(dir, "entry.json.tmp");
            File.WriteAllText(tmp, JsonConvert.SerializeObject(entry, Formatting.Indented));
            string path = Path.Combine(dir, "entry.json");
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        // ------------------------------------------------------------------ index

        class IndexItem
        {
            public string id;
            public string seal;
            public string createdAt;
        }

        static List<IndexItem> ReadIndex()
        {
            try
            {
                return File.Exists(IndexPath)
                    ? JsonConvert.DeserializeObject<List<IndexItem>>(File.ReadAllText(IndexPath)) ?? new List<IndexItem>()
                    : new List<IndexItem>();
            }
            catch (Exception e)
            {
                MaliangLog.Warn("Library", "Index unreadable, rebuilding: " + e.Message);
                return RebuildIndex();
            }
        }

        static void WriteIndex(List<IndexItem> items)
        {
            Directory.CreateDirectory(Root);
            string tmp = IndexPath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(items, Formatting.Indented));
            if (File.Exists(IndexPath)) File.Delete(IndexPath);
            File.Move(tmp, IndexPath);
        }

        static void AddToIndex(LibraryEntry e)
        {
            var items = ReadIndex();
            items.RemoveAll(i => i.id == e.id);
            items.Add(new IndexItem { id = e.id, seal = e.seal, createdAt = e.createdAt });
            WriteIndex(items);
        }

        /// <summary>Recreates the index from the work folders (if it was lost or damaged).</summary>
        static List<IndexItem> RebuildIndex()
        {
            var items = new List<IndexItem>();
            if (!Directory.Exists(Root)) return items;
            foreach (var dir in Directory.GetDirectories(Root))
            {
                var e = ReadEntry(dir);
                if (e != null) items.Add(new IndexItem { id = e.id, seal = e.seal, createdAt = e.createdAt });
            }
            WriteIndex(items);
            return items;
        }

        // ------------------------------------------------------------------ reading

        /// <summary>
        /// Works stamped with <paramref name="seal"/>, newest first, that are complete on disk. Incomplete ones are left
        /// out with a warning (they stay on disk).
        /// </summary>
        public static List<LibraryEntry> List(SealType seal, int max = int.MaxValue)
        {
            var result = new List<LibraryEntry>();
            foreach (var item in ReadIndex().Where(i => i.seal == seal.ToString()).OrderByDescending(i => i.createdAt))
            {
                if (result.Count >= max) break;
                var e = ReadEntry(Path.Combine(Root, item.id));
                if (e == null) continue;
                var missing = e.MissingFiles();
                if (missing.Count > 0)
                {
                    MaliangLog.Warn("Library", $"{e.id} is incomplete (missing {string.Join(", ", missing)}); not shown");
                    continue;
                }
                result.Add(e);
            }
            return result;
        }

        static LibraryEntry ReadEntry(string dir)
        {
            string path = Path.Combine(dir, "entry.json");
            if (!File.Exists(path)) return null;
            try
            {
                var e = JsonConvert.DeserializeObject<LibraryEntry>(File.ReadAllText(path));
                if (e == null || e.schemaVersion != SchemaVersion) return null;
                e.Dir = dir;
                return e;
            }
            catch (Exception ex)
            {
                MaliangLog.Warn("Library", $"Unreadable work in {dir}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Clears out leftovers of interrupted summonings (pending works and agent scratch folders).</summary>
        public static void CleanUp()
        {
            foreach (var sub in new[] { Pending, Work })
            {
                string dir = Path.Combine(Root, sub);
                if (!Directory.Exists(dir)) continue;
                foreach (var d in Directory.GetDirectories(dir))
                {
                    try { Directory.Delete(d, true); }
                    catch (Exception e) { MaliangLog.Warn("Library", $"Could not remove {d}: {e.Message}"); }
                }
            }
        }
    }

    /// <summary>A work being made: its pending folder (layers, later the model) and the agent's scratch folder.</summary>
    public class PendingWork
    {
        public string Id { get; }
        public SealType Seal { get; }
        public string Dir { get; }
        public string AgentDir { get; }

        internal PendingWork(string id, SealType seal, string dir, string agentDir)
        {
            Id = id;
            Seal = seal;
            Dir = dir;
            AgentDir = agentDir;
        }

        /// <summary>The scroll's drawing and seal layers at full resolution (what the replay shows).</summary>
        public void WriteLayers(byte[] inkPng, byte[] sealPng)
        {
            File.WriteAllBytes(Path.Combine(Dir, "ink.png"), inkPng);
            File.WriteAllBytes(Path.Combine(Dir, "seal.png"), sealPng);
        }

        /// <summary>The summoning succeeded: the work moves into the library.</summary>
        public LibraryEntry Commit(ObjectJob job) => ArtLibrary.Commit(this, job);

        /// <summary>The world was raised: the work moves into the library.</summary>
        public LibraryEntry Commit(WorldJob job) => ArtLibrary.CommitWorld(this, job);

        /// <summary>The summoning failed: nothing is kept.</summary>
        public void Discard()
        {
            TryDelete(Dir);
            DiscardScratch();
        }

        /// <summary>Removes the agent's scratch folder (once the late sound, if any, has been taken).</summary>
        public void DiscardScratch() => TryDelete(AgentDir);

        static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (Exception e) { MaliangLog.Warn("Library", $"Could not remove {dir}: {e.Message}"); }
        }
    }

    /// <summary>entry.json (Phase3Design 8.2).</summary>
    public class LibraryEntry
    {
        public int schemaVersion;
        public string id;
        public string createdAt;
        public string seal;
        public string subject;
        public string category;
        public LibraryFiles files = new LibraryFiles();
        [JsonProperty("object")] public LibraryObject @object;
        public LibraryWorld world;

        /// <summary>The work's folder (not saved).</summary>
        [JsonIgnore] public string Dir;

        public string PathOf(string file) => string.IsNullOrEmpty(file) ? null : System.IO.Path.Combine(Dir, file);

        /// <summary>Files the manifest lists that are not on disk (the replay needs them all).</summary>
        public List<string> MissingFiles()
        {
            var missing = new List<string>();
            void Need(string f) { if (!string.IsNullOrEmpty(f) && !File.Exists(PathOf(f))) missing.Add(f); }
            if (string.IsNullOrEmpty(files.ink) || string.IsNullOrEmpty(files.seal)) missing.Add("layers");
            Need(files.ink);
            Need(files.seal);
            if (seal == SealType.World.ToString())
            {
                if (string.IsNullOrEmpty(files.world)) missing.Add("world");
                Need(files.world);
            }
            if (seal == SealType.Object.ToString())
            {
                if (string.IsNullOrEmpty(files.model)) missing.Add("model");
                Need(files.model);
                foreach (var c in files.clips ?? new string[0]) Need(c);
            }
            return missing;
        }
    }

    public class LibraryFiles
    {
        public string ink, seal, model, sound, refined, plan, world;
        /// <summary>A 「境」 work's looping ambience (optional: a replay without it is simply quiet).</summary>
        public string ambience;
        public string[] clips = new string[0];
    }

    public class LibraryObject
    {
        public bool animated;
        public string[] animations = new string[0];
        public float sizeM = 0.4f;
        public LibrarySound sound;
    }

    /// <summary>A 「境」 work's world: how it was made and how to place it (Phase3Design 8.2).</summary>
    public class LibraryWorld
    {
        public string splatSize;
        public float? metricScale;
        public float? groundOffset;
        public string worldId;
        public string marbleUrl;
        public string caption;
        public string model;
    }

    public class LibrarySound
    {
        public string kind;
        public string trigger;
    }
}
