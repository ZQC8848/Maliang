using UnityEngine;

namespace Maliang.Core
{
    /// <summary>
    /// Plays a sound when this body lands on something (a fallen scroll rod clattering on the desk). Fires once per
    /// <see cref="Arm"/>, louder for harder hits; soft touches are ignored.
    /// </summary>
    public class ImpactSound : MonoBehaviour
    {
        public string sound;
        [Tooltip("Impacts slower than this (m/s) make no sound.")]
        public float minSpeed = 0.3f;
        [Tooltip("Impact speed (m/s) for full volume.")]
        public float fullSpeed = 2.5f;

        bool _armed;

        public void Arm() => _armed = true;

        void OnCollisionEnter(Collision collision)
        {
            float speed = collision.relativeVelocity.magnitude;
            if (!_armed || speed < minSpeed) return;
            _armed = false;
            Sfx.Play(sound, collision.GetContact(0).point, Mathf.Clamp01(speed / fullSpeed));
        }
    }
}
