using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Maliang.Loading
{
    /// <summary>
    /// A summoned object in the room (Phase3Design 5.4, 5.5). Animated: loops the first clip; on grab plays the second
    /// clip once (when there is one) and returns to the first. Sound by trigger: on_spawn plays once, on_grab plays on
    /// each grab (with a cool-down), loop plays continuously. It stays where it is let go (no gravity, no return).
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class SummonedObject : MonoBehaviour
    {
        public Animation anim;
        public string[] clips = new string[0];
        public AudioSource audioSource;
        public string soundTrigger;      // on_spawn | on_grab | loop
        public float grabSoundCooldown = 1.5f;
        public ProceduralMotion motion;

        XRGrabInteractable _grab;
        float _lastSound = -99f;

        void Awake() => _grab = GetComponent<XRGrabInteractable>();

        void OnEnable()
        {
            _grab.selectEntered.AddListener(OnGrab);
            _grab.selectExited.AddListener(OnRelease);
        }

        void OnDisable()
        {
            _grab.selectEntered.RemoveListener(OnGrab);
            _grab.selectExited.RemoveListener(OnRelease);
        }

        void Start()
        {
            if (anim != null && clips.Length > 0) Loop(clips[0]);
            if (soundTrigger == "on_spawn") PlaySound();
            else if (soundTrigger == "loop") StartLoop();
        }

        /// <summary>Attaches a sound that arrived after the object was summoned.</summary>
        public void SetSound(AudioClip clip, string trigger)
        {
            if (clip == null) return;
            if (audioSource == null) audioSource = SummonAudio.Create(gameObject);
            audioSource.clip = clip;
            soundTrigger = trigger;
            if (trigger == "loop") StartLoop();
        }

        void OnGrab(SelectEnterEventArgs args)
        {
            if (motion != null) motion.Paused = true;
            if (anim != null && clips.Length > 1)
            {
                anim[clips[1]].wrapMode = WrapMode.Once;
                anim.CrossFade(clips[1], 0.2f);
                anim.CrossFadeQueued(clips[0], 0.2f, QueueMode.CompleteOthers);
            }
            if (soundTrigger == "on_grab" && Time.time - _lastSound >= grabSoundCooldown) PlaySound();
        }

        void OnRelease(SelectExitEventArgs args)
        {
            if (_grab.isSelected) return;
            if (motion != null)
            {
                motion.Rebase();
                motion.Paused = false;
            }
        }

        void Loop(string clip)
        {
            anim[clip].wrapMode = WrapMode.Loop;
            anim.Play(clip);
        }

        void PlaySound()
        {
            if (audioSource == null || audioSource.clip == null) return;
            _lastSound = Time.time;
            audioSource.loop = false;
            audioSource.Play();
        }

        void StartLoop()
        {
            if (audioSource == null || audioSource.clip == null) return;
            audioSource.loop = true;
            if (!audioSource.isPlaying) audioSource.Play();
        }
    }

    static class SummonAudio
    {
        /// <summary>A 3D source: full spatial blend, logarithmic rolloff, audible to about 6 m.</summary>
        public static AudioSource Create(GameObject go)
        {
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Logarithmic;
            a.minDistance = 0.4f;
            a.maxDistance = 6f;
            a.dopplerLevel = 0f;
            return a;
        }
    }
}
