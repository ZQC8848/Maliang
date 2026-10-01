using Maliang.Core;
using UnityEngine;

namespace Maliang.Ritual
{
    /// <summary>
    /// The quiet outdoor ambience of the desk above the clouds (wind, distant birds and water). Starts by itself in any
    /// scene that has a <see cref="ScrollStation"/>; nothing needs to be placed in the scene.
    /// </summary>
    public static class DeskAmbience
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            if (Object.FindAnyObjectByType<ScrollStation>() == null) return;
            var go = new GameObject("Desk Ambience");
            Sfx.Ambient(SfxId.DeskAmbience, go);
        }
    }
}
