using System;
using System.Collections;
using System.Collections.Generic;
using Maliang.Core;
using Maliang.Drawing;
using Maliang.Effects;
using Maliang.VR;
using UnityEngine;

namespace Maliang.Ritual
{
    /// <summary>
    /// Burning the hovering scroll (Phase 4). A candle flame held against the paper for <see cref="igniteTime"/> lights
    /// it at the touching point; after that the candle no longer affects this scroll. More points catch over time, some
    /// as embers jumping just ahead of the fire, some anywhere on the paper: each first browns as a scorch spot, then
    /// opens a hole of its own. Every hole spreads at the same speed.
    ///
    /// <see cref="Progress"/> (0..1, never goes back) is the burned share of the paper; <see cref="BurnPacer"/> sets
    /// <see cref="TargetProgress"/> from the summoning job (with its 40% hold point). The fire's own clock follows it:
    /// when the target stops, the fire stops and smoulders. On failure the fire is put out (<see cref="Extinguish"/>)
    /// and the remnant later crumbles to ash (<see cref="Crumble"/>).
    /// </summary>
    public class ScrollBurn : MonoBehaviour
    {
        public ScrollRitual ritual;
        public InkCanvas canvas;
        public ScrollUnroll unroll;

        [Header("Ignition")]
        [Tooltip("The flame must stay this close to the paper (m)...")]
        public float touchDistance = 0.02f;
        [Tooltip("...for this long (s) to set it alight. Shorter touches only scorch it.")]
        public float igniteTime = 0.35f;

        [Header("Fire")]
        [Tooltip("Speed of every burning edge, metres per second of the fire's clock.")]
        public float spreadSpeed = 0.03f;
        [Tooltip("Fire clock seconds between new points catching (random within the range).")]
        public Vector2 newPointInterval = new Vector2(1.2f, 3f);
        [Tooltip("Share of new points that jump just ahead of the fire (the rest catch anywhere).")]
        [Range(0f, 1f)] public float jumpShare = 0.65f;
        [Tooltip("How far ahead of the edge a jumping ember lands (m).")]
        public Vector2 jumpDistance = new Vector2(0.025f, 0.1f);
        [Tooltip("A new point browns for this long (fire clock s) before it catches.")]
        public float scorchLead = 2f;
        [Tooltip("No new points once this share of the paper has burned.")]
        [Range(0f, 1f)] public float newPointsUntil = 0.85f;
        [Tooltip("The fire clock catches up with the target at most this many times real speed.")]
        public float maxClockRate = 4f;

        [Header("Effects")]
        public Material flameMaterial;   // Burn Flame (flame shader with occlusion, reads against the bright sky)
        public Material emberMaterial;   // Burn Ember
        public Material ashMaterial;     // Burn Ash
        public Material smokeMaterial;   // Burn Smoke
        public Color lightColor = new Color(1f, 0.55f, 0.25f);
        public float lightIntensity = 1.1f;
        [Tooltip("Front samples per second per metre of burning edge.")]
        public float flameDensity = 420f;

        public const int MaxPoints = 16;
        const float FinishMargin = 0.5f;
        const int GridX = 64, GridY = 32;

        public bool IsBurning { get; private set; }
        public bool IsBurnedAway { get; private set; }
        /// <summary>Burned share of the paper (0..1); never goes back.</summary>
        public float Progress { get; private set; }
        /// <summary>Where the burn is heading (set by <see cref="BurnPacer"/>).</summary>
        public float TargetProgress { get; set; }
        /// <summary>Waiting at the hold point: the fire smoulders and its edge glows in slow breaths.</summary>
        public bool Holding { get; set; }
        /// <summary>Put out (failure): the fire no longer advances.</summary>
        public bool IsExtinguished { get; private set; }
        /// <summary>The canvas UV the candle lit.</summary>
        public Vector2 IgnitionUv { get; private set; }

        public event Action<ScrollBurn> Ignited;
        public event Action<ScrollBurn> BurnedAway;

