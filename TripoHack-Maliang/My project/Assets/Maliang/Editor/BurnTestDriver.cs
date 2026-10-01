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
    /// Runs the burn without a headset (play mode): lays the spare scroll on the desk, paints the test drawing, seals
    /// it, holds the candle flame against the paper until it catches, takes the candle away, and saves a frame from the
    /// main camera at each of <see cref="Shots"/> (burned share) to TestData/Burn/&lt;time&gt;/.
    /// </summary>
    public static class BurnTestDriver
    {
        static readonly float[] Shots = { 0.05f, 0.2f, 0.4f, 0.6f, 0.8f, 0.95f };
        const float BurnDuration = 40f;
        static readonly Vector2 TouchUv = new Vector2(0.62f, 0.3f);

        enum Step { LayDown, WaitUnrolled, WaitHover, Touch, Burning, Done }

        static Step _step;
        static ScrollRitual _scroll;
        static ScrollBurn _burn;
        static Transform _candle;
        static Vector3 _candleRest;
        static int _shot;
        static string _dir;
        static float _stepTime;

        [MenuItem("Maliang/Debug/Burn Test (Play Mode)")]
        public static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[BurnTest] enter play mode first"); return; }
            _step = Step.LayDown;
            _shot = 0;
            _dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestData", "Burn", System.DateTime.Now.ToString("HHmmss")));
            Directory.CreateDirectory(_dir);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Debug.Log("[BurnTest] started -> " + _dir);
        }

        static void Next(Step s)
        {
            _step = s;
            _stepTime = Time.time;
        }

        static void Tick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Tick; return; }
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
                    _scroll.OnSealed(SealType.Object);
                    _burn = _scroll.GetComponent<ScrollBurn>();
                    _burn.selfTimedDuration = BurnDuration;
                    Next(Step.WaitHover);
                    break;

                case Step.WaitHover:
                    if (!_scroll.CanIgnite) return;
                    var flame = CandleFlame.All[0];
                    _candle = flame.GetComponentInParent<GrabbableTool>().transform;
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
                    if (_shot < Shots.Length && _burn.Progress >= Shots[_shot])
                    {
                        Capture($"burn_{Mathf.RoundToInt(_burn.Progress * 100):00}.png");
                        _shot++;
                    }
                    if (_burn.IsBurnedAway)
                    {
                        Debug.Log($"[BurnTest] burned away after {Time.time - _stepTime:F1}s; frames in {_dir}");
                        Stop();
                    }
                    break;
            }
        }

        static void Stop()
        {
            _step = Step.Done;
            EditorApplication.update -= Tick;
        }

        static void Capture(string file)
        {
            var cam = Camera.main;
            var rt = new RenderTexture(1280, 720, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            cam.targetTexture = prev;
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(_dir, file), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
