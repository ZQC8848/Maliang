using UnityEngine;

namespace Maliang.Core
{
    /// <summary>Names of the pre-made sound effects (Assets/Maliang/Audio/SFX; made by Tools/sfx/generate_sfx.py).</summary>
    public static class SfxId
    {
        public const string ScrollRoll = "scroll_roll";
        public const string BrushTouch = "brush_touch";
        public const string BrushStrokeLoop = "brush_stroke_loop";
        public const string InkDip = "ink_dip";
        public const string SealStamp = "seal_stamp";
        public const string ScrollRise = "scroll_rise";
        public const string Ignite = "ignite";
        public const string BurnLoop = "burn_loop";
        public const string EmberPop = "ember_pop";
        public const string RodFall = "rod_fall";
        public const string Fizzle = "fizzle";
        public const string Deflate = "deflate";
        public const string ScrollThud = "scroll_thud";
        public const string Crumble = "crumble";
        public const string Materialize = "materialize";
        public const string DrawerSlideLoop = "drawer_slide_loop";
        public const string DrawerKnock = "drawer_knock";
        public const string DeskAmbience = "desk_ambience";
    }

    /// <summary>
    /// Plays the pre-made sound effects in the room: one-shots at a point, or loops that follow an object and whose
    /// loudness the caller drives. All are 3D (heard from where they happen) except <see cref="Ambient"/>.
    /// Missing sounds are silently skipped.
    /// </summary>
    public static class Sfx
    {
        static SoundBank _bank;
        static bool _loaded;

        static SoundBank Bank
        {
            get
            {
                if (!_loaded) { _bank = Resources.Load<SoundBank>("SoundBank"); _loaded = true; }
                return _bank;
            }
        }

        public static AudioClip Clip(string id) => Bank != null ? Bank.Find(id)?.clip : null;

        /// <summary>One shot at <paramref name="at"/>. <paramref name="volume"/> scales the bank's mix volume.</summary>
        public static AudioSource Play(string id, Vector3 at, float volume = 1f, float pitchJitter = 0.06f)
        {
            var e = Bank != null ? Bank.Find(id) : null;
            if (e == null || e.clip == null || volume <= 0f) return null;
            var go = new GameObject("Sfx " + id);
            go.transform.position = at;
            var src = Configure(go.AddComponent<AudioSource>());
            src.clip = e.clip;
            src.volume = e.volume * volume;
            src.pitch = 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter);
            src.Play();
            UnityEngine.Object.Destroy(go, e.clip.length / Mathf.Max(0.1f, src.pitch) + 0.1f);
            return src;
        }

        /// <summary>
        /// A looping 3D source on <paramref name="parent"/>, playing at volume 0; set <see cref="LoopVolume"/> each
        /// frame (0..1, scaled by the bank's mix volume). Returns null if the sound is missing.
        /// </summary>
        public static AudioSource Loop(string id, Transform parent)
        {
            var e = Bank != null ? Bank.Find(id) : null;
            if (e == null || e.clip == null) return null;
            var go = new GameObject("Sfx " + id);
            go.transform.SetParent(parent, false);
            var src = Configure(go.AddComponent<AudioSource>());
            src.clip = e.clip;
            src.loop = true;
            src.volume = 0f;
            src.time = UnityEngine.Random.value * e.clip.length; // copies of a loop do not play in step
            src.Play();
            return src;
        }

        /// <summary>Sets a loop's loudness (0..1 of its mix volume), easing towards it so it never clicks.</summary>
        public static void LoopVolume(AudioSource src, string id, float level, float sharpness = 10f)
        {
            if (src == null) return;
            var e = Bank != null ? Bank.Find(id) : null;
            float target = (e != null ? e.volume : 1f) * Mathf.Clamp01(level);
            src.volume = Mathf.Lerp(src.volume, target, 1f - Mathf.Exp(-sharpness * Time.deltaTime));
        }

        /// <summary>A non-spatial loop (room ambience), already at its mix volume.</summary>
        public static AudioSource Ambient(string id, GameObject host)
        {
            var e = Bank != null ? Bank.Find(id) : null;
            if (e == null || e.clip == null) return null;
            var src = host.AddComponent<AudioSource>();
            src.clip = e.clip;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = e.volume;
            src.playOnAwake = false;
            src.Play();
            return src;
        }

        /// <summary>Fades a source out over <paramref name="seconds"/>, then destroys its GameObject.</summary>
        public static void FadeOut(AudioSource src, float seconds)
        {
            if (src == null) return;
            src.transform.SetParent(null, true); // outlives whatever it was following
            src.gameObject.AddComponent<SfxFadeOut>().seconds = seconds;
        }

        static AudioSource Configure(AudioSource src)
        {
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 0.35f;
            src.maxDistance = 8f;
            src.dopplerLevel = 0f;
            return src;
        }
    }

    /// <summary>Fades its AudioSource to silence, then removes the GameObject (<see cref="Sfx.FadeOut"/>).</summary>
    public class SfxFadeOut : MonoBehaviour
    {
        public float seconds = 0.8f;
        AudioSource _src;
        float _v0, _t;

        void Start()
        {
            _src = GetComponent<AudioSource>();
            _v0 = _src != null ? _src.volume : 0f;
        }

        void Update()
        {
            _t += Time.deltaTime;
            if (_src != null) _src.volume = _v0 * Mathf.Clamp01(1f - _t / Mathf.Max(0.01f, seconds));
            if (_t >= seconds) Destroy(gameObject);
        }
    }
}
