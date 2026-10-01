using System;
using System.Collections.Generic;
using UnityEngine;

namespace Maliang.Core
{
    /// <summary>
    /// The pre-made sound effects with their mix volume. Lives at Resources/SoundBank (built by the editor menu
    /// Maliang/Build/Sound Bank from the files in Audio/SFX).
    /// </summary>
    [CreateAssetMenu(menuName = "Maliang/Sound Bank")]
    public class SoundBank : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string id;
            public AudioClip clip;
            [Range(0f, 1f)] public float volume = 0.8f;
        }

        public List<Entry> sounds = new List<Entry>();

        public Entry Find(string id) => sounds.Find(e => e.id == id);
    }
}