        struct BurnPoint
        {
            public Vector2 pos;   // canvas metres from the centre
            public float start;   // fire clock time it catches
            public float speed;   // m per clock second
            public bool caught;   // has passed its start (for the pop as it catches)
        }

        static readonly int BurnPointsId = Shader.PropertyToID("_BurnPoints");
        static readonly int BurnCountId = Shader.PropertyToID("_BurnCount");
        static readonly int CanvasSizeId = Shader.PropertyToID("_CanvasSize");
        static readonly int GlowGainId = Shader.PropertyToID("_GlowGain");
        static readonly int CoolId = Shader.PropertyToID("_Cool");
        static readonly int CrumbleId = Shader.PropertyToID("_Crumble");

        readonly List<BurnPoint> _points = new List<BurnPoint>();
        readonly Vector4[] _shaderPoints = new Vector4[MaxPoints];
        readonly float[] _cellTime = new float[GridX * GridY];   // fire clock time each cell burns
        readonly float[] _sorted = new float[GridX * GridY];
        float _clock, _nextPoint, _contact, _frontLength, _heat, _glowGain = 1f;
        Coroutine _ending;
        Material _mat;
        Vector2 _size;

        // Effects (live outside the scroll so they outlast it)
        GameObject _fx;
        ParticleSystem _flames, _embers, _ash, _smoke;
        Light _light;
        AudioSource _burnLoop;
        float _emitDebt;

        // Rods fall once the paper next to them has burned
        struct RodState { public Transform rod, wrap, parent; public Vector3 pos; public Quaternion rot; public Vector3 scale; public bool fallen, hadCollider; }
        RodState[] _rods;

        void Awake()
        {
            if (ritual == null) ritual = GetComponent<ScrollRitual>();
            if (canvas == null && ritual != null) canvas = ritual.canvas;
            if (unroll == null && ritual != null) unroll = ritual.unroll;
        }

        void Start()
        {
            // InkCanvas makes its own material instance in Awake.
            _mat = canvas.GetComponent<Renderer>().sharedMaterial;
            _size = canvas.size;
            ClearShader();
        }

        void Update()
        {
            if (IsBurnedAway || IsExtinguished) return;
            if (!IsBurning)
            {
                if (ritual != null && ritual.CanIgnite) CheckCandles();
                else _contact = 0f;
                return;
            }
            // Holding: the edge glow breathes slowly, as if the fire were gathering itself.
            float gain = Holding ? 0.6f + 0.4f * Mathf.Sin(Time.time * 2.4f) : 1f;
            _glowGain = Mathf.Lerp(_glowGain, gain, Time.deltaTime * 4f);
            if (_mat != null) _mat.SetFloat(GlowGainId, _glowGain);
            AdvanceClock();
            UpdateEffects();
            UpdateRods();
            // Run a little past the last cell so the rough edges have burned through too.
            if (Progress >= 1f && _clock >= _sorted[_sorted.Length - 1] + FinishMargin) Finish();
        }

        // ------------------------------------------------------------------ ignition

        void CheckCandles()
        {
            bool touching = false;
            Vector2 uv = default;
            CandleFlame touchedBy = null;
            foreach (var flame in CandleFlame.All)
            {
                if (!flame.IsLit) continue;
                // Test along the flame, from its root to the tip, and take the point closest to the paper.
                for (int s = 0; s <= 2; s++)
                {
                    Vector3 p = Vector3.Lerp(flame.transform.position, flame.Tip.position, 0.5f + 0.25f * s);
                    if (canvas.TryProject(p, out var u, out float h) && Mathf.Abs(h) <= touchDistance)
                    {
                        touching = true;
                        uv = u;
                        touchedBy = flame;
                        break;
                    }
                }
                if (touching) break;
            }

            if (!touching)
            {
                // Cooling off: a brief touch leaves no mark.
                _contact = Mathf.Max(0f, _contact - Time.deltaTime * 2f);
                if (_contact <= 0f && _points.Count > 0) ClearShader();
                return;
            }

            _contact += Time.deltaTime;
            var tool = touchedBy.GetComponentInParent<GrabbableTool>();
            // Heating: a scorch spot browning under the flame (its radius climbs to zero as it catches).
            Vector2 pos = UvToPos(uv);
            float k = Mathf.Clamp01(_contact / igniteTime);
            _points.Clear();
            _points.Add(new BurnPoint { pos = pos, start = 0f, speed = spreadSpeed });
            _clock = Mathf.Lerp(-scorchLead, 0f, k);
            PushShader();
            if (tool != null) tool.Haptic(0.08f + 0.12f * k, 0.03f);

            if (_contact >= igniteTime) Ignite(uv, tool);
        }

