using System.Collections.Generic;
using UnityEngine;

namespace Maliang.Effects
{
    /// <summary>
    /// A candle flame: particle layers (built by the editor) plus a flickering point light.
    /// <see cref="Tip"/> is the hot point of the flame; the burning ritual (Phase 4) tests the scroll against it.
    /// </summary>
    public class CandleFlame : MonoBehaviour
    {
        public Light flameLight;
        public ParticleSystem[] layers;
        [Tooltip("Top of the visible flame, used as the burning point.")]
        public Transform tip;

        [Header("Flicker")]
        public float baseIntensity = 0.8f;
        [Range(0f, 1f)] public float flickerAmount = 0.3f;
        public float flickerSpeed = 7f;
        [Tooltip("How far the light wanders around the flame (m).")]
        public float lightWander = 0.004f;

        /// <summary>Every enabled flame (the scrolls test these for burning).</summary>
        public static readonly List<CandleFlame> All = new List<CandleFlame>();

        public bool IsLit { get; private set; } = true;
        public Transform Tip => tip != null ? tip : transform;

        Vector3 _lightRest;
        float _seed;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Awake()
        {
            _seed = Random.value * 100f;
            if (flameLight != null) _lightRest = flameLight.transform.localPosition;
        }

        void Update()
        {
            if (!IsLit || flameLight == null) return;
            float t = Time.time * flickerSpeed + _seed;
            // Two octaves of noise: a slow breathing and a fast flutter.
            float n = Mathf.PerlinNoise(t, 0.37f) * 0.7f + Mathf.PerlinNoise(t * 3.1f, 5.1f) * 0.3f;
            flameLight.intensity = baseIntensity * (1f - flickerAmount + 2f * flickerAmount * n);
            flameLight.transform.localPosition = _lightRest + new Vector3(
                (Mathf.PerlinNoise(t * 0.8f, 11.3f) - 0.5f) * 2f * lightWander,
                0f,
                (Mathf.PerlinNoise(t * 0.8f, 17.9f) - 0.5f) * 2f * lightWander);
        }

        public void Ignite()
        {
            IsLit = true;
            foreach (var ps in layers) if (ps != null) ps.Play(true);
            if (flameLight != null) flameLight.enabled = true;
        }

        public void Extinguish()
        {
            IsLit = false;
            foreach (var ps in layers) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (flameLight != null) flameLight.enabled = false;
        }
    }
}
