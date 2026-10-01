using System;
using Maliang.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maliang.Drawing
{
    /// <summary>
    /// The scroll's drawing surface (TechPlan §4). A flat, horizontal quad in this transform's local XZ plane,
    /// facing local +Y, <see cref="size"/> metres across. Keep the transform's scale at 1: size is defined here.
    ///
    /// Layers: Ink RT (opaque white, brush strokes) and Seal RT (transparent, one stamp). Paper is a material tint/texture.
    /// UV (0,0) is the local (-x, -z) corner, UV (1,1) the (+x, +z) corner; the top of the drawing is the far (+z) edge.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class InkCanvas : MonoBehaviour
    {
        [Tooltip("Scroll surface size in metres (x = width, y = depth along local Z).")]
        public Vector2 size = new Vector2(0.72f, 0.36f);
        [Tooltip("Pixels along the longer side of the Ink RT.")]
        public int longSidePixels = 2048;
        public Material displayMaterial;
        public Shader stampShader;

        [Header("Coverage (for the 'enough ink to seal' check)")]
        [Tooltip("Cells along the long side of the coarse coverage grid.")]
        public int coverageGridLongSide = 64;

        RenderTexture _ink, _seal;
        Material _stampMat, _display;
        bool[] _cells;
        int _gridW, _gridH, _inkedCells;

        public RenderTexture Ink => _ink;
        public RenderTexture Seal => _seal;
        public int WidthPx { get; private set; }
        public int HeightPx { get; private set; }
        public int LongSidePx => Mathf.Max(WidthPx, HeightPx);
        public float Aspect => size.x / size.y;

        /// <summary>Fraction of coverage-grid cells touched by ink, 0..1.</summary>
        public float InkCoverage => _cells == null || _cells.Length == 0 ? 0f : _inkedCells / (float)_cells.Length;

        /// <summary>When true, pens and seals ignore this canvas (set once the seal is placed).</summary>
        public bool InputLocked { get; set; }

        public bool HasSeal { get; private set; }

        public event Action Cleared;

        void Awake()
        {
            BuildMesh();
            CreateTargets();
            ClearAll();
        }

        void OnDestroy()
        {
            if (_ink != null) _ink.Release();
            if (_seal != null) _seal.Release();
            if (_stampMat != null) Destroy(_stampMat);
            if (_display != null) Destroy(_display);
        }

        /// <summary>Generates the quad for <see cref="size"/>. Runs in Awake; the scene builder also calls it so the paper shows in edit mode.</summary>
        public void BuildMesh()
        {
            float hx = size.x * 0.5f, hz = size.y * 0.5f;
            var mesh = new Mesh { name = "InkCanvasQuad" };
            mesh.vertices = new[] { new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        void CreateTargets()
        {
            if (size.x >= size.y) { WidthPx = longSidePixels; HeightPx = Mathf.RoundToInt(longSidePixels * size.y / size.x); }
            else { HeightPx = longSidePixels; WidthPx = Mathf.RoundToInt(longSidePixels * size.x / size.y); }

            _ink = NewTarget("InkRT");
            _seal = NewTarget("SealRT");

            _stampMat = new Material(stampShader != null ? stampShader : Shader.Find("Hidden/Maliang/BrushStamp")) { hideFlags = HideFlags.HideAndDontSave };

            var mr = GetComponent<MeshRenderer>();
            _display = displayMaterial != null ? new Material(displayMaterial) : new Material(Shader.Find("Maliang/ScrollDisplay"));
            _display.name = "ScrollDisplay (Instance)";
            _display.SetTexture("_InkTex", _ink);
            _display.SetTexture("_SealTex", _seal);
            mr.sharedMaterial = _display;

            float cell = Mathf.Max(size.x, size.y) / coverageGridLongSide;
            _gridW = Mathf.Max(1, Mathf.RoundToInt(size.x / cell));
            _gridH = Mathf.Max(1, Mathf.RoundToInt(size.y / cell));
            _cells = new bool[_gridW * _gridH];
        }

        RenderTexture NewTarget(string label)
        {
            var rt = new RenderTexture(WidthPx, HeightPx, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = label,
                useMipMap = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
            };
            rt.Create();
            return rt;
        }

        // ------------------------------------------------------------------ geometry

        /// <summary>Projects a world point onto the canvas. height = signed distance above the surface (metres).</summary>
        public bool TryProject(Vector3 world, out Vector2 uv, out float height)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            height = local.y;
            uv = new Vector2(local.x / size.x + 0.5f, local.z / size.y + 0.5f);
            return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
        }

        public Vector2 UvToPixel(Vector2 uv) => new Vector2(uv.x * WidthPx, uv.y * HeightPx);

        // ------------------------------------------------------------------ drawing

        RenderTexture _batchPrevRT;
        bool _batching;

        /// <summary>
        /// Starts a batch of brush stamps into the Ink layer (one GL begin/end for many quads).
        /// Must be closed with <see cref="EndBrushBatch"/> in the same frame.
        /// </summary>
        public void BeginBrushBatch(Texture brush, Color color)
        {
            if (_batching) EndBrushBatch();
            _batchPrevRT = RenderTexture.active;
            Bind(_ink, brush, color, false);
            GL.Begin(GL.QUADS);
            _batching = true;
        }

        /// <summary>Adds one brush stamp centred at pixel <paramref name="px"/> to the open batch.</summary>
        public void AddBrushStamp(Vector2 px, Vector2 pixelSize)
        {
            if (!_batching) return;
            float l = (px.x - pixelSize.x * 0.5f) / WidthPx, r = (px.x + pixelSize.x * 0.5f) / WidthPx;
            float b = (px.y - pixelSize.y * 0.5f) / HeightPx, t = (px.y + pixelSize.y * 0.5f) / HeightPx;
            GL.TexCoord2(0, 0); GL.Vertex3(l, b, 0);
            GL.TexCoord2(1, 0); GL.Vertex3(r, b, 0);
            GL.TexCoord2(1, 1); GL.Vertex3(r, t, 0);
            GL.TexCoord2(0, 1); GL.Vertex3(l, t, 0);
            MarkCoverage(px, Mathf.Max(pixelSize.x, pixelSize.y) * 0.25f);
        }

        public void EndBrushBatch()
        {
            if (!_batching) return;
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = _batchPrevRT;
            _batching = false;
        }

        /// <summary>Draws a single brush stamp (convenience; strokes use the batch API).</summary>
        public void StampBrush(Texture brush, Vector2 px, Vector2 pixelSize, Color color)
        {
            BeginBrushBatch(brush, color);
            AddBrushStamp(px, pixelSize);
            EndBrushBatch();
        }

        /// <summary>
        /// Replaces the Seal layer with one stamp. Corners are canvas UVs matching texture UVs
        /// (0,0), (1,0), (1,1), (0,1), so any quad (rotated, skewed) can be stamped.
        /// </summary>
        public void StampSeal(Texture sealTex, Vector2 c00, Vector2 c10, Vector2 c11, Vector2 c01)
        {
            ClearTarget(_seal, Color.clear);
            DrawQuad(_seal, sealTex, Color.white, true, c00, c10, c11, c01,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
            HasSeal = true;
        }

        void DrawQuad(RenderTexture target, Texture tex, Color color, bool useTexColor,
            Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Vector2 t0, Vector2 t1, Vector2 t2, Vector2 t3)
        {
            var prev = RenderTexture.active;
            Bind(target, tex, color, useTexColor);
            GL.Begin(GL.QUADS);
            GL.TexCoord2(t0.x, t0.y); GL.Vertex3(p0.x, p0.y, 0);
            GL.TexCoord2(t1.x, t1.y); GL.Vertex3(p1.x, p1.y, 0);
            GL.TexCoord2(t2.x, t2.y); GL.Vertex3(p2.x, p2.y, 0);
            GL.TexCoord2(t3.x, t3.y); GL.Vertex3(p3.x, p3.y, 0);
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        /// <summary>Targets <paramref name="rt"/> with a full-texture viewport and an ortho (0..1) projection.</summary>
        void Bind(RenderTexture rt, Texture tex, Color color, bool useTexColor)
        {
            Graphics.SetRenderTarget(rt);
            // GL keeps whatever viewport the last camera used; without this, 0..1 maps to the wrong pixel rect.
            GL.Viewport(new Rect(0, 0, rt.width, rt.height));
            _stampMat.SetTexture("_MainTex", tex);
            _stampMat.SetColor("_Color", color);
            _stampMat.SetFloat("_UseTexColor", useTexColor ? 1f : 0f);
            _stampMat.SetPass(0);
            GL.PushMatrix();
            GL.LoadOrtho();
        }

        static void ClearTarget(RenderTexture rt, Color c)
        {
            var prev = RenderTexture.active;
            Graphics.SetRenderTarget(rt);
            GL.Viewport(new Rect(0, 0, rt.width, rt.height)); // GL.Clear only clears the current viewport
            GL.Clear(false, true, c);
            RenderTexture.active = prev;
        }

        public void ClearInk()
        {
            ClearTarget(_ink, Color.white);
            Array.Clear(_cells, 0, _cells.Length);
            _inkedCells = 0;
        }

        public void ClearSeal()
        {
            ClearTarget(_seal, Color.clear);
            HasSeal = false;
        }

        public void ClearAll()
        {
            ClearInk();
            ClearSeal();
            InputLocked = false;
            Cleared?.Invoke();
        }

        void MarkCoverage(Vector2 px, float radiusPx)
        {
            float cx = px.x / WidthPx * _gridW, cy = px.y / HeightPx * _gridH;
            float r = radiusPx / WidthPx * _gridW;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r)), x1 = Mathf.Min(_gridW - 1, Mathf.FloorToInt(cx + r));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r)), y1 = Mathf.Min(_gridH - 1, Mathf.FloorToInt(cy + r));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = y * _gridW + x;
                if (_cells[i]) continue;
                _cells[i] = true;
                _inkedCells++;
            }
        }

        // ------------------------------------------------------------------ layers (library)

        /// <summary>
        /// Reads the ink and seal layers at full resolution as PNGs (the seal keeps its alpha), for the library
        /// (Phase3Design 8.1). Calls back on the main thread a frame or two later.
        /// </summary>
        public void ReadLayers(Action<byte[], byte[]> done)
        {
            ReadPng(_ink, ink => ReadPng(_seal, seal => done?.Invoke(ink, seal)));
        }

        static void ReadPng(RenderTexture rt, Action<byte[]> done)
        {
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
            {
                if (req.hasError)
                {
                    Debug.LogError("[InkCanvas] layer readback failed");
                    done?.Invoke(null);
                    return;
                }
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                tex.SetPixelData(req.GetData<Color32>(), 0);
                tex.Apply(false);
                byte[] png = tex.EncodeToPNG();
                Destroy(tex);
                done?.Invoke(png);
            });
        }

        /// <summary>
        /// Puts saved layers back on the scroll (a replay from the library): the drawing and the seal exactly as they
        /// were. The canvas stays locked; the ink coverage is not tracked for a replay.
        /// </summary>
        public bool LoadLayers(byte[] inkPng, byte[] sealPng)
        {
            bool ok = BlitPng(inkPng, _ink) & BlitPng(sealPng, _seal);
            HasSeal = sealPng != null;
            InputLocked = true;
            return ok;
        }

        static bool BlitPng(byte[] png, RenderTexture rt)
        {
            if (png == null) return false;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(png)) { Destroy(tex); return false; }
            Graphics.Blit(tex, rt);
            Destroy(tex);
            return true;
        }

        // ------------------------------------------------------------------ export

        /// <summary>Exports the Ink layer only (no seal, no paper), cropped and padded back to the canvas aspect (TechPlan §5.5).</summary>
        public void Export(Action<InkExport> done, int outputLongSide = 1536, float margin = 0.09f)
        {
            InkExporter.Export(_ink, Aspect, outputLongSide, margin, result =>
            {
                MaliangLog.Info("Canvas", result.Empty
                    ? "Export: canvas is empty"
                    : $"Export: {result.Width}x{result.Height} PNG ({result.Png.Length / 1024} KB), ink rect {result.InkRect}");
                done?.Invoke(result);
            });
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.9f, 0.6f, 0.2f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, 0f, size.y));
            Gizmos.DrawLine(Vector3.zero, new Vector3(0, 0, size.y * 0.5f)); // top of the drawing
        }
#endif
    }
}
