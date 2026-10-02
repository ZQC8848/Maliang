using System.Collections;
using System.IO;
using Maliang.Loading;
using Maliang.Ritual;
using UnityEngine;

namespace Maliang.Core
{
    /// <summary>
    /// A build check, only with <c>-selftest</c> on the command line (Maliang.exe -selftest): replays the bundled cat
    /// and lake from the drawers (no API, no headset needed), saves a screenshot of each result next to the exe
    /// (selftest_object.png, selftest_world.png) and quits. Shows that models (glTF shaders) and splats render in the
    /// player. Without the flag it does nothing.
    /// </summary>
    public class SelfTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-selftest") < 0) return;
            new GameObject("Self Test").AddComponent<SelfTest>();
        }

        static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        IEnumerator Start()
        {
            MaliangLog.Info("SelfTest", "Starting");
            yield return new WaitForSeconds(3f);
            yield return Burn("orange cat", "selftest_object.png", 2.5f);
            foreach (var o in FindObjectsByType<SummonedObject>()) Destroy(o.gameObject);
            yield return new WaitForSeconds(2f);
            yield return Burn("lake", "selftest_world.png", 5f);
            MaliangLog.Info("SelfTest", "Done");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        IEnumerator Burn(string name, string shot, float settle)
        {
            ScrollRitual scroll = null;
            for (float t = 0f; scroll == null && t < 15f; t += 0.25f)
            {
                foreach (var r in FindObjectsByType<ScrollRitual>())
                    if (r.name.Contains(name) && r.State == ScrollState.Rolled) scroll = r;
                if (scroll == null) yield return new WaitForSeconds(0.25f);
            }
            if (scroll == null) { MaliangLog.Warn("SelfTest", $"No drawer scroll named *{name}*"); yield break; }

            var station = FindAnyObjectByType<ScrollStation>();
            var pickup = scroll.GetComponent<ScrollPickup>();
            pickup.transform.position = station.transform.position + Vector3.up * 0.05f;
            if (!station.TryAccept(pickup)) { MaliangLog.Warn("SelfTest", "The desk was not free"); yield break; }

            var burn = scroll.GetComponent<ScrollBurn>();
            for (float t = 0f; !scroll.CanIgnite && t < 20f; t += Time.deltaTime) yield return null;
            burn.Ignite(new Vector2(0.5f, 0.5f));
            for (float t = 0f; !burn.IsBurnedAway && t < 40f; t += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(settle);

            string path = Path.Combine(Folder, shot);
            ScreenCapture.CaptureScreenshot(path);
            MaliangLog.Info("SelfTest", $"{name}: burned away {burn.IsBurnedAway}, state {scroll.State}; screenshot {path}");
            yield return new WaitForSeconds(1f);
        }
    }
}
