using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maliang.Api;
using Maliang.Core;
using Maliang.Library;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Maliang.Loading
{
    /// <summary>
    /// Puts a generated object into the room (Phase3Design 5.5): loads the GLB (merging extra animation clips),
    /// scales it to the planned size, centres it on the spawn point, adds a grab box and an XR grab, then either the
    /// skeletal animation or procedural motion, and the sound with its trigger.
    /// </summary>
    public static class ObjectSpawner
    {
        public const float MinSize = 0.15f, MaxSize = 1.2f;

        /// <summary>What to spawn; filled from an <see cref="ObjectJob"/> or a library entry.</summary>
        public class Request
        {
            public string ModelPath;
            public string[] ExtraClipPaths = new string[0];
            public bool Animated;
            public string SoundPath;          // may be null; can also arrive later via SummonedObject.SetSound
            public string SoundTrigger;       // on_spawn | on_grab | loop
            public string Category;
            public float SizeM = 0.4f;
            public string Name = "Summoned";

            public static Request From(ObjectJob job) => new Request
            {
                ModelPath = job.ModelPath,
                ExtraClipPaths = job.ExtraClipPaths,
                Animated = job.Animated,
                SoundPath = job.SoundPath,
                SoundTrigger = job.Plan?.Sound?.Wanted == true ? job.Plan.Sound.Trigger : null,
                Category = job.Plan?.Category,
                SizeM = job.Plan?.SizeM ?? 0.4f,
                Name = job.Plan?.Subject ?? "Summoned",
            };

            /// <summary>A work from the library, exactly as it was first summoned.</summary>
            public static Request From(LibraryEntry e) => new Request
            {
                ModelPath = e.PathOf(e.files.model),
                ExtraClipPaths = (e.files.clips ?? new string[0]).Select(e.PathOf).ToArray(),
                Animated = e.@object?.animated ?? false,
                SoundPath = e.PathOf(e.files.sound),
                SoundTrigger = e.@object?.sound?.trigger,
                Category = e.category,
                SizeM = e.@object?.sizeM ?? 0.4f,
                Name = e.subject ?? "Summoned",
            };
        }

        /// <param name="hidden">Build it out of sight and inactive (nothing starts, nothing sounds) until
        /// <see cref="Summoning.Reveal"/> places and wakes it.</param>
        public static async Task<SummonedObject> SpawnAsync(Request req, Vector3 centre, Quaternion facing, CancellationToken cancel = default,
            bool hidden = false)
        {
            // Everything that waits comes first; the object is then put together in one frame.
            AudioClip clip = !string.IsNullOrEmpty(req.SoundPath) ? await SoundClient.LoadClipAsync(req.SoundPath, cancel) : null;
            var loaded = await GlbObjectLoader.LoadAsync(req.ModelPath, null, cancel, req.ExtraClipPaths);
            if (loaded == null) return null;

            // Holder: the grab and the motion move this; the model inside is scaled and centred on it.
            var holder = new GameObject("Summoned " + req.Name);
            holder.transform.SetPositionAndRotation(centre, facing);
            var model = loaded.Root.transform;
            model.SetParent(holder.transform, false);
            float size = Mathf.Clamp(req.SizeM, MinSize, MaxSize);
            var b = loaded.Bounds;
            float longest = Mathf.Max(b.size.x, b.size.y, b.size.z, 0.001f);
            model.localScale = Vector3.one * (size / longest);
            b = GlbObjectLoader.RendererBounds(model.gameObject);
            model.position += centre - b.center;
            b = GlbObjectLoader.RendererBounds(model.gameObject);

            var box = holder.AddComponent<BoxCollider>();
            box.center = holder.transform.InverseTransformPoint(b.center);
            box.size = holder.transform.InverseTransformVector(b.size);
            box.size = new Vector3(Mathf.Abs(box.size.x), Mathf.Abs(box.size.y), Mathf.Abs(box.size.z));

            var rb = holder.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var grab = holder.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.useDynamicAttach = true;
            grab.throwOnDetach = false;

            var summoned = holder.AddComponent<SummonedObject>();
            if (req.Animated && loaded.Animation != null && loaded.ClipNames.Length > 0)
            {
                summoned.anim = loaded.Animation;
                summoned.clips = loaded.ClipNames;
            }
            else
            {
                var motion = holder.AddComponent<ProceduralMotion>();
                motion.flight = req.Category == "avian";
                summoned.motion = motion;
            }

            summoned.soundTrigger = req.SoundTrigger;
            if (clip != null)
            {
                summoned.audioSource = SummonAudio.Create(holder);
                summoned.audioSource.clip = clip;
            }
            if (hidden) holder.SetActive(false); // same frame: Start (motion, sound) waits for the reveal
            MaliangLog.Info("Spawn", $"{req.Name}: size {size:F2} m, {(summoned.anim != null ? $"{summoned.clips.Length} clip(s)" : "procedural motion")}, " +
                                     $"sound {(summoned.audioSource != null ? req.SoundTrigger : "none")}{(hidden ? " (ready, hidden)" : "")}");
            return summoned;
        }
    }
}
