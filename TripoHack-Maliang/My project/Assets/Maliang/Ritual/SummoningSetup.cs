using Maliang.Core;
using Maliang.Library;
using UnityEngine;

namespace Maliang.Ritual
{
    /// <summary>
    /// Connects sealing to the real summoning at start-up (Phase 5): with the config online and the object keys set,
    /// a scroll sealed 「物」 runs the <see cref="SummonJob"/> (agent, library, reveal) and one sealed 「境」 the
    /// <see cref="WorldSummonJob"/> (with the World Labs key). Otherwise sealed scrolls play a <see cref="FakeJob"/>.
    /// Also clears leftovers of interrupted summonings.
    /// </summary>
    public static class SummoningSetup
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            ArtLibrary.CleanUp();
            var config = MaliangConfig.Current;
            if (config.IsOnline && (config.HasObjectKeys || config.HasWorldKeys))
            {
                bool objects = config.HasObjectKeys, worlds = config.HasWorldKeys;
                ScrollRitual.StartJob = r =>
                    r.Seal == SealType.Object && objects ? new SummonJob(r)
                    : r.Seal == SealType.World && worlds ? new WorldSummonJob(r)
                    : (IBurnJob)null;
                MaliangLog.Info("Summon", $"Real summoning on: objects {objects}, worlds {worlds}" +
                                          $"{(worlds && config.worldLabs.draft ? " (draft worlds)" : "")} (library at {ArtLibrary.Root})");
            }
            else
            {
                ScrollRitual.StartJob = null;
                MaliangLog.Info("Summon", "Real summoning off (config offline or keys missing): sealed scrolls play a fake summoning");
            }
        }
    }
}