        /// <summary>Sets the scroll alight at <paramref name="uv"/> (also used by debug keys and tests).</summary>
        public void Ignite(Vector2 uv, GrabbableTool candle = null)
        {
            if (IsBurning || IsBurnedAway) return;
            IsBurning = true;
            IgnitionUv = uv;
            _points.Clear();
            _points.Add(new BurnPoint { pos = UvToPos(uv), start = 0f, speed = spreadSpeed });
            _clock = 0f;
            _heat = 1f;
            Progress = 0f;
            TargetProgress = 0f;
            _nextPoint = UnityEngine.Random.Range(newPointInterval.x, newPointInterval.y);
            RebuildCells();
            BuildEffects();
            CacheRods();
            if (candle != null) candle.Haptic(0.6f, 0.12f);
            Sfx.Play(SfxId.Ignite, PosToWorld(UvToPos(uv)));
            MaliangLog.Info("Burn", $"Scroll caught fire at uv {uv.x:F2},{uv.y:F2}.");
            ritual?.OnBurnStarted();
            Ignited?.Invoke(this);
        }

        // ------------------------------------------------------------------ fire clock

        void AdvanceClock()
        {
            float target = Mathf.Max(Progress, Mathf.Clamp01(TargetProgress));
            float targetClock = target >= 1f ? _sorted[_sorted.Length - 1] + FinishMargin : Quantile(target);
            float before = _clock;
            if (targetClock > _clock)
                _clock = Mathf.Min(targetClock, _clock + Time.deltaTime * maxClockRate);
            // How hard the fire is burning: full while it moves (a slow job still burns properly), a smoulder while it
            // waits at the hold point.
            float pace = Time.deltaTime > 0f ? (_clock - before) / Time.deltaTime : 0f;
            float heat = Holding ? 0f : pace > 0.001f ? 1f : 0.3f;
            _heat = Mathf.Lerp(_heat, heat, Time.deltaTime * 2f);
            Progress = BurnedShare(_clock);

            // A soft pop as each new point catches.
            for (int i = 1; i < _points.Count; i++)
            {
                var p = _points[i];
                if (p.caught || _clock < p.start) continue;
                p.caught = true;
                _points[i] = p;
                Sfx.Play(SfxId.EmberPop, PosToWorld(p.pos));
            }

            if (_points.Count < MaxPoints && Progress < newPointsUntil && _clock >= _nextPoint)
            {
                SpawnPoint();
                _nextPoint = _clock + UnityEngine.Random.Range(newPointInterval.x, newPointInterval.y);
            }
            PushShader();
        }

        void SpawnPoint()
        {
            for (int attempt = 0; attempt < 24; attempt++)
            {
                Vector2 pos;
                if (UnityEngine.Random.value < jumpShare)
                {
                    // An ember lands just ahead of a burning edge.
                    var from = _points[UnityEngine.Random.Range(0, _points.Count)];
                    float r = Radius(from);
                    if (r <= 0f) continue;
                    float a = UnityEngine.Random.value * Mathf.PI * 2f;
                    pos = from.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r + UnityEngine.Random.Range(jumpDistance.x, jumpDistance.y));
                }
                else pos = new Vector2(UnityEngine.Random.Range(-0.5f, 0.5f) * _size.x, UnityEngine.Random.Range(-0.5f, 0.5f) * _size.y);

                if (Mathf.Abs(pos.x) > _size.x * 0.5f - 0.01f || Mathf.Abs(pos.y) > _size.y * 0.5f - 0.01f) continue;
                // It must still be paper when it catches: well outside every hole by then.
                float start = _clock + scorchLead;
                if (BurnTimeAt(pos) < start + 0.5f) continue;

                _points.Add(new BurnPoint { pos = pos, start = start, speed = spreadSpeed * UnityEngine.Random.Range(0.8f, 1.15f) });
                RebuildCells();
                return;
            }
        }

