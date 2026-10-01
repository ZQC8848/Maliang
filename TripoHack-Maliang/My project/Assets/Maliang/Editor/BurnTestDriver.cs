using System.IO;
using Maliang.Core;
using Maliang.Drawing;
using Maliang.Effects;
using Maliang.Ritual;
using Maliang.VR;
using UnityEditor;
using UnityEngine;

namespace Maliang.EditorTools
{
    /// <summary>
    /// Runs the burn without a headset (play mode) against a fake summoning job: lays the spare scroll on the desk,
    /// paints the test drawing, seals it, holds the candle flame against the paper until it catches, takes the candle
    /// away, and saves frames to TestData/Burn/&lt;time&gt;_&lt;outcome&gt;/: at set burned shares, while the fire waits
    /// at the hold point, and through the failure sequence. A capture camera at the player's head follows the scroll,
    /// so the remnant stays in view when it drops.
    /// </summary>
    public static class BurnTestDriver
    {
        static readonly float[] Shots = { 0.05f, 0.2f, 0.4f, 0.6f, 0.8f, 0.95f };
        static readonly float[] FailShots = { 0.4f, 1.0f, 1.7f, 2.6f, 5f, 14.4f };
        static readonly Vector2 TouchUv = new Vector2(0.62f, 0.3f);

        enum Step { LayDown, WaitUnrolled, WaitHover, Touch, Burning, Done }

        static Step _step;
        static ScrollRitual _scroll;
        static ScrollBurn _burn;
        static BurnPacer _pacer;
        static Transform _candle;
        static Vector3 _candleRest;
        static int _shot, _failShot;
        static bool _holdShot;
        static float _failedAt = -1f, _stepTime;
        static string _dir;
        static FakeJobSettings _settings;
        static float _replayLoad = -1f; // >= 0: replay with a stand-in load of this many seconds
        static bool _replayFaded;
        static Camera _capture;

        [MenuItem("Maliang/Debug/Burn Test/Success (Play Mode)")]
        static void RunSuccess() => Run(FakeOutcome.Success, 6f);
        [MenuItem("Maliang/Debug/Burn Test/Fail At Verdict (Play Mode)")]
        static void RunFailVerdict() => Run(FakeOutcome.FailAtVerdict, 6f);
        [MenuItem("Maliang/Debug/Burn Test/Fail During Generation (Play Mode)")]
        static void RunFailGeneration() => Run(FakeOutcome.FailAfterVerdict, 6f);
        [MenuItem("Maliang/Debug/Burn Test/Slow Verdict - Hold At 40% (Play Mode)")]
        static void RunSlowVerdict() => Run(FakeOutcome.Success, 25f);
        [MenuItem("Maliang/Debug/Burn Test/Replay - Loads In 3 s (Play Mode)")]
        static void RunReplay() => RunReplay(3f, false);
        [MenuItem("Maliang/Debug/Burn Test/Replay - Slow Load, Embers (Play Mode)")]
        static void RunReplaySlow() => RunReplay(16f, false);
        [MenuItem("Maliang/Debug/Burn Test/Replay - Faded (Play Mode)")]
        static void RunReplayFaded() => RunReplay(2f, true);

        /// <summary>A replayed scroll (fixed 10 s burn) with a stand-in load of <paramref name="loadSeconds"/>.</summary>
        public static void RunReplay(float loadSeconds, bool faded)
        {
            Start(new FakeJobSettings(), $"Replay_{loadSeconds:F0}s{(faded ? "_faded" : "")}");
            _replayLoad = loadSeconds;
            _replayFaded = faded;
        }

        public static void Run(FakeOutcome outcome, float verdictDelay, float generationTime = 20f)
        {
            Start(new FakeJobSettings { outcome = outcome, verdictDelay = verdictDelay, generationTime = generationTime },
                $"{outcome}{(verdictDelay > 10f ? "_slow" : "")}");
        }

        static void Start(FakeJobSettings settings, string label)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[BurnTest] enter play mode first"); return; }
            _settings = settings;
            _replayLoad = -1f;
            _step = Step.LayDown;
            _shot = _failShot = 0;
            _holdShot = false;
            _failedAt = -1f;
            _dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestData", "Burn", $"{System.DateTime.Now:HHmmss}_{label}"));
            Directory.CreateDirectory(_dir);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Debug.Log($"[BurnTest] {label} -> {_dir}");
        }

        static void Next(Step s)
        {
            _step = s;
            _stepTime = Time.time;
        }

