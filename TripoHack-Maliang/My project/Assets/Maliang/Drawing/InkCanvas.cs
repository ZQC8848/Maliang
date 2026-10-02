using System;
using System.Collections.Generic;
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
    ///
    /// Undo: the brush marks of the last <see cref="UndoLimit"/> strokes are recorded; <see cref="Undo"/> puts the
    /// picture from before them back and redraws all but the last (same marks, same pixels). Older strokes are merged
    /// into that base picture.
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

        /// <summary>How many strokes can be taken back.</summary>
        public const int UndoLimit = 20;

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
            if (_base != null) _base.Release();
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
            _segment = null;
            if (_recording != null)
            {
                _segment = new Segment { brush = brush, color = color };
                _recording.Add(_segment);
            }
        }

        /// <summary>Adds one brush stamp centred at pixel <paramref name="px"/> to the open batch.</summary>
        public void AddBrushStamp(Vector2 px, Vector2 pixelSize)
        {
            if (!_batching) return;
            EmitQuad(px, pixelSize);
            MarkCoverage(_cells, ref _inkedCells, px, pixelSize);
            _segment?.stamps.Add(new Vector4(px.x, px.y, pixelSize.x, pixelSize.y));
        }

        void EmitQuad(Vector2 px, Vector2 pixelSize)
        {
            float l = (px.x - pixelSize.x * 0.5f) / WidthPx, r = (px.x + pixelSize.x * 0.5f) / WidthPx;
            float b = (px.y - pixelSize.y * 0.5f) / HeightPx, t = (px.y + pixelSize.y * 0.5f) / HeightPx;
            GL.TexCoord2(0, 0); GL.Vertex3(l, b, 0);
            GL.TexCoord2(1, 0); GL.Vertex3(r, b, 0);
            GL.TexCoord2(1, 1); GL.Vertex3(r, t, 0);
            GL.TexCoord2(0, 1); GL.Vertex3(l, t, 0);
        }

        public void EndBrushBatch()
        {
            if (!_batching) return;
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = _batchPrevRT;
            _batching = false;
            if (_recording == null) Rebase(); // ink put down outside a stroke cannot be taken back
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
            ForgetStrokes(); // sealed: the drawing is final, nothing can be taken back
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
            _history.Clear();
            _recording = null;
            _segment = null;
            _baseBlank = true;
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

        void MarkCoverage(bool[] cells, ref int inked, Vector2 px, Vector2 pixelSize)
        {
            float radiusPx = Mathf.Max(pixelSize.x, pixelSize.y) * 0.25f;
            float cx = px.x / WidthPx * _gridW, cy = px.y / HeightPx * _gridH;
            float r = radiusPx / WidthPx * _gridW;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r)), x1 = Mathf.Min(_gridW - 1, Mathf.FloorToInt(cx + r));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r)), y1 = Mathf.Min(_gridH - 1, Mathf.FloorToInt(cy + r));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = y * _gridW + x;
                if (cells[i]) continue;
                cells[i] = true;
                inked++;
            }
        }

        // ------------------------------------------------------------------ undo

        class Segment
        {
            public Texture brush;
            public Color color;
            public readonly List<Vector4> stamps = new List<Vector4>(); // centre px (x, y), size px (z, w)
        }

        readonly List<List<Segment>> _history = new List<List<Segment>>(); // oldest first
        List<Segment> _recording;
        Segment _segment;
        RenderTexture _base;                 // the picture before the recorded strokes (allocated when first needed)
        bool _baseBlank = true;              // ... or a clean sheet
        bool[] _baseCells;
        int _baseInked;

        /// <summary>Strokes that can be taken back now.</summary>
        public int UndoCount => _history.Count;
        /// <summary>Only while drawing: never once the seal is on (or the canvas is locked).</summary>
        public bool CanUndo => !InputLocked && !HasSeal && _history.Count > 0;

        /// <summary>Starts recording a stroke (pen down). Ends any stroke still open.</summary>
        public void BeginStroke()
        {
            EndStroke();
            _recording = new List<Segment>();
        }

        /// <summary>Ends the stroke being recorded (pen up); safe to call when none is.</summary>
        public void EndStroke()
        {
            if (_recording == null) return;
            var stroke = _recording;
            _recording = null;
            _segment = null;
            if (!stroke.Exists(s => s.stamps.Count > 0)) return;
            _history.Add(stroke);
            if (_history.Count > UndoLimit)
            {
                // The oldest stroke can no longer be taken back: merge it into the base picture.
                EnsureBase();
                Draw(_history[0], _base, _baseCells, ref _baseInked);
                _history.RemoveAt(0);
            }
        }

        /// <summary>Takes back the last stroke. False when there is none (or the canvas is locked).</summary>
        public bool Undo()
        {
            EndStroke();
            if (!CanUndo) return false;
            _history.RemoveAt(_history.Count - 1);

            if (_baseBlank)
            {
                ClearTarget(_ink, Color.white);
                Array.Clear(_cells, 0, _cells.Length);
                _inkedCells = 0;
            }
            else
            {
                Graphics.CopyTexture(_base, _ink);
                Array.Copy(_baseCells, _cells, _cells.Length);
                _inkedCells = _baseInked;
            }
            foreach (var stroke in _history) Draw(stroke, _ink, _cells, ref _inkedCells);
            MaliangLog.Info("Canvas", $"Undo: {_history.Count} stroke(s) left to take back, ink coverage {InkCoverage:P1}");
            return true;
        }

        /// <summary>Drops the recorded strokes (the picture stays as it is).</summary>
        void ForgetStrokes()
        {
            if (_batching) EndBrushBatch();
            _history.Clear();
            _recording = null;
            _segment = null;
        }

        /// <summary>Makes the current picture the base: nothing before now can be taken back.</summary>
        void Rebase()
        {
            _history.Clear();
            EnsureBase();
            Graphics.CopyTexture(_ink, _base);
            Array.Copy(_cells, _baseCells, _cells.Length);
            _baseInked = _inkedCells;
        }

        void EnsureBase()
        {
            if (_base == null) _base = NewTarget("InkUndoBaseRT");
            if (_baseCells == null || _baseCells.Length != _cells.Length) _baseCells = new bool[_cells.Length];
            if (!_baseBlank) return;
            ClearTarget(_base, Color.white);
            Array.Clear(_baseCells, 0, _baseCells.Length);
            _baseInked = 0;
            _baseBlank = false;
        }

        /// <summary>Redraws a recorded stroke onto <paramref name="target"/>, marking its coverage in <paramref name="cells"/>.</summary>
        void Draw(List<Segment> stroke, RenderTexture target, bool[] cells, ref int inked)
        {
            var prev = RenderTexture.active;
            foreach (var seg in stroke)
            {
                if (seg.stamps.Count == 0 || seg.brush == null) continue;
                Bind(target, seg.brush, seg.color, false);
                GL.Begin(GL.QUADS);
                foreach (var s in seg.stamps)
                {
                    var px = new Vector2(s.x, s.y);
                    var size = new Vector2(s.z, s.w);
                    EmitQuad(px, size);
                    MarkCoverage(cells, ref inked, px, size);
                }
                GL.End();
                GL.PopMatrix();
            }
            RenderTexture.active = prev;
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
            ForgetStrokes(); // locked from now on: nothing to take back
            HasSeal = sealPng != null;
            InputLocked = true;
            return ok;
        }

        /// <summary>Puts pictures on the scroll's layers directly (the sky scroll); null leaves a layer empty.</summary>
        public void LoadLayers(Texture ink, Texture seal)
        {
            if (ink != null) Graphics.Blit(ink, _ink);
            if (seal != null) Graphics.Blit(seal, _seal);
            ForgetStrokes(); // locked from now on: nothing to take back
            HasSeal = seal != null;
            InputLocked = true;
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
