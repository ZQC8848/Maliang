using System.Collections;
using TMPro;
using UnityEngine;

namespace Maliang.Ritual
{
    /// <summary>
    /// The failure line in the world (Phase3Design 7.2): English, ink-black serif, fades in above where the failed
    /// scroll lands, turns to face the player, stays a few seconds and fades out. The serif face is built at runtime
    /// from a font installed on the machine (nothing is bundled); without one it falls back to the TextMeshPro default.
    /// A soft parchment glow behind the letters keeps them readable over the dark desk and the bright sky alike.
    /// </summary>
    public class FailMessage : MonoBehaviour
    {
        static readonly string[] SerifFaces = { "Georgia", "Palatino Linotype", "Book Antiqua", "Cambria", "Times New Roman" };
        static TMP_FontAsset _font;
        static bool _fontTried;

        public float fadeIn = 0.5f;
        public float hold = 3f;
        public float fadeOut = 0.8f;

        TextMeshPro _text;
        Transform _head;
        Color _color = new Color(0.07f, 0.06f, 0.05f, 1f);

        /// <summary>Shows <paramref name="line"/> at <paramref name="at"/>, facing <paramref name="head"/>.</summary>
        public static FailMessage Show(string line, Vector3 at, Transform head)
        {
            var go = new GameObject("Fail Message");
            go.transform.position = at;
            var msg = go.AddComponent<FailMessage>();
            msg._head = head;
            msg.Build(line);
            return msg;
        }

        public void Hide()
        {
            if (this != null) Destroy(gameObject);
        }

        void Build(string line)
        {
            _text = gameObject.AddComponent<TextMeshPro>();
            var font = SerifFont();
            if (font != null) _text.font = font;
            _text.text = line;
            _text.fontSize = 0.5f;             // world units: capitals about 4-5 cm tall
            _text.alignment = TextAlignmentOptions.Center;
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.rectTransform.sizeDelta = new Vector2(1f, 0.3f);
            _text.color = new Color(_color.r, _color.g, _color.b, 0f);

            // Soft light glow behind the letters (TextMeshPro underlay).
            var mat = _text.fontMaterial;
            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor("_UnderlayColor", new Color(0.97f, 0.93f, 0.84f, 0.75f));
            mat.SetFloat("_UnderlayDilate", 0.6f);
            mat.SetFloat("_UnderlaySoftness", 0.8f);
            Face();
            StartCoroutine(Run());
        }

        static TMP_FontAsset SerifFont()
        {
            if (_fontTried) return _font;
            _fontTried = true;
            foreach (var face in SerifFaces)
            {
                try
                {
                    _font = TMP_FontAsset.CreateFontAsset(face, "Regular");
                    if (_font != null) return _font;
                }
                catch (System.Exception) { /* not installed */ }
            }
            return null;
        }

        void Face()
        {
            if (_head == null && Camera.main != null) _head = Camera.main.transform;
            if (_head == null) return;
            Vector3 away = transform.position - _head.position;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        IEnumerator Run()
        {
            Vector3 start = transform.position;
            for (float t = 0f; t < fadeIn; t += Time.deltaTime)
            {
                SetAlpha(t / fadeIn);
                transform.position = start + Vector3.up * (0.03f * (1f - t / fadeIn)); // settles down as it appears
                Face();
                yield return null;
            }
            SetAlpha(1f);
            transform.position = start;
            for (float t = 0f; t < hold; t += Time.deltaTime)
            {
                Face();
                yield return null;
            }
            for (float t = 0f; t < fadeOut; t += Time.deltaTime)
            {
                SetAlpha(1f - t / fadeOut);
                Face();
                yield return null;
            }
            Destroy(gameObject);
        }

        void SetAlpha(float a)
        {
            _text.color = new Color(_color.r, _color.g, _color.b, a);
            _text.fontMaterial.SetColor("_UnderlayColor", new Color(0.97f, 0.93f, 0.84f, 0.75f * a));
        }
    }
}
