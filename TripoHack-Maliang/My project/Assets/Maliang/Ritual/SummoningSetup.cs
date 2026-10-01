using Maliang.Core;
using Maliang.Library;
using UnityEngine;

namespace Maliang.Ritual
{
    /// <summary>
    /// Connects sealing to the real summoning at start-up (Phase 5): with the config online and the object keys set,
    /// a scroll sealed 「物」 runs the <see cref="SummonJob"/> (agent, library, reveal). Otherwise sealed scrolls play a
    /// <see cref="FakeJob"/>. 「境」 waits for Phase 6. Also clears leftovers of interrupted summonings.
    /// </summary>
    public static class SummoningSetup
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            ArtLibrary.CleanUp();
            var config = MaliangConfig.Current;
            if (config.IsOnline && config.HasObjectKeys)
            {
                ScrollRitual.StartJob = r => r.Seal == SealType.Object ? new SummonJob(r) : null;
                MaliangLog.Info("Summon", $"Real summoning on (library at {ArtLibrary.Root})");
            }
            else
            {
                ScrollRitual.StartJob = null;
                MaliangLog.Info("Summon", "Real summoning off (config offline or keys missing): sealed scrolls play a fake summoning");
            }
        }
    }
}