        float Radius(BurnPoint p) => (_clock - p.start) * p.speed;

        /// <summary>When the fire reaches <paramref name="pos"/> (fire clock), ignoring the edge noise.</summary>
        float BurnTimeAt(Vector2 pos)
        {
            float t = float.MaxValue;
            foreach (var p in _points) t = Mathf.Min(t, p.start + Vector2.Distance(pos, p.pos) / p.speed);
            return t;
        }

        void RebuildCells()
        {
            for (int y = 0; y < GridY; y++)
            for (int x = 0; x < GridX; x++)
                _cellTime[y * GridX + x] = BurnTimeAt(CellPos(x, y));
            Array.Copy(_cellTime, _sorted, _cellTime.Length);
            Array.Sort(_sorted);
        }

        Vector2 CellPos(int x, int y) => new Vector2(((x + 0.5f) / GridX - 0.5f) * _size.x, ((y + 0.5f) / GridY - 0.5f) * _size.y);

        /// <summary>Fire clock time by which <paramref name="share"/> of the paper has burned.</summary>
        float Quantile(float share)
        {
            int i = Mathf.Clamp(Mathf.CeilToInt(share * _sorted.Length) - 1, 0, _sorted.Length - 1);
            return share <= 0f ? 0f : _sorted[i];
        }

