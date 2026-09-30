using UnityEngine;

namespace Maliang.Drawing
{
    /// <summary>
    /// Brush stroke rendering ported from Node_Brush PaintingHandler: cubic Bézier smoothing between samples,
    /// thinner strokes when moving fast, random "burr" jitter. Works in Ink RT pixels.
    ///
    /// The reference tuned its constants for a ~656 px wide canvas; <see cref="InkCanvas"/> is ~2048 px,
    /// so every size and distance goes through <see cref="PixelScale"/> to keep the same look.
    /// </summary>
    public class BrushStroke
    {
        const float ReferenceCanvasWidthPx = 656f;

        readonly InkCanvas _canvas;
        readonly Vector2[] _points = new Vector2[4];
        readonly float[] _speeds = new float[4];
        int _pointCount, _speedCount;
        Vector2 _last;
        bool _hasLast;

        public Texture Brush;
        public Color Color = Color.black;
        public bool Burrs = true;
        /// <summary>Overall size multiplier on top of pressure. The reference was tuned for calligraphy characters
        /// filling a small sheet; for painting a scene on the scroll ~0.3 gives 1–2 cm strokes.</summary>
        public float SizeMultiplier = 0.3f;
        public float BurrOffset = 4.5f;

        float _pressure = 1f;

        public BrushStroke(InkCanvas canvas) { _canvas = canvas; }

        float PixelScale => _canvas.WidthPx / ReferenceCanvasWidthPx;

        public bool Active => _hasLast;

        public void Begin()
        {
            _hasLast = false;
            _pointCount = 0;
            _speedCount = 0;
        }

        public void End() => Begin();

        /// <param name="px">Brush position in Ink RT pixels.</param>
        /// <param name="pressure">0..1 size from pen height (reference "brushOutSize").</param>
        public void AddPoint(Vector2 px, float pressure)
        {
            if (Brush == null) return;
            _pressure = pressure;
            if (!_hasLast) { _last = px; _hasLast = true; }

            // Distance in reference-canvas units so the speed → width curve matches the original.
            float distance = Vector2.Distance(_last, px) / PixelScale;
            _canvas.BeginBrushBatch(Brush, Color);
            Bezier(px, distance, BurrOffset * PixelScale);
            _canvas.EndBrushBatch();
            _last = px;
        }

        /// <summary>Reference SetScale: faster movement (larger per-frame distance) gives a thinner stroke.</summary>
        float ScaleFor(float distance)
        {
            float s = distance < 100f ? 0.8f - 0.005f * distance : 0.425f - 0.00125f * distance;
            if (s <= 0.05f) s = 0.05f;
            return s * _pressure;
        }

        void Bezier(Vector2 pos, float distance, float burr)
        {
            _points[_pointCount++] = pos;
            _speeds[_speedCount++] = distance;

            if (_pointCount == 4)
            {
                Vector2 tmp1 = _points[1], tmp2 = _points[2];

                // Push the two inner control points outwards for a fuller curve (reference constants).
                Vector2 middle = (_points[0] + _points[2]) / 2f;
                _points[1] = (_points[1] - middle) * 1.5f + middle;
                middle = (tmp1 + _points[3]) / 2f;
                _points[2] = (_points[2] - middle) * 2.1f + middle;

                float deltaSpeed = (_speeds[3] - _speeds[0]) / 50f;
                for (int i = 0; i < 50 / 1.5f; i++)
                {
                    float t = i / 50f;
                    float a1 = Mathf.Pow(1 - t, 3), a2 = Mathf.Pow(1 - t, 2) * 3 * t, a3 = 3 * t * t * (1 - t), a4 = t * t * t;
                    Vector2 p = a1 * _points[0] + a2 * _points[1] + a3 * _points[2] + a4 * _points[3];
                    float jitter = Burrs ? Random.Range(-burr, burr) : 0f;
                    Stamp(new Vector2(p.x + jitter, p.y + jitter), ScaleFor(_speeds[0] + deltaSpeed * i));
                }

                _points[0] = tmp1;
                _points[1] = tmp2;
                _points[2] = _points[3];
                _speeds[0] = _speeds[1];
                _speeds[1] = _speeds[2];
                _speeds[2] = _speeds[3];
                _pointCount = 3;
                _speedCount = 3;
            }
            else
            {
                Stamp(pos, ScaleFor(distance));
            }
        }

        void Stamp(Vector2 px, float scale)
        {
            float k = scale * SizeMultiplier * PixelScale;
            _canvas.AddBrushStamp(px, new Vector2(Brush.width * k, Brush.height * k));
        }
    }
}