        static void Tick()
        {
            if (!Application.isPlaying) { Stop(); return; }
            var station = Object.FindAnyObjectByType<ScrollStation>();
            switch (_step)
            {
                case Step.LayDown:
                    if (station.spare == null) return;
                    station.spare.transform.position = station.transform.position + Vector3.up * 0.05f;
                    _scroll = station.spare.Ritual;
                    if (station.TryAccept(station.spare)) Next(Step.WaitUnrolled);
                    break;

                case Step.WaitUnrolled:
                    if (_scroll.State != ScrollState.Unrolled) return;
                    CanvasTestPainter.PaintMountainScene(_scroll.canvas, Object.FindAnyObjectByType<BrushPen>().CurrentStyle);
                    if (_replayLoad >= 0f) _scroll.BeginReplay(ReplayJob.Fake(_replayLoad, _replayFaded)); // rises by itself
                    else
                    {
                        FakeJob.Settings = _settings;
                        _scroll.OnSealed(SealType.Object);
                    }
                    _burn = _scroll.GetComponent<ScrollBurn>();
                    _pacer = _scroll.GetComponent<BurnPacer>();
                    Next(Step.WaitHover);
                    break;

                case Step.WaitHover:
                    if (!_scroll.CanIgnite) return;
                    _candle = CandleFlame.All[0].GetComponentInParent<GrabbableTool>().transform;
                    _candleRest = _candle.position;
                    Next(Step.Touch);
                    break;

                case Step.Touch:
                    if (!_burn.IsBurning)
                    {
                        // Keep the flame on the paper as it bobs.
                        var f = CandleFlame.All[0];
                        Vector3 target = _scroll.canvas.transform.TransformPoint(new Vector3(
                            (TouchUv.x - 0.5f) * _scroll.canvas.size.x, 0.004f, (TouchUv.y - 0.5f) * _scroll.canvas.size.y));
                        _candle.position += target - Vector3.Lerp(f.transform.position, f.Tip.position, 0.75f);
                        if (Time.time - _stepTime > 3f) { Debug.LogError("[BurnTest] the candle did not light the scroll"); Stop(); }
                        return;
                    }
                    Debug.Log($"[BurnTest] caught after {Time.time - _stepTime:F2}s at uv {_burn.IgnitionUv}");
                    _candle.position = _candleRest; // the candle no longer matters
                    Next(Step.Burning);
                    break;

                case Step.Burning:
                    float t = Time.time - _stepTime;
                    if (_scroll.State == ScrollState.Failed)
                    {
                        if (_failedAt < 0f) _failedAt = Time.time;
                        float since = Time.time - _failedAt;
                        if (_failShot < FailShots.Length && since >= FailShots[_failShot])
                        {
                            Capture($"fail_{FailShots[_failShot]:00.0}s.png");
                            _failShot++;
                        }
                        if (!_scroll.gameObject.activeInHierarchy)
                        {
                            Debug.Log($"[BurnTest] failure sequence done ({since:F1}s); frames in {_dir}");
                            Stop();
                        }
                        return;
                    }
                    if (!_holdShot && _pacer != null && (_pacer.Current == BurnPacer.Phase.Holding || _pacer.Current == BurnPacer.Phase.Embers) && t > 4f)
                    {
                        Capture($"hold_{Mathf.RoundToInt(_burn.Progress * 100):00}.png");
                        _holdShot = true;
                    }
                    if (_shot < Shots.Length && _burn.Progress >= Shots[_shot])
                    {
                        Capture($"burn_{Mathf.RoundToInt(_burn.Progress * 100):00}.png");
                        _shot++;
                    }
                    if (_burn.IsBurnedAway)
                    {
                        Debug.Log($"[BurnTest] burned away {t:F1}s after catching; frames in {_dir}");
                        Stop();
                    }
                    break;
            }
        }

        static void Stop()
        {
            _step = Step.Done;
            EditorApplication.update -= Tick;
            if (_capture != null) Object.Destroy(_capture.gameObject);
            _capture = null;
        }

        /// <summary>A frame from the player's eye position, looking at the scroll (wherever it has fallen).</summary>
        static void Capture(string file)
        {
            var main = Camera.main;
            if (_capture == null)
            {
                var go = new GameObject("Burn Test Capture Camera");
                _capture = go.AddComponent<Camera>();
                _capture.CopyFrom(main);
                _capture.enabled = false;
            }
            Vector3 eye = main.transform.position;
            Vector3 look = _scroll.canvas.transform.position - eye;
            _capture.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look.sqrMagnitude > 1e-4f ? look : main.transform.forward, Vector3.up));
            _capture.fieldOfView = 70f;

            var rt = new RenderTexture(1280, 720, 24);
            _capture.targetTexture = rt;
            _capture.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            _capture.targetTexture = null;
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(_dir, file), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
