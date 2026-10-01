using Maliang.Core;
using UnityEngine;

namespace Maliang.Drawing
{
    /// <summary>A dish of paint (port of Node_Brush PenColorHandler): dipping the brush nib into this trigger sets its ink colour.</summary>
    [RequireComponent(typeof(Collider))]
    public class InkPot : MonoBehaviour
    {
        public Color color = Color.black;
        [Tooltip("Renderer showing the paint surface; tinted with the colour.")]
        public Renderer paintSurface;

        void Reset() => GetComponent<Collider>().isTrigger = true;

        void Start() => ApplyColor();
        void OnValidate() => ApplyColor();

        void ApplyColor()
        {
            if (paintSurface == null) return;
            var mpb = new MaterialPropertyBlock();
            paintSurface.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", color);
            paintSurface.SetPropertyBlock(mpb);
        }

        void OnTriggerEnter(Collider other)
        {
            var pen = other.GetComponentInParent<BrushPen>();
            if (pen == null || other != pen.nibCollider) return;
            Sfx.Play(SfxId.InkDip, other.transform.position);
            if (pen.inkColor == color) return;
            pen.SetInk(color);
            pen.Haptic(0.3f, 0.06f);
        }
    }
}
