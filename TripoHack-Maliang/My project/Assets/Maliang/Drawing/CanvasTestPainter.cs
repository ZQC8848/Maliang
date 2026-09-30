using UnityEngine;

namespace Maliang.Drawing
{
    /// <summary>
    /// Editor / desktop test helper: paints sample strokes through the same <see cref="BrushStroke"/> path the VR brush uses,
    /// so drawing, export and sealing can be checked without a headset.
    /// </summary>
    public static class CanvasTestPainter
    {
        /// <summary>Snowy mountains under a moon, with a tower: the example from the Idea doc.</summary>
        public static void PaintMountainScene(InkCanvas canvas, Texture brush)
        {
            var ink = new Color(0.08f, 0.07f, 0.07f);
            var red = new Color(0.78f, 0.18f, 0.10f);
            var blue = new Color(0.13f, 0.30f, 0.62f);

            // Mountain ridge
            Polyline(canvas, brush, ink, 0.9f, new[]
            {
                new Vector2(0.08f, 0.25f), new Vector2(0.22f, 0.62f), new Vector2(0.33f, 0.42f),
                new Vector2(0.48f, 0.78f), new Vector2(0.62f, 0.45f), new Vector2(0.72f, 0.58f), new Vector2(0.9f, 0.25f),
            });
            // Tower on the peak
            Polyline(canvas, brush, ink, 0.6f, new[] { new Vector2(0.47f, 0.78f), new Vector2(0.47f, 0.9f) });
            Polyline(canvas, brush, ink, 0.6f, new[] { new Vector2(0.49f, 0.78f), new Vector2(0.49f, 0.9f) });
            Polyline(canvas, brush, red, 0.6f, new[] { new Vector2(0.455f, 0.9f), new Vector2(0.48f, 0.94f), new Vector2(0.505f, 0.9f) });
            // Moon
            Circle(canvas, brush, red, 0.7f, new Vector2(0.8f, 0.8f), 0.07f);
            // Lake
            Polyline(canvas, brush, blue, 0.8f, new[] { new Vector2(0.25f, 0.15f), new Vector2(0.4f, 0.12f), new Vector2(0.6f, 0.13f), new Vector2(0.75f, 0.16f) });
        }

        public static void Polyline(InkCanvas canvas, Texture brush, Color color, float pressure, Vector2[] uvPoints, float stepPx = 6f)
        {
            var stroke = new BrushStroke(canvas) { Brush = brush, Color = color };
            stroke.Begin();
            for (int i = 0; i < uvPoints.Length - 1; i++)
            {
                Vector2 a = canvas.UvToPixel(uvPoints[i]), b = canvas.UvToPixel(uvPoints[i + 1]);
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / stepPx));
                for (int k = 0; k < n; k++) stroke.AddPoint(Vector2.Lerp(a, b, k / (float)n), pressure);
            }
            stroke.AddPoint(canvas.UvToPixel(uvPoints[uvPoints.Length - 1]), pressure);
            stroke.End();
        }

        public static void Circle(InkCanvas canvas, Texture brush, Color color, float pressure, Vector2 centerUv, float radiusV, int segments = 48)
        {
            var pts = new Vector2[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                pts[i] = centerUv + new Vector2(Mathf.Cos(a) * radiusV / canvas.Aspect, Mathf.Sin(a) * radiusV);
            }
            Polyline(canvas, brush, color, pressure, pts);
        }
    }
}
