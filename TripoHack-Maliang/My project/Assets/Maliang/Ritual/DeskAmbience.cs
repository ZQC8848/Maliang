using Maliang.Core;
using UnityEngine;

namespace Maliang.Ritual
{
    /// <summary>
    /// The quiet outdoor ambience of the desk above the clouds (wind, distant birds and water). Starts by itself in any
    /// scene that has a <see cref="ScrollStation"/>; nothing needs to be placed in the scene. A 「境」 world turns it
    /// down while its own ambience plays (<see cref="Level"/>).
    /// </summary>
    public static class DeskAmbience
    {
        static AudioSource _source;
        static float _mix;

        /// <summary>Loudness relative to its mix volume (0..1).</summary>
        public static float Level
        {
            get => _mix > 0f && _source != null ? _source.volume / _mix : 1f;
            set { if (_source != null) _source.volume = _mix * Mathf.Clamp01(value); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            if (Object.FindAnyObjectByType<ScrollStation>() == null) return;
            var go = new GameObject("Desk Ambience");
            _source = Sfx.Ambient(SfxId.DeskAmbience, go);
            _mix = _source != null ? _source.volume : 0f;
        }
    }
}
