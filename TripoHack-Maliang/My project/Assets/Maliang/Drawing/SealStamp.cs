using Maliang.Core;
using Maliang.Ritual;
using Maliang.VR;
using UnityEngine;

namespace Maliang.Drawing
{
    /// <summary>
    /// A physical seal (TechPlan §5). Pressing its face flat onto the scroll stamps once; the imprint's position,
    /// rotation and size match the face exactly. The ritual then decides whether the stamp is accepted.
    ///
    /// <see cref="face"/>: a child at the centre of the stamping face with its +Y pointing OUT of the face
    /// (downwards while stamping). The face spans ±faceSize/2 on its local X and Z.
    /// Texture orientation: holding the seal upright with its forward pointing away from the player gives an upright imprint.
    /// </summary>
    public class SealStamp : GrabbableTool
    {
        public SealType type;
        public Texture sealTexture;
        public Transform face;
        [Tooltip("Size of the stamping face in metres (local X, local Z of the face).")]
        public Vector2 faceSize = new Vector2(0.05f, 0.05f);

        [Tooltip("Stamps this station's active scroll. Without a station, the canvas / ritual below.")]
        public ScrollStation station;
        public InkCanvas canvas;
        public ScrollRitual ritual;

        [Header("Trigger")]
        [Tooltip("Face centre within this height of the paper (m) counts as contact.")]
        public float contactHeight = 0.008f;
        [Tooltip("Face must lift this high (m) before it can stamp again.")]
        public float rearmHeight = 0.03f;
        [Tooltip("Max angle between the face and the paper to stamp (TechPlan §5.3).")]
        public float maxTiltDegrees = 35f;
        [Tooltip("Ignore contacts further below the paper than this (m).")]
        public float maxPenetration = 0.06f;

        bool _armed = true;

        ScrollRitual Ritual => station != null && station.Active != null ? station.Active : ritual;
        InkCanvas Canvas => station != null && station.Active != null ? station.Active.canvas : canvas;

        void Update()
        {
            var canvas = Canvas;
            if (!IsHeld || canvas == null || Ritual == null || face == null) return;

            bool inside = canvas.TryProject(face.position, out _, out float height);
            if (height > rearmHeight || !inside) _armed = true;
            if (!_armed || canvas.InputLocked) return;
            if (!inside || height > contactHeight || height < -maxPenetration) return;

            float tilt = Vector3.Angle(face.up, -canvas.transform.up);
            if (tilt > maxTiltDegrees) return;

            _armed = false;
            TryStamp();
        }

        /// <summary>Stamps at the face's current pose, if the ritual allows it. Public for editor / desktop testing.</summary>
        public bool TryStamp()
        {
            var ritual = Ritual;
            var canvas = Canvas;
            if (!ritual.CanSeal)
            {
                // Not enough ink: the seal refuses to take (TechPlan §5.1). Small bump, nothing printed.
                Haptic(0.15f, 0.05f);
                Sfx.Play(SfxId.SealStamp, face.position, 0.25f);
                return false;
            }

            float hx = faceSize.x * 0.5f, hz = faceSize.y * 0.5f;
            // Face +Y points down onto the paper, so its local +Z maps to the near edge of the canvas:
            // assign texture V = 0 to +Z corners to keep the imprint upright and unmirrored.
            Vector2 uvA = ProjectUv(new Vector3(-hx, 0, hz));  // tex (0,0)
            Vector2 uvB = ProjectUv(new Vector3(hx, 0, hz));   // tex (1,0)
            Vector2 uvC = ProjectUv(new Vector3(hx, 0, -hz));  // tex (1,1)
            Vector2 uvD = ProjectUv(new Vector3(-hx, 0, -hz)); // tex (0,1)
            canvas.StampSeal(sealTexture, uvA, uvB, uvC, uvD);

            Haptic(0.8f, 0.15f);
            Sfx.Play(SfxId.SealStamp, face.position);
            ritual.OnSealed(type);
            return true;
        }

        Vector2 ProjectUv(Vector3 faceLocal)
        {
            Canvas.TryProject(face.TransformPoint(faceLocal), out var uv, out _);
            return uv;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (face == null) return;
            Gizmos.matrix = face.localToWorldMatrix;
            Gizmos.color = type == SealType.Object ? new Color(1f, 0.3f, 0.2f) : new Color(0.3f, 0.6f, 1f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(faceSize.x, 0.001f, faceSize.y));
            Gizmos.DrawLine(Vector3.zero, Vector3.up * 0.03f);
        }
#endif
    }
}
