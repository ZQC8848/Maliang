using System;
using System.Collections;
using System.Text;
using Gsplat;
using Maliang.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Maliang.Loading
{
    /// <summary>
    /// M0 Splat Spike: loads a Marble SPZ in the headset and lets you tune coordinates / scale / ground offset
    /// with the controllers while watching frame times. Not part of the shipping game.
    ///
    /// Right A: next source coordinate convention (reloads)    Right B: rotate world 180° around Y
    /// Left X: scale up (hold)   Left Y: scale down (hold)
    /// Right stick click: raise world (hold)   Left stick click: lower world (hold)
    /// Both grips: reset alignment
    /// </summary>
    public class SplatSpikeController : MonoBehaviour
    {
        [Tooltip("SPZ export to load. Relative paths resolve against the project root (TestData/ is git-ignored).")]
        public string spzPath = "TestData/Splats/worldlabs_test.spz";
        [Tooltip("Optional collider GLB exported with the same world (leave empty to skip). Relative paths resolve against the project root.")]
        public string colliderGlbPath = "";
        public SourceCoordinates sourceCoordinates = SourceCoordinates.RDF;
        public WorldAlignment alignment = WorldAlignment.Identity;

        [Header("Wiring")]
        public SplatWorldLoader loader;
        public Camera hudCamera;

        TextMesh _hud;
        InputAction _nextCoords, _flip, _scaleUp, _scaleDown, _raise, _lower, _gripL, _gripR;
        readonly float[] _frames = new float[240];
        int _frameIdx, _frameCount;
        float _logTimer;
        string _status = "Starting…";
        static readonly SourceCoordinates[] Cycle =
        {
            SourceCoordinates.RDF, SourceCoordinates.RUB, SourceCoordinates.RUF, SourceCoordinates.RDB,
            SourceCoordinates.LDF, SourceCoordinates.LUF, SourceCoordinates.LUB, SourceCoordinates.LDB,
        };

        void Awake()
        {
            if (loader == null) loader = GetComponent<SplatWorldLoader>();
            _nextCoords = Button("<XRController>{RightHand}/primaryButton");
            _flip = Button("<XRController>{RightHand}/secondaryButton");
            _scaleUp = Button("<XRController>{LeftHand}/primaryButton");
            _scaleDown = Button("<XRController>{LeftHand}/secondaryButton");
            _raise = Button("<XRController>{RightHand}/thumbstickClicked");
            _lower = Button("<XRController>{LeftHand}/thumbstickClicked");
            _gripL = Button("<XRController>{LeftHand}/gripPressed");
            _gripR = Button("<XRController>{RightHand}/gripPressed");
        }

        static InputAction Button(string binding)
        {
            var a = new InputAction(binding: binding, type: InputActionType.Button);
            a.Enable();
            return a;
        }

        IEnumerator Start()
        {
            BuildHud();
            _status = "Loading…";
            yield return null;
            yield return null;
            Load();
            if (!string.IsNullOrWhiteSpace(colliderGlbPath))
            {
                var t = loader.LoadColliderAsync(Resolve(colliderGlbPath));
                while (!t.IsCompleted) yield return null;
                if (t.IsFaulted) MaliangLog.Error("Spike", t.Exception);
            }
        }

        static string Resolve(string path) =>
            System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", path));

        static bool ApiSupportsGsplat =>
            SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Vulkan ||
            SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D12;

        void Load()
        {
            if (!ApiSupportsGsplat)
                MaliangLog.Error("Spike", $"Graphics API is {SystemInfo.graphicsDeviceType}; Gsplat needs Vulkan or Direct3D12 (restart Unity after changing Player Settings > Graphics APIs).");
            try
            {
                loader.LoadSpz(Resolve(spzPath), sourceCoordinates);
                loader.Align(alignment);
                _status = $"Loaded in {loader.LastLoadSeconds:F1}s";
            }
            catch (Exception e)
            {
                _status = "LOAD FAILED: " + e.Message;
                MaliangLog.Error("Spike", e);
            }
        }

        void Update()
        {
            TrackFrame();
            HandleInput();
            UpdateHud();
        }

        void HandleInput()
        {
            bool changed = false;
            float dt = Time.unscaledDeltaTime;

            if (_gripL.IsPressed() && _gripR.IsPressed())
            {
                alignment = WorldAlignment.Identity;
                changed = true;
            }
            if (_flip.WasPressedThisFrame())
            {
                alignment.extraEulerRotation.y = (alignment.extraEulerRotation.y + 180f) % 360f;
                changed = true;
            }
            if (_scaleUp.IsPressed()) { alignment.metricScaleFactor = Mathf.Max(0.01f, alignment.metricScaleFactor) * (1f + 0.6f * dt); changed = true; }
            if (_scaleDown.IsPressed()) { alignment.metricScaleFactor = Mathf.Max(0.01f, alignment.metricScaleFactor) / (1f + 0.6f * dt); changed = true; }
            if (_raise.IsPressed()) { alignment.groundPlaneOffset += 0.5f * dt; changed = true; }
            if (_lower.IsPressed()) { alignment.groundPlaneOffset -= 0.5f * dt; changed = true; }
            if (changed && loader.HasWorld) loader.Align(alignment);

            if (_nextCoords.WasPressedThisFrame())
            {
                int i = Array.IndexOf(Cycle, sourceCoordinates);
                sourceCoordinates = Cycle[(i + 1) % Cycle.Length];
                _status = "Reloading as " + sourceCoordinates + "…";
                Load();
            }
        }

        void TrackFrame()
        {
            _frames[_frameIdx] = Time.unscaledDeltaTime;
            _frameIdx = (_frameIdx + 1) % _frames.Length;
            _frameCount = Mathf.Min(_frameCount + 1, _frames.Length);

            _logTimer += Time.unscaledDeltaTime;
            if (_logTimer >= 5f && loader.HasWorld)
            {
                _logTimer = 0f;
                Stats(out float avg, out float worst);
                MaliangLog.Info("Spike", $"fps avg={1f / avg:F1} ({avg * 1000f:F1} ms) worst={worst * 1000f:F1} ms splats={loader.LoadedSplats:N0} " +
                                         $"coords={sourceCoordinates} scale={alignment.metricScaleFactor:F3} y={alignment.groundPlaneOffset:F2} rotY={alignment.extraEulerRotation.y:F0}");
            }
        }

        void Stats(out float avg, out float worst)
        {
            float sum = 0f; worst = 0f;
            for (int i = 0; i < _frameCount; i++) { sum += _frames[i]; worst = Mathf.Max(worst, _frames[i]); }
            avg = _frameCount > 0 ? sum / _frameCount : 0.0167f;
        }

        void BuildHud()
        {
            var cam = hudCamera != null ? hudCamera : Camera.main;
            var go = new GameObject("SpikeHUD");
            if (cam != null) go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, -0.12f, 0.9f);
            _hud = go.AddComponent<TextMesh>();
            _hud.anchor = TextAnchor.UpperCenter;
            _hud.alignment = TextAlignment.Center;
            _hud.characterSize = 0.012f;
            _hud.fontSize = 48;
            _hud.color = new Color(1f, 0.95f, 0.6f);
        }

        void UpdateHud()
        {
            if (_hud == null) return;
            Stats(out float avg, out float worst);
            var sb = new StringBuilder();
            sb.AppendLine($"{1f / avg:F0} fps  {avg * 1000f:F1} ms  worst {worst * 1000f:F1} ms");
            sb.AppendLine($"{loader.LoadedSplats:N0} splats  {sourceCoordinates}  rendering={loader.IsRendering}  bounds={loader.BoundsSize.x:F1}x{loader.BoundsSize.y:F1}x{loader.BoundsSize.z:F1}");
            sb.AppendLine($"scale {alignment.metricScaleFactor:F2}  y {alignment.groundPlaneOffset:F2}  rotY {alignment.extraEulerRotation.y:F0}");
            sb.AppendLine(_status);
            sb.Append($"{SystemInfo.graphicsDeviceType} | {XRSettings.loadedDeviceName}");
            if (!ApiSupportsGsplat) sb.Append("\nWRONG GRAPHICS API - NEED VULKAN/D3D12, RESTART UNITY");
            _hud.text = sb.ToString();
            _hud.color = ApiSupportsGsplat ? new Color(1f, 0.95f, 0.6f) : new Color(1f, 0.3f, 0.3f);
        }

        void OnDestroy()
        {
            foreach (var a in new[] { _nextCoords, _flip, _scaleUp, _scaleDown, _raise, _lower, _gripL, _gripR })
                a?.Dispose();
        }
    }
}
