using System.Collections;
using Maliang.Drawing;
using UnityEngine;

namespace Maliang.Ritual
{
    /// <summary>
    /// Unrolling the scroll: the two rods start together in the middle, each wrapped in its half of the paper, and roll
    /// outwards to the edges. They turn as they roll along the desk, their paper wraps thin out, and the paper between
    /// them is revealed (the display shader clips it to the strip between the rods).
    /// Progress 0 = rolled up in the middle, 1 = flat and open (the layout the scene is built in).
    /// Runs in edit mode too, so a scroll saved rolled up also looks rolled up in the editor.
    /// </summary>
    [ExecuteAlways]
    public class ScrollUnroll : MonoBehaviour
    {
        public InkCanvas canvas;
        public Transform rodLeft, rodRight;
        [Tooltip("Rolled-up paper around each rod; thins to nothing as the scroll opens.")]
        public Transform wrapLeft, wrapRight;

        public float rodRadius = 0.011f;
        [Tooltip("Outer radius of each paper roll when fully rolled up (m).")]
        public float rolledRadius = 0.016f;
        [Tooltip("How far the rods sink below the paper plane to rest on the desk (m).")]
        public float restDrop = 0.005f;
        public float duration = 2f;

        static readonly int RevealHalf = Shader.PropertyToID("_RevealHalf");
        static readonly Quaternion RodRest = Quaternion.Euler(90f, 0f, 0f); // cylinder axis along the scroll's local Z

        [SerializeField, Range(0f, 1f)] float progress = 1f;
        public float Progress => progress;

        Renderer _paper;
        MaterialPropertyBlock _mpb;

        void OnEnable()
        {
            if (!Application.isPlaying && canvas != null && rodLeft != null && rodRight != null) SetProgress(progress);
        }

        bool Init()
        {
            if (canvas == null || rodLeft == null || rodRight == null) return false;
            if (_paper == null) _paper = canvas.GetComponent<Renderer>();
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            return true;
        }

        /// <summary>Plays from the current progress to open (1) or rolled (0).</summary>
        public IEnumerator Play(float to = 1f)
        {
            float from = Progress;
            float time = duration * Mathf.Abs(to - from);
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                SetProgress(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / time)));
                yield return null;
            }
            SetProgress(to);
        }

        public void SetProgress(float p)
        {
            if (!Init()) return;
            progress = Mathf.Clamp01(p);
            float half = canvas.size.x * 0.5f;

            // Paper left on each roll shrinks linearly, so the roll's cross-section area does too.
            float r = Mathf.Sqrt(rodRadius * rodRadius + (rolledRadius * rolledRadius - rodRadius * rodRadius) * (1f - Progress));
            float x = Mathf.Lerp(rolledRadius, half + rodRadius, Progress); // rod centre from the middle
            float y = r - restDrop;
            // Rolling along the desk: turned angle = distance / radius (mean radius over the unroll).
            float turned = (x - rolledRadius) / ((rolledRadius + rodRadius) * 0.5f) * Mathf.Rad2Deg;

            Place(rodRight, wrapRight, new Vector3(x, y, 0f), -turned, r);
            Place(rodLeft, wrapLeft, new Vector3(-x, y, 0f), turned, r);

            // Paper shows up to the rods' contact lines.
            _paper.GetPropertyBlock(_mpb);
            _mpb.SetFloat(RevealHalf, Mathf.Min(x, half) / canvas.size.x);
            _paper.SetPropertyBlock(_mpb);
        }

        void Place(Transform rod, Transform wrap, Vector3 localPos, float angle, float radius)
        {
            var rot = Quaternion.AngleAxis(angle, Vector3.forward) * RodRest;
            rod.localPosition = new Vector3(localPos.x, localPos.y, rod.localPosition.z);
            rod.localRotation = rot;
            if (wrap == null) return;
            wrap.localPosition = new Vector3(localPos.x, localPos.y, wrap.localPosition.z);
            wrap.localRotation = rot;
            var s = wrap.localScale;
            wrap.localScale = new Vector3(radius * 2f, s.y, radius * 2f);
            var wr = wrap.GetComponent<Renderer>();
            if (wr != null) wr.enabled = radius > rodRadius + 0.0003f; // fully unrolled: nothing left to show
        }
    }
}