        float BurnedShare(float clock)
        {
            // Binary search in the sorted cell times.
            int lo = 0, hi = _sorted.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_sorted[mid] <= clock) lo = mid + 1; else hi = mid;
            }
            return lo / (float)_sorted.Length;
        }

        Vector2 UvToPos(Vector2 uv) => new Vector2((uv.x - 0.5f) * _size.x, (uv.y - 0.5f) * _size.y);
        Vector3 PosToWorld(Vector2 pos) => canvas.transform.TransformPoint(new Vector3(pos.x, 0f, pos.y));

        // ------------------------------------------------------------------ shader

        void PushShader()
        {
            if (_mat == null) return;
            int n = Mathf.Min(_points.Count, MaxPoints);
            for (int i = 0; i < MaxPoints; i++)
            {
                if (i < n)
                {
                    var p = _points[i];
                    // Not caught yet: a negative radius, which the shader shows as a browning spot.
                    float r = Mathf.Max(Radius(p), -scorchLead * p.speed);
                    _shaderPoints[i] = new Vector4(p.pos.x, p.pos.y, r, 0f);
                }
                else _shaderPoints[i] = Vector4.zero;
            }
            _mat.SetVector(CanvasSizeId, new Vector4(_size.x, _size.y, 0f, 0f));
            _mat.SetVectorArray(BurnPointsId, _shaderPoints);
            _mat.SetFloat(BurnCountId, n);
        }

        void ClearShader()
        {
            _points.Clear();
            if (_mat == null) return;
            Array.Clear(_shaderPoints, 0, _shaderPoints.Length);
            _mat.SetVectorArray(BurnPointsId, _shaderPoints);
            _mat.SetFloat(BurnCountId, 0f);
        }

        // ------------------------------------------------------------------ effects

        void BuildEffects()
        {
            if (_fx != null) Destroy(_fx);
            _fx = new GameObject("Burn FX (" + name + ")");
            _flames = MakeSystem("Flames", flameMaterial, 600);
            var flameMain = _flames.main;
            flameMain.startSize3D = true;
            _embers = MakeSystem("Embers", emberMaterial, 300);
            _ash = MakeSystem("Ash", ashMaterial, 300);
            _smoke = MakeSystem("Smoke", smokeMaterial != null ? smokeMaterial : ashMaterial, 80);

            var noise = _embers.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 1.5f;
            noise = _ash.noise;
            noise.enabled = true;
            noise.strength = 0.06f;
            noise.frequency = 0.8f;
            var ashMain = _ash.main;
            ashMain.gravityModifier = 0.015f;
            var size = _smoke.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.6f));
            var flameSize = _flames.sizeOverLifetime;
            flameSize.enabled = true;
            flameSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.3f)));

            var lightGo = new GameObject("Burn Light");
            lightGo.transform.SetParent(_fx.transform, false);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = lightColor;
            _light.range = 1.6f;
            _light.intensity = 0f;
            _light.shadows = LightShadows.None;
            _burnLoop = Sfx.Loop(SfxId.BurnLoop, lightGo.transform); // follows the fire like its light
        }

        ParticleSystem MakeSystem(string label, Material mat, int max)
        {
            var go = new GameObject(label);
            go.transform.SetParent(_fx.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.maxParticles = max;
            main.startSpeed = 0f;
            var emission = ps.emission;
            emission.enabled = false; // emitted by hand along the burning edge
            var shape = ps.shape;
            shape.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeGradient();
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static Gradient FadeGradient()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        void UpdateEffects()
        {
            if (_fx == null) return;
            // Burning edge length: circumference of every caught circle, minus the parts inside other holes or off the paper.
            float edge = 0f;
            Vector3 centre = Vector3.zero;
            int samples = 0;
            float burningShare = 1f - Progress; // the fire dies down as the paper runs out
            float budget = 0f;
            foreach (var p in _points)
            {
                float r = Radius(p);
                if (r > 0f) budget += 2f * Mathf.PI * r;
            }
            _emitDebt += budget * flameDensity * Mathf.Lerp(0.35f, 1f, _heat) * Time.deltaTime;
            int tries = Mathf.Min(Mathf.FloorToInt(_emitDebt), 80);
            _emitDebt -= tries;
            float growing = Mathf.Lerp(0.3f, 1f, _heat); // smaller, fewer flames while the fire is held back

            // Lift the effects off the paper towards the viewer, or the paper hides half of every flame.
            Vector3 normal = FacingNormal();

            for (int i = 0; i < tries; i++)
            {
                if (!SampleFront(out var pos)) continue;
                samples++;
                Vector3 w = PosToWorld(pos) + normal * 0.006f;
                centre += w;
                EmitFlame(w, growing);
                float roll = UnityEngine.Random.value;
                if (roll < 0.22f) EmitEmber(w);
                else if (roll < 0.34f) EmitAsh(w);
                else if (roll < 0.38f) EmitSmoke(w);
            }
            if (tries > 0) edge = budget * samples / tries;
            _frontLength = Mathf.Lerp(_frontLength, edge, Time.deltaTime * 4f);

            Sfx.LoopVolume(_burnLoop, SfxId.BurnLoop, Mathf.Lerp(0.3f, 1f, _heat) * Mathf.Clamp01(_frontLength / 0.35f + 0.25f), 4f);
            if (samples > 0) _light.transform.position = Vector3.Lerp(_light.transform.position, centre / samples, Time.deltaTime * 6f);
            float flicker = 0.75f + 0.5f * Mathf.PerlinNoise(Time.time * 9f, 3.3f);
            _light.intensity = lightIntensity * Mathf.Clamp01(_frontLength / 0.6f) * flicker * (0.5f + 0.5f * burningShare + 0.3f);
        }

        /// <summary>A random point on the burning edge (on paper, not inside another hole).</summary>
        bool SampleFront(out Vector2 pos)
        {
            pos = default;
            var p = _points[UnityEngine.Random.Range(0, _points.Count)];
            float r = Radius(p);
            if (r <= 0.002f) return false;
            float a = UnityEngine.Random.value * Mathf.PI * 2f;
            pos = p.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r + 0.002f);
            if (Mathf.Abs(pos.x) > _size.x * 0.5f || Mathf.Abs(pos.y) > _size.y * 0.5f) return false;
            foreach (var q in _points)
                if (Vector2.Distance(pos, q.pos) < Radius(q)) return false;
            return true;
        }

        void EmitFlame(Vector3 at, float strength)
        {
            var e = new ParticleSystem.EmitParams
            {
                position = at + UnityEngine.Random.insideUnitSphere * 0.003f,
                velocity = Vector3.up * UnityEngine.Random.Range(0.03f, 0.07f),
                startLifetime = UnityEngine.Random.Range(0.22f, 0.4f),
                startSize3D = new Vector3(UnityEngine.Random.Range(0.016f, 0.028f), UnityEngine.Random.Range(0.035f, 0.06f), 1f) * strength,
                startColor = Color.Lerp(new Color(1f, 0.85f, 0.5f), new Color(1f, 0.45f, 0.15f), UnityEngine.Random.value),
            };
            _flames.Emit(e, 1);
        }

        void EmitEmber(Vector3 at)
        {
            var e = new ParticleSystem.EmitParams
            {
                position = at,
                velocity = Vector3.up * UnityEngine.Random.Range(0.06f, 0.18f) + UnityEngine.Random.insideUnitSphere * 0.04f,
                startLifetime = UnityEngine.Random.Range(0.6f, 1.5f),
                startSize = UnityEngine.Random.Range(0.002f, 0.004f),
                startColor = new Color(1f, UnityEngine.Random.Range(0.45f, 0.75f), 0.2f),
            };
            _embers.Emit(e, 1);
        }

        void EmitAsh(Vector3 at, bool falling = false)
        {
            float g = UnityEngine.Random.Range(0.08f, 0.3f);
            var e = new ParticleSystem.EmitParams
            {
                position = at,
                velocity = (falling ? Vector3.down * UnityEngine.Random.Range(0.02f, 0.1f) : Vector3.up * UnityEngine.Random.Range(0.02f, 0.08f))
                           + UnityEngine.Random.insideUnitSphere * 0.02f,
                startLifetime = UnityEngine.Random.Range(2f, 3.5f),
                startSize = UnityEngine.Random.Range(0.003f, 0.007f),
                rotation = UnityEngine.Random.value * 360f,
                startColor = new Color(g, g * 0.95f, g * 0.9f, 0.9f),
            };
            _ash.Emit(e, 1);
        }

        void EmitSmoke(Vector3 at, float scale = 1f)
        {
            var e = new ParticleSystem.EmitParams
            {
                position = at + Vector3.up * 0.02f,
                velocity = Vector3.up * UnityEngine.Random.Range(0.06f, 0.12f) + UnityEngine.Random.insideUnitSphere * 0.015f,
                startLifetime = UnityEngine.Random.Range(1.8f, 3f) * scale,
                startSize = UnityEngine.Random.Range(0.04f, 0.09f) * scale,
                rotation = UnityEngine.Random.value * 360f,
                startColor = new Color(0.4f, 0.38f, 0.36f, 0.1f),
            };
            _smoke.Emit(e, 1);
        }

        // ------------------------------------------------------------------ rods

        void CacheRods()
        {
            if (_rods != null || unroll == null) return;
            _rods = new[] { Rod(unroll.rodLeft, unroll.wrapLeft), Rod(unroll.rodRight, unroll.wrapRight) };
        }

        static RodState Rod(Transform rod, Transform wrap) => new RodState
        {
            rod = rod, wrap = wrap, parent = rod != null ? rod.parent : null,
            pos = rod != null ? rod.localPosition : default, rot = rod != null ? rod.localRotation : default,
            scale = rod != null ? rod.localScale : Vector3.one,
            hadCollider = rod != null && rod.GetComponent<Collider>() != null,
        };

        void UpdateRods()
        {
            if (_rods == null) return;
            for (int i = 0; i < _rods.Length; i++)
            {
                if (_rods[i].fallen || _rods[i].rod == null) continue;
                // The paper column next to this rod has burned through.
                int col = i == 0 ? 0 : GridX - 1;
                bool free = true;
                for (int y = 0; y < GridY && free; y++) free = _cellTime[y * GridX + col] <= _clock;
                if (free) DropRod(ref _rods[i]);
            }
        }

        void DropRod(ref RodState s)
        {
            s.fallen = true;
            if (s.wrap != null) s.wrap.gameObject.SetActive(false);
            s.rod.SetParent(null, true);
            var body = s.rod.gameObject.AddComponent<Rigidbody>();
            body.mass = 0.08f;
            body.angularVelocity = UnityEngine.Random.insideUnitSphere * 2f;
            if (s.rod.GetComponent<Collider>() == null) s.rod.gameObject.AddComponent<CapsuleCollider>();
            var impact = s.rod.GetComponent<ImpactSound>();
            if (impact == null) impact = s.rod.gameObject.AddComponent<ImpactSound>();
            impact.sound = SfxId.RodFall;
            impact.Arm();
            var fade = s.rod.gameObject.AddComponent<FadeAndDestroy>();
            fade.delay = 4f;
            fade.deactivateOnly = true; // kept so a reset can put it back
            MaliangLog.Info("Burn", $"{s.rod.name} fell.");
        }

        // ------------------------------------------------------------------ failure

        /// <summary>
        /// Puts the fire out (failure, Phase3Design 7.2): the burn freezes, the glowing edge fades from orange through
        /// dark red to grey over <paramref name="duration"/>, a few wisps of smoke rise, and the flames and light die.
        /// </summary>
        public void Extinguish(float duration = 1.2f)
        {
            if (!IsBurning || IsExtinguished) return;
            IsExtinguished = true;
            Holding = false;
            if (_ending != null) StopCoroutine(_ending);
            _ending = StartCoroutine(ExtinguishRoutine(duration));
        }

        IEnumerator ExtinguishRoutine(float duration)
        {
            float light0 = _light != null ? _light.intensity : 0f;
            int wisps = 0;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                if (_mat != null)
                {
                    _mat.SetFloat(CoolId, k);
                    _mat.SetFloat(GlowGainId, Mathf.Lerp(_glowGain, 1f, k));
                }
                if (_light != null) _light.intensity = light0 * (1f - k) * (1f - k);
                Sfx.LoopVolume(_burnLoop, SfxId.BurnLoop, 0f, 8f);
                // A few wisps of smoke from the dying edge, mostly at the start.
                if (_fx != null && wisps < 10 && UnityEngine.Random.value < 0.5f * (1f - k) && SampleFront(out var pos))
                {
                    EmitSmoke(PosToWorld(pos) + FacingNormal() * 0.006f, 1.6f);
                    wisps++;
                }
                yield return null;
            }
            if (_mat != null) _mat.SetFloat(CoolId, 1f);
            if (_light != null) _light.intensity = 0f;
            _ending = null;
        }

        /// <summary>The failed remnant crumbles to ash over <paramref name="duration"/>; then it is hidden.</summary>
        public void Crumble(float duration = 1.5f, Action done = null)
        {
            if (_ending != null) StopCoroutine(_ending);
            _ending = StartCoroutine(CrumbleRoutine(duration, done));
        }

        IEnumerator CrumbleRoutine(float duration, Action done)
        {
            if (_fx == null) BuildEffects();
            if (_light != null) _light.intensity = 0f;
            if (_burnLoop != null) _burnLoop.volume = 0f;
            Sfx.Play(SfxId.Crumble, canvas.transform.position);
            var rods = unroll != null ? new[] { unroll.rodLeft, unroll.rodRight } : new Transform[0];
            var rodScale = new Vector3[rods.Length];
            for (int i = 0; i < rods.Length; i++) if (rods[i] != null) rodScale[i] = rods[i].localScale;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                if (_mat != null) _mat.SetFloat(CrumbleId, k);
                for (int i = 0; i < rods.Length; i++)
                    if (rods[i] != null && rods[i].IsChildOf(transform)) rods[i].localScale = rodScale[i] * (1f - k * k);
                // Ash falling off the paper as it goes.
                for (int n = 0; n < 3; n++)
                {
                    var pos = new Vector2(UnityEngine.Random.Range(-0.5f, 0.5f) * _size.x, UnityEngine.Random.Range(-0.5f, 0.5f) * _size.y);
                    EmitAsh(PosToWorld(pos), falling: true);
                }
                yield return null;
            }
            if (_mat != null) _mat.SetFloat(CrumbleId, 1f);
            for (int i = 0; i < rods.Length; i++) if (rods[i] != null && rods[i].IsChildOf(transform)) rods[i].localScale = rodScale[i];
            IsBurning = false;
            IsBurnedAway = true;
            if (_fx != null) Destroy(_fx, 4f);
            _fx = null;
            _ending = null;
            done?.Invoke();
        }

        Vector3 FacingNormal()
        {
            var cam = Camera.main;
            Vector3 normal = canvas.transform.up;
            if (cam != null && Vector3.Dot(normal, cam.transform.position - canvas.transform.position) < 0f) normal = -normal;
            return normal;
        }

        // ------------------------------------------------------------------ end / reset

        void Finish()
        {
            IsBurning = false;
            IsBurnedAway = true;
            if (_rods != null) for (int i = 0; i < _rods.Length; i++) if (!_rods[i].fallen && _rods[i].rod != null) DropRod(ref _rods[i]);
            if (_light != null) _light.gameObject.AddComponent<FadeAndDestroy>().delay = 0f;
            Sfx.FadeOut(_burnLoop, 1f);
            _burnLoop = null;
            if (_fx != null) Destroy(_fx, 4f);
            _fx = null;
            MaliangLog.Info("Burn", "Scroll burned away.");
            ritual?.OnBurnedAway();
            BurnedAway?.Invoke(this);
        }

        /// <summary>Back to unburned paper (reset / new round): puts fallen rods back.</summary>
        public void ResetBurn()
        {
            if (_ending != null) { StopCoroutine(_ending); _ending = null; }
            IsBurning = false;
            IsBurnedAway = false;
            IsExtinguished = false;
            Holding = false;
            _glowGain = 1f;
            if (_mat != null)
            {
                _mat.SetFloat(CoolId, 0f);
                _mat.SetFloat(CrumbleId, 0f);
                _mat.SetFloat(GlowGainId, 1f);
            }
            Progress = TargetProgress = 0f;
            _contact = 0f;
            _clock = 0f;
            if (_fx != null) Destroy(_fx);
            _fx = null;
            if (_rods != null)
            {
                for (int i = 0; i < _rods.Length; i++)
                {
                    var s = _rods[i];
                    if (s.rod == null) continue;
                    var fade = s.rod.GetComponent<FadeAndDestroy>();
                    if (fade != null) Destroy(fade);
                    var body = s.rod.GetComponent<Rigidbody>();
                    if (body != null) Destroy(body);
                    s.rod.SetParent(s.parent, false);
                    s.rod.localPosition = s.pos;
                    s.rod.localRotation = s.rot;
                    s.rod.localScale = s.scale;
                    if (s.wrap != null) s.wrap.gameObject.SetActive(true);
                    var col = s.rod.GetComponent<Collider>();
                    if (col != null && !s.hadCollider) Destroy(col);
                    s.rod.gameObject.SetActive(true);
                    _rods[i].fallen = false;
                }
            }
            ClearShader();
        }

        void OnDestroy()
        {
            if (_fx != null) Destroy(_fx);
        }
    }

    /// <summary>Shrinks an object (or dims a light) after a delay, then destroys or hides it (fallen rods, the burn light).</summary>
    public class FadeAndDestroy : MonoBehaviour
    {
        public float delay = 3f;
        public float duration = 0.8f;
        [Tooltip("Hide the object instead of destroying it (and remove this component).")]
        public bool deactivateOnly;
        float _t;
        Vector3 _scale;
        Light _light;
        float _intensity;

        void Start()
        {
            _scale = transform.localScale;
            _light = GetComponent<Light>();
            if (_light != null) _intensity = _light.intensity;
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01((_t - delay) / duration);
            if (_light != null) _light.intensity = _intensity * (1f - k);
            else transform.localScale = _scale * (1f - k);
            if (k < 1f) return;
            if (!deactivateOnly) { Destroy(gameObject); return; }
            transform.localScale = _scale;
            gameObject.SetActive(false);
            Destroy(this);
        }
    }
}
