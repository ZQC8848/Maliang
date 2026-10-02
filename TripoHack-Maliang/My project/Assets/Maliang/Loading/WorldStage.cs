using System.Collections;
using System.IO;
using System.Threading.Tasks;
using Maliang.Api;
using Maliang.Core;
using Maliang.Ritual;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Maliang.Loading
{
    /// <summary>
    /// The world around the lotus throne (Phase 6). The player never leaves the throne: a 「境」 world is raised
    /// around it, covering the sky, with the desk where it was. One world at a time; a new one replaces the old.
    /// The world is centred on the player's platform with its forward (the generated view) towards the desk; it
    /// blooms in out of thin air and fades away the same way. Burning the sky scroll clears it (<see cref="Clear"/>).
    /// The world's ambience loop (if it has one) fades in with it, kept quiet, while the desk's own ambience steps back.
    /// </summary>
    public class WorldStage : MonoBehaviour
    {
        /// <summary>World Labs worlds are generated from eye level; without scale metadata (draft model) the origin
        /// is taken to be this high above the ground (m).</summary>
        public const float AssumedEyeHeight = 1.6f;

        [Tooltip("How far below the throne's floor the world's ground lies (m): the throne floats a little above the land.")]
        public float groundBelowFloor = 1.6f;
        public float bloomInSeconds = 3.5f;
        public float fadeOutSeconds = 2.5f;
        [Tooltip("The world's ambience loudness: kept low, a background to the ritual. Each world's loop is first levelled " +
                 "to the same loudness (generated sounds vary), so this sets them all.")]
        [Range(0f, 1f)] public float ambienceVolume = 0.28f;
        public float ambienceFadeInSeconds = 4f;
        [Tooltip("The desk's ambience (wind above the clouds) while a world's ambience plays, relative to its usual loudness.")]
        [Range(0f, 1f)] public float deskAmbienceUnderWorld = 0.35f;

        static WorldStage _instance;

        SplatWorldLoader _loader;
        Coroutine _transition;
        AudioSource _ambience;
        float _ambienceGain = 1f;
        bool _leaving;

        /// <summary>The loudness (RMS) a world's ambience is levelled to before <see cref="ambienceVolume"/> (about -37 dBFS).</summary>
        const float AmbienceReferenceRms = 0.014f;

        public static WorldStage Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<WorldStage>();
                    if (_instance == null) _instance = new GameObject("World Stage").AddComponent<WorldStage>();
                }
                return _instance;
            }
        }

        public bool HasWorld => _loader != null;
        public string CurrentName { get; private set; }

        /// <summary>
        /// Raises the world in <paramref name="spzPath"/> around the throne (replacing any world there now), with its
        /// ambience loop from <paramref name="ambiencePath"/> (MP3; null or missing: silent).
        /// </summary>
        public void Show(string spzPath, float? metricScale, float? groundOffset, string name, string ambiencePath = null)
        {
            if (!File.Exists(spzPath))
            {
                MaliangLog.Warn("World", $"Cannot show {name}: {spzPath} is missing");
                return;
            }
            if (_transition != null) StopCoroutine(_transition);
            _transition = StartCoroutine(ShowRoutine(spzPath, metricScale, groundOffset, name, ambiencePath));
        }

        /// <summary>Raises a library work's world (with its scale, ground and ambience); null does nothing.</summary>
        public void Show(Library.LibraryEntry work)
        {
            if (work == null) return;
            Show(work.PathOf(work.files.world), work.world?.metricScale, work.world?.groundOffset, work.subject,
                work.PathOf(work.files.ambience));
        }

        /// <summary>Lets the current world fade away, back to the sky.</summary>
        public void Clear()
        {
            if (_transition != null) StopCoroutine(_transition);
            _transition = StartCoroutine(ClearRoutine());
        }

        IEnumerator ShowRoutine(string spzPath, float? metricScale, float? groundOffset, string name, string ambiencePath)
        {
            var clip = !string.IsNullOrEmpty(ambiencePath) && File.Exists(ambiencePath) ? SoundClient.LoadClipAsync(ambiencePath) : null;
            if (_loader != null) yield return FadeOut();
            _leaving = false;

            var go = new GameObject("World " + name);
            go.transform.SetParent(transform, false);
            var root = new GameObject("World Root").transform;
            root.SetParent(go.transform, false);
            _loader = go.AddComponent<SplatWorldLoader>();
            _loader.worldRoot = root;
            _loader.LoadSpz(spzPath); // a short hitch (about half a second for two million splats), hidden by the bloom
            Place(go.transform, root, metricScale, groundOffset);
            CurrentName = name;
            MaliangLog.Info("World", $"Showing \"{name}\" ({_loader.LoadedSplats:N0} splats, scale {metricScale?.ToString("F2") ?? "assumed 1"}, " +
                                     $"ground {groundOffset?.ToString("F2") ?? "assumed"})");

            _loader.Bloom = 0f;
            StartCoroutine(StartAmbience(clip, _loader));
            yield return null; // the renderer binds the asset on its first update
            for (float t = 0f; t < bloomInSeconds; t += Time.deltaTime)
            {
                float k = t / bloomInSeconds;
                _loader.Bloom = 1f - Mathf.Pow(1f - k, 3f); // ease out: blooms fast, settles slowly
                yield return null;
            }
            _loader.Bloom = 1f;
            _transition = null;
        }

        IEnumerator ClearRoutine()
        {
            if (_loader != null)
            {
                string name = CurrentName;
                yield return FadeOut();
                MaliangLog.Info("World", $"\"{name}\" faded; the sky is back");
            }
            _transition = null;
        }

        /// <summary>Starts the ambience once decoded (unless the world has gone meanwhile); <see cref="Update"/> fades it.</summary>
        IEnumerator StartAmbience(Task<AudioClip> clip, SplatWorldLoader owner)
        {
            if (clip == null) yield break;
            while (!clip.IsCompleted) yield return null;
            if (clip.IsFaulted || clip.Result == null || _loader != owner || _leaving) yield break;
            _ambience = owner.gameObject.AddComponent<AudioSource>();
            _ambience.clip = clip.Result;
            _ambience.loop = true;
            _ambience.spatialBlend = 0f; // all around: the place itself
            _ambience.volume = 0f;
            _ambience.playOnAwake = false;
            _ambience.time = Random.Range(0f, clip.Result.length * 0.5f);
            float rms = Rms(clip.Result);
            _ambienceGain = rms > 1e-5f ? Mathf.Clamp(AmbienceReferenceRms / rms, 0.3f, 2f) : 1f;
            _ambience.Play();
        }

        /// <summary>Eases the world's ambience and the desk's towards their levels (in with the world, out with its fade).</summary>
        void Update()
        {
            bool on = _loader != null && !_leaving;
            bool worldSound = on && _ambience != null;
            float seconds = on ? ambienceFadeInSeconds : fadeOutSeconds * 0.9f;
            float step = Time.deltaTime / Mathf.Max(0.05f, seconds);
            float full = Mathf.Min(1f, ambienceVolume * _ambienceGain);
            if (_ambience != null) _ambience.volume = Mathf.MoveTowards(_ambience.volume, on ? full : 0f, step * full);
            DeskAmbience.Level = Mathf.MoveTowards(DeskAmbience.Level, worldSound ? deskAmbienceUnderWorld : 1f, step);
        }

        static float Rms(AudioClip clip)
        {
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) return 0f;
            double sum = 0;
            for (int i = 0; i < data.Length; i++) sum += data[i] * data[i];
            return data.Length > 0 ? (float)System.Math.Sqrt(sum / data.Length) : 0f;
        }

        IEnumerator FadeOut()
        {
            _leaving = true;
            float b0 = _loader.Bloom;
            for (float t = 0f; t < fadeOutSeconds; t += Time.deltaTime)
            {
                float k = t / fadeOutSeconds;
                _loader.Bloom = b0 * (1f - k * k);
                yield return null;
            }
            Destroy(_loader.gameObject); // unloads the splats (and the ambience with it)
            _loader = null;
            _ambience = null;
            CurrentName = null;
        }

        /// <summary>
        /// Centres the world on the player's throne, its forward towards the desk, its ground
        /// <see cref="groundBelowFloor"/> below the throne's floor. Scale and ground come from World Labs' semantics
        /// when given (metric units, ground at y = 0 after the offset); otherwise the generation origin is taken to be
        /// at eye height.
        /// </summary>
        void Place(Transform world, Transform root, float? metricScale, float? groundOffset)
        {
            var rig = FindAnyObjectByType<XROrigin>();
            Vector3 centre = rig != null ? rig.transform.position : Vector3.zero;
            Vector3 forward = Vector3.forward;
            var station = FindAnyObjectByType<Ritual.ScrollStation>();
            if (station != null)
            {
                Vector3 toDesk = Vector3.ProjectOnPlane(station.transform.position - centre, Vector3.up);
                if (toDesk.sqrMagnitude > 1e-4f) forward = toDesk.normalized;
            }
            world.SetPositionAndRotation(centre, Quaternion.LookRotation(forward, Vector3.up));

            float scale = metricScale is float s && s > 0f ? s : 1f;
            float ground = groundOffset ?? AssumedEyeHeight; // metres above the world's ground of its origin
            _loader.Align(new WorldAlignment { metricScaleFactor = scale, groundPlaneOffset = ground - groundBelowFloor });
        }
    }
}
