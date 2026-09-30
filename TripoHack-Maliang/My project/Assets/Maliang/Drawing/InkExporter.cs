using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maliang.Drawing
{
    public struct InkExport
    {
        public bool Empty;
        public byte[] Png;
        public int Width, Height;
        /// <summary>Region of the Ink RT (in RT pixels) that was exported, before scaling.</summary>
        public RectInt InkRect;
    }

    /// <summary>
    /// Ink RT → PNG for the vision model (TechPlan §5.5):
    /// read back asynchronously, find the ink bounding box, add a margin, grow it to the canvas aspect ratio
    /// around the ink centre (padding with white, never stretching), scale to the output size, encode PNG.
    /// </summary>
    public static class InkExporter
    {
        /// <summary>A pixel counts as ink when its darkest channel is below this (paper is pure white).</summary>
        const byte InkThreshold = 235;

        public static void Export(RenderTexture ink, float aspect, int outputLongSide, float margin, Action<InkExport> done)
        {
            AsyncGPUReadback.Request(ink, 0, TextureFormat.RGBA32, req =>
            {
                if (req.hasError)
                {
                    Debug.LogError("[InkExporter] GPU readback failed");
                    done?.Invoke(new InkExport { Empty = true });
                    return;
                }
                done?.Invoke(Process(req.GetData<Color32>(), ink.width, ink.height, aspect, outputLongSide, margin));
            });
        }

        static InkExport Process(NativeArray<Color32> px, int w, int h, float aspect, int outLong, float margin)
        {
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    var c = px[row + x];
                    byte m = c.r < c.g ? (c.r < c.b ? c.r : c.b) : (c.g < c.b ? c.g : c.b);
                    if (m >= InkThreshold) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0) return new InkExport { Empty = true };

            // Margin around the ink, then grow the short side so the region matches the canvas aspect.
            float cx = (minX + maxX + 1) * 0.5f, cy = (minY + maxY + 1) * 0.5f;
            float rw = maxX - minX + 1, rh = maxY - minY + 1;
            float pad = margin * Mathf.Max(rw, rh);
            rw += pad * 2f; rh += pad * 2f;
            if (rw / rh < aspect) rw = rh * aspect; else rh = rw / aspect;

            int regionW = Mathf.Max(1, Mathf.RoundToInt(rw)), regionH = Mathf.Max(1, Mathf.RoundToInt(rh));
            int rx = Mathf.RoundToInt(cx - regionW * 0.5f), ry = Mathf.RoundToInt(cy - regionH * 0.5f);

            // Copy the region; anything outside the RT stays white.
            var region = new Color32[regionW * regionH];
            var white = new Color32(255, 255, 255, 255);
            for (int y = 0; y < regionH; y++)
            {
                int sy = ry + y;
                for (int x = 0; x < regionW; x++)
                {
                    int sx = rx + x;
                    region[y * regionW + x] = sx >= 0 && sx < w && sy >= 0 && sy < h ? Opaque(px[sy * w + sx]) : white;
                }
            }

            var src = new Texture2D(regionW, regionH, TextureFormat.RGBA32, false);
            src.SetPixels32(region);
            src.Apply(false);

            float scale = Mathf.Min(1f, outLong / (float)Mathf.Max(regionW, regionH));
            int outW = Mathf.Max(1, Mathf.RoundToInt(regionW * scale)), outH = Mathf.Max(1, Mathf.RoundToInt(regionH * scale));
            Texture2D final = src;
            if (outW != regionW || outH != regionH)
            {
                final = Resize(src, outW, outH);
                UnityEngine.Object.Destroy(src);
            }

            var png = final.EncodeToPNG();
            UnityEngine.Object.Destroy(final);
            return new InkExport { Png = png, Width = outW, Height = outH, InkRect = new RectInt(rx, ry, regionW, regionH) };
        }

        static Color32 Opaque(Color32 c) { c.a = 255; return c; }

        static Texture2D Resize(Texture2D src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            src.filterMode = FilterMode.Bilinear;
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            dst.Apply(false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }
    }
}
