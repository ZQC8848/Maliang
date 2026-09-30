using Maliang.VR;
using UnityEngine;

namespace Maliang.Drawing
{
    /// <summary>
    /// The calligraphy brush (port of Node_Brush PenBase/BonePen). While held, the nib is projected onto the
    /// fixed horizontal canvas; within <see cref="contactHeight"/> above the paper it paints, lower = thicker.
    /// Dip the nib into an <see cref="InkPot"/> to change colour.
    /// </summary>
    public class BrushPen : GrabbableTool
    {
        public InkCanvas canvas;
        [Tooltip("Tip of the brush. Painting uses its position.")]
        public Transform nib;
        [Tooltip("Trigger collider on the nib, used by ink pots.")]
        public Collider nibCollider;

        [Header("Ink")]
        public Color inkColor = new Color(0.08f, 0.07f, 0.07f);
        [Tooltip("Brush tip textures (Node_Brush styles). The first one is used unless changed.")]
        public Texture[] styles;
        public int styleIndex;
        [Tooltip("Renderer whose bristle material shows the current ink colour.")]
        public Renderer hairRenderer;
        [Tooltip("Material slot of the bristles on hairRenderer (the split brush mesh puts them in slot 1).")]
        public int hairMaterialIndex = 1;

        // Pressure: the paper has no physical resistance in VR, so "pressing" means pushing the nib *into* the paper.
        // Painting starts at contact; how deep the nib goes below the surface sets the stroke size.
        //
        //   height (m) :  +contactHeight ... 0 ........... -pressDepth ........ -maxPenetration
        //   stroke     :  thinnest (touch)               thickest              stops painting below this
        [Header("Pressure")]
        [Tooltip("Tolerance above the paper (m) that still counts as touching (tracking jitter). Keep small: 0–3 mm.")]
        public float contactHeight = 0.002f;
        [Tooltip("How far (m) the nib must be pushed below the paper to reach full stroke size.")]
        public float pressDepth = 0.02f;
        [Tooltip("Pushed deeper than this (m), painting stops (hand went through the desk).")]
        public float maxPenetration = 0.06f;
        [Tooltip("Stroke size at a light touch → at full press (reference BrushMinMax).")]
        public Vector2 pressureRange = new Vector2(0.15f, 1f);
        [Tooltip("1 = linear. >1: light presses stay thin longer (finer control); <1: grows fast right after contact.")]
        [Range(0.3f, 3f)] public float pressureCurve = 1.3f;
        [Tooltip("Stroke size multiplier on top of pressure (see BrushStroke.SizeMultiplier).")]
        public float sizeMultiplier = 0.3f;

        [Header("Bristle animation (BonePen)")]
        public Animator boneAnimator;
        [Range(0f, 1f)] public float boneWeight = 0.92f;
        [Tooltip("Nib travel (m) per 20 ms sample below which the bend direction is left unchanged (avoids jitter from hand tremor).")]
        public float minBendTravel = 0.0005f;

        BrushStroke _stroke;
        MaterialPropertyBlock _mpb;
        Vector3 _lastBonePos, _boneDir;
        float _boneTimer;

        public bool IsDrawing => _stroke != null && _stroke.Active;
        public Texture CurrentStyle => styles != null && styles.Length > 0 ? styles[Mathf.Clamp(styleIndex, 0, styles.Length - 1)] : null;

        protected override void Awake()
        {
            base.Awake();
            _mpb = new MaterialPropertyBlock();
            if (canvas != null) _stroke = new BrushStroke(canvas);
            SetInk(inkColor);
        }

        protected override void OnReleased() => _stroke?.End();

        public void SetInk(Color color)
        {
            inkColor = color;
            if (hairRenderer == null) return;
            // Per-slot property block: only the bristles change colour, the handle keeps its wood material.
            hairRenderer.GetPropertyBlock(_mpb, hairMaterialIndex);
            _mpb.SetColor("_BaseColor", color);
            hairRenderer.SetPropertyBlock(_mpb, hairMaterialIndex);
        }

        void Update()
        {
            if (_stroke == null || nib == null) return;
            if (!IsHeld || canvas.InputLocked) { _stroke.End(); return; }

            if (canvas.TryProject(nib.position, out var uv, out float height) &&
                height <= contactHeight && height >= -maxPenetration)
            {
                float depth = Mathf.Max(0f, -height);
                float t = Mathf.Pow(Mathf.Clamp01(depth / Mathf.Max(0.001f, pressDepth)), pressureCurve);
                float pressure = Mathf.Lerp(pressureRange.x, pressureRange.y, t);

                if (!_stroke.Active) _stroke.Begin();
                _stroke.Brush = CurrentStyle;
                _stroke.Color = inkColor;
                _stroke.SizeMultiplier = sizeMultiplier;
                _stroke.AddPoint(canvas.UvToPixel(uv), pressure);
            }
            else
            {
                _stroke.End();
            }
        }

        void LateUpdate()
        {
            if (boneAnimator == null) return;
            if (IsDrawing)
            {
                _boneTimer += Time.deltaTime;
                if (_boneTimer <= 0.02f) return; // too frequent looks jittery (reference value)
                _boneTimer = 0f;

                // The bristles trail behind the nib: bend opposite to its travel. Work in the brush's own frame
                // (local Y = along the handle) so it stays right however the brush is tilted or turned in the hand.
                Vector3 trail = transform.InverseTransformDirection(_lastBonePos - nib.position);
                trail.y = 0f;
                _lastBonePos = nib.position;
                if (trail.magnitude < minBendTravel) return; // hold the current bend while (almost) still

                trail.Normalize();
                _boneDir = trail * (1f - boneWeight) + _boneDir * boneWeight;
                // Blend tree (measured on the clips): Y = +1 bends the tip to local +Z, X = +1 bends it to local -X.
                boneAnimator.SetFloat("Y", _boneDir.z);
                boneAnimator.SetFloat("X", -_boneDir.x);
            }
            else
            {
                boneAnimator.SetFloat("X", 0f);
                boneAnimator.SetFloat("Y", 0f);
                _boneDir = Vector3.zero;
                _lastBonePos = nib.position;
            }
        }
    }
}
