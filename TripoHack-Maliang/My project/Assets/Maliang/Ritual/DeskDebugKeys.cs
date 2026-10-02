using System.IO;
using Maliang.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Maliang.Ritual
{
    /// <summary>
    /// Development shortcuts for the desk: keyboard (editor / desktop) and the left controller's menu button.
    /// R or left menu: reset the scroll (always). The rest only in the editor and development builds:
    /// E: export the ink to TestData/Exports/ as PNG.
    /// B: set the hovering scroll alight without the candle.   I: log the state of every scroll.
    /// 0: switch between the real agent and fake summoning (no API).
    /// Fake summoning (the fake job a sealed scroll waits on; also applied to the scroll in progress):
    /// 1: succeeds   2: fails at the verdict (unrecognizable)   3: fails during generation (collapsed)
    /// 4: slow verdict (25 s), to see the fire wait at 40%.
    /// Replay (the 10 s library burn) of whatever lies drawn on the desk, with a stand-in load:
    /// 5: loads in 3 s   6: loads in 16 s (the fire waits in its embers)   7: files broken (faded).
    /// </summary>
    public class DeskDebugKeys : MonoBehaviour
    {
        [Tooltip("Resets / exports the station's active scroll (and clears away earlier ones). Without it, the ritual below.")]
        public ScrollStation station;
        public ScrollRitual ritual;
        [Tooltip("Use the fake summoning below even when the real agent is available (no API calls, no credits). Key 0 toggles.")]
        public bool fakeSummoning;
        [Tooltip("How a sealed scroll's summoning plays out when it is fake (or the real agent is not available).")]
        public FakeJobSettings fakeJob = new FakeJobSettings();

        ScrollRitual Active => station != null && station.Active != null ? station.Active : ritual;

        InputAction _reset;

        void OnEnable()
        {
            _reset = new InputAction(type: InputActionType.Button);
            _reset.AddBinding("<Keyboard>/r");
            _reset.AddBinding("<XRController>{LeftHand}/menuButton");
            _reset.Enable();
        }

        void OnDisable() => _reset?.Dispose();

        void Awake() => Apply();
        void OnValidate() => Apply();

        void Apply()
        {
            FakeJob.Settings = fakeJob;
            FakeJob.Forced = fakeSummoning;
        }

        void Update()
        {
            if (Active == null) return;
            if (_reset.WasPressedThisFrame())
            {
                if (station != null) station.ResetAll();
                else ritual.ResetScroll();
            }
            if (!Debug.isDebugBuild) return; // a release build (the judges') has only the reset
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) ExportToTestData();
            if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame) IgniteHovering();
            if (Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame) LogScrolls();
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.digit0Key.wasPressedThisFrame)
            {
                fakeSummoning = !fakeSummoning;
                Apply();
                MaliangLog.Info("Debug", fakeSummoning ? "Summoning: FAKE (no API)" : "Summoning: real agent (if configured)");
            }
            if (kb.digit1Key.wasPressedThisFrame) SetOutcome(FakeOutcome.Success, 6f);
            if (kb.digit2Key.wasPressedThisFrame) SetOutcome(FakeOutcome.FailAtVerdict, 6f);
            if (kb.digit3Key.wasPressedThisFrame) SetOutcome(FakeOutcome.FailAfterVerdict, 6f);
            if (kb.digit4Key.wasPressedThisFrame) SetOutcome(FakeOutcome.Success, 25f);
            if (kb.digit5Key.wasPressedThisFrame) Replay(3f, false);
            if (kb.digit6Key.wasPressedThisFrame) Replay(16f, false);
            if (kb.digit7Key.wasPressedThisFrame) Replay(2f, true);
        }

        /// <summary>Turns the scroll lying on the desk into a replay (locked; rises by itself) with a stand-in load.</summary>
        public void Replay(float loadSeconds, bool faded)
        {
            var active = Active;
            if (active == null || (active.State != ScrollState.Unrolled && active.State != ScrollState.Rolled))
            {
                MaliangLog.Info("Debug", "Replay needs a scroll lying on the desk (not yet sealed).");
                return;
            }
            active.BeginReplay(ReplayJob.Fake(loadSeconds, faded));
            MaliangLog.Info("Debug", $"Replay: loads in {loadSeconds:F0}s{(faded ? ", then fails (faded)" : "")}");
        }

        /// <summary>Sets how fake summonings play out, and restarts the active scroll's job if it has not finished.</summary>
        public void SetOutcome(FakeOutcome outcome, float verdictDelay)
        {
            fakeJob.outcome = outcome;
            fakeJob.verdictDelay = verdictDelay;
            fakeSummoning = true; // choosing a fake outcome means testing without the API
            Apply();
            var active = Active;
            if (active != null && active.Job is FakeJob && !active.Job.Done &&
                (active.State == ScrollState.Levitating || active.State == ScrollState.Burning))
                active.Job = new FakeJob(fakeJob, Time.time);
            MaliangLog.Info("Debug", $"Fake summoning: {outcome}, verdict after {verdictDelay:F0}s" +
                                     (active != null && active.Job is FakeJob ? " (applied to the current scroll)" : ""));
        }

        /// <summary>Logs every scroll's state (to diagnose a summoning that did not appear).</summary>
        [ContextMenu("Log Scrolls")]
        public void LogScrolls()
        {
            foreach (var r in FindObjectsByType<ScrollRitual>(FindObjectsInactive.Include))
                MaliangLog.Info("Debug", r.Describe());
        }

        /// <summary>Sets the hovering scroll alight at a random spot, without the candle (desktop testing).</summary>
        [ContextMenu("Ignite Hovering Scroll")]
        public void IgniteHovering()
        {
            foreach (var burn in FindObjectsByType<ScrollBurn>())
            {
                if (burn.ritual == null || !burn.ritual.CanIgnite) continue;
                burn.Ignite(new Vector2(Random.Range(0.2f, 0.8f), Random.Range(0.2f, 0.8f)));
                return;
            }
            MaliangLog.Info("Debug", "No hovering scroll to ignite.");
        }

        [ContextMenu("Export Ink To TestData")]
        public void ExportToTestData()
        {
            Active.canvas.Export(result =>
            {
                if (result.Empty) return;
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestData", "Exports"));
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"ink_{System.DateTime.Now:yyyyMMdd_HHmmss}.png");
                File.WriteAllBytes(path, result.Png);
                MaliangLog.Info("Debug", "Wrote " + path);
            });
        }
    }
}
