using System.Collections.Generic;
using System.Linq;
using Maliang.VR;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Maliang.EditorTools
{
    /// <summary>
    /// Turns the desk's drawer fronts into two working drawers. The FBX has both fronts in one mesh (Box31) and nothing
    /// behind them, so this splits Box31 into a left and a right front, builds a wooden drawer box behind each
    /// (bottom, sides, back; box colliders so things can be stored inside), and moves each front's hardware
    /// (back plate, pin, hanging pull) onto its drawer. The pull hangs from a hinge at its pin (<see cref="DrawerPull"/>).
    /// </summary>
    public static class DrawerBuilder
    {
        const string MeshDir = "Assets/Maliang/Art/Desk/Drawers";
        const string FrontsMesh = "Box31";
        static readonly string[] Hardware = { "NGon02", "NGon03", "Circle02.001", "Circle03.001", "LWPolyline.36", "LWPolyline.37" };

        // Drawer box (metres)
        const float Wall = 0.012f;
        const float Depth = 0.52f;        // back of the front to the back of the box
        const float SideInset = 0.01f;    // the box is this much narrower than the front on each side
        const float BottomInset = 0.005f; // bottom panel sits this far above the front's lower edge
        const float TopGap = 0.016f;      // sides stop this far below the front's top edge, clear of the tabletop
        const float MaxOpen = 0.42f;      // leaves 10 cm of the box inside the desk when fully open
        const float UvPerMetre = 1.2f;    // same texel density as the desk's own wood

        public static void Build(Transform model, List<string> report)
        {
            var fronts = model.Find(FrontsMesh);
            if (fronts == null) { report.Add("Drawers skipped: " + FrontsMesh + " not found"); return; }
            EnsureFolder(MeshDir);

            var wood = fronts.GetComponent<MeshRenderer>().sharedMaterial;
            var srcMesh = fronts.GetComponent<MeshFilter>().sharedMesh;
            float midX = fronts.GetComponent<Renderer>().bounds.center.x;
            var hardware = Hardware.Select(n => model.Find(n)).Where(t => t != null).ToList();

            foreach (var (side, left) in new[] { ("L", true), ("R", false) })
            {
                var half = SplitHalf(srcMesh, fronts, midX, left);
                half.name = $"Drawer{side}_Front";
                half = SaveMesh(half);
                var fb = WorldBounds(half, fronts);

                // Root at the bottom centre of the front face; world-aligned so the panels are simple boxes.
                var root = new GameObject($"Drawer {side}");
                root.transform.position = new Vector3(fb.center.x, fb.min.y, fb.min.z);

                var front = new GameObject("Front", typeof(MeshFilter), typeof(MeshRenderer));
                front.transform.SetParent(root.transform, false);
                front.transform.SetPositionAndRotation(fronts.position, fronts.rotation);
                front.transform.localScale = fronts.lossyScale;
                front.GetComponent<MeshFilter>().sharedMesh = half;
                front.GetComponent<MeshRenderer>().sharedMaterial = wood;
                var frontCollider = front.AddComponent<BoxCollider>(); // fits the mesh; its axes are world-aligned

                // Drawer box behind the front (root-local = world axes, origin at the front's bottom-front edge).
                float frontDepth = fb.size.z;
                float width = fb.size.x - 2f * SideInset;
                float bottomY = BottomInset;
                float topY = fb.size.y - TopGap;
                float wallH = topY - (bottomY + Wall);
                float zBack = frontDepth + Depth;
                Panel(root, $"Drawer{side}_Bottom", "Bottom", new Vector3(width, Wall, Depth), new Vector3(0f, bottomY + Wall * 0.5f, frontDepth + Depth * 0.5f), wood);
                var sideMesh = new Vector3(Wall, wallH, Depth);
                Panel(root, $"Drawer{side}_Side", "Side L", sideMesh, new Vector3(-width * 0.5f + Wall * 0.5f, bottomY + Wall + wallH * 0.5f, frontDepth + Depth * 0.5f), wood);
                Panel(root, $"Drawer{side}_Side", "Side R", sideMesh, new Vector3(width * 0.5f - Wall * 0.5f, bottomY + Wall + wallH * 0.5f, frontDepth + Depth * 0.5f), wood);
                Panel(root, $"Drawer{side}_Back", "Back", new Vector3(width - 2f * Wall, wallH, Wall), new Vector3(0f, bottomY + Wall + wallH * 0.5f, zBack - Wall * 0.5f), wood);

                // Hardware on this front: back plate and pin ride along; the pull hangs from a hinge at the pin.
                var mine = hardware.Where(t => (t.GetComponent<Renderer>().bounds.center.x < midX) == left).ToList();
                var pin = mine.FirstOrDefault(t => t.name.StartsWith("Circle"));
                var pull = mine.FirstOrDefault(t => t.name.StartsWith("LWPolyline"));
                foreach (var t in mine)
                {
                    foreach (var c in t.GetComponents<Collider>()) Object.DestroyImmediate(c);
                    t.gameObject.isStatic = false;
                    t.SetParent(root.transform, true);
                }

                Collider pullCollider = null;
                DrawerPull hingeCtl = null;
                if (pull != null)
                {
                    var pinPos = pin != null ? pin.GetComponent<Renderer>().bounds.center : pull.GetComponent<Renderer>().bounds.max;
                    var hinge = new GameObject("Pull Hinge");
                    hinge.transform.SetParent(root.transform, false);
                    hinge.transform.position = pinPos;
                    pull.SetParent(hinge.transform, true);

                    var box = pull.gameObject.AddComponent<BoxCollider>(); // thin ring: thicken it so it is easy to grab
                    var s = box.size;
                    box.size = new Vector3(s.x, s.y, Mathf.Max(s.z, 0.008f / Mathf.Max(0.001f, pull.lossyScale.z)));
                    pullCollider = box;

                    hingeCtl = hinge.AddComponent<DrawerPull>();
                    hingeCtl.length = Vector3.Distance(pinPos, pull.GetComponent<Renderer>().bounds.center);
                }

                var rb = root.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;

                // Only the front and the pull are grab targets, not the panels (so things stored inside can be picked).
                var interactable = root.AddComponent<XRSimpleInteractable>();
                interactable.colliders.Clear();
                interactable.colliders.Add(frontCollider);
                if (pullCollider != null) interactable.colliders.Add(pullCollider);

                var drawer = root.AddComponent<Drawer>();
                drawer.openDirection = Vector3.back; // the desk front faces the player (-Z)
                drawer.maxOpen = MaxOpen;
                if (hingeCtl != null) hingeCtl.drawer = drawer;

                report.Add($"Drawer {side}: front {fb.size.x:F3}×{fb.size.y:F3} m, inside {width - 2f * Wall:F3} × {Depth - Wall:F3} × {wallH:F3} m (w×d×h), opens {MaxOpen} m; hardware {string.Join("+", mine.Select(t => t.name))}");
            }

            fronts.gameObject.SetActive(false); // replaced by the two split fronts
        }

        // ------------------------------------------------------------------ meshes

        /// <summary>The triangles of <paramref name="src"/> on one side of <paramref name="midX"/> (world), as a new mesh in the same local space.</summary>
        static Mesh SplitHalf(Mesh src, Transform t, float midX, bool left)
        {
            var v = src.vertices; var n = src.normals; var uv = src.uv; var tan = src.tangents;
            var tri = src.triangles;
            var map = new Dictionary<int, int>();
            var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nuv = new List<Vector2>(); var ntan = new List<Vector4>();
            var ntri = new List<int>();
            for (int i = 0; i < tri.Length; i += 3)
            {
                var c = (t.TransformPoint(v[tri[i]]) + t.TransformPoint(v[tri[i + 1]]) + t.TransformPoint(v[tri[i + 2]])) / 3f;
                if ((c.x < midX) != left) continue;
                for (int k = 0; k < 3; k++)
                {
                    int s = tri[i + k];
                    if (!map.TryGetValue(s, out int d))
                    {
                        d = nv.Count; map[s] = d;
                        nv.Add(v[s]);
                        if (n.Length > 0) nn.Add(n[s]);
                        if (uv.Length > 0) nuv.Add(uv[s]);
                        if (tan.Length > 0) ntan.Add(tan[s]);
                    }
                    ntri.Add(d);
                }
            }
            var m = new Mesh();
            m.SetVertices(nv);
            if (nn.Count > 0) m.SetNormals(nn);
            if (nuv.Count > 0) m.SetUVs(0, nuv);
            if (ntan.Count > 0) m.SetTangents(ntan);
            m.SetTriangles(ntri, 0);
            m.RecalculateBounds();
            if (nn.Count == 0) m.RecalculateNormals();
            return m;
        }

        static Bounds WorldBounds(Mesh m, Transform t)
        {
            var v = m.vertices;
            var b = new Bounds(t.TransformPoint(v[0]), Vector3.zero);
            foreach (var p in v) b.Encapsulate(t.TransformPoint(p));
            return b;
        }

        static void Panel(GameObject root, string meshName, string name, Vector3 size, Vector3 localCentre, Material mat)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localCentre;
            go.GetComponent<MeshFilter>().sharedMesh = SaveMesh(BoxMesh(meshName, size));
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<BoxCollider>(); // fits the panel exactly
        }

        /// <summary>A box centred on the origin with box-mapped UVs in metres × <see cref="UvPerMetre"/> (grain along the long side).</summary>
        static Mesh BoxMesh(string name, Vector3 size)
        {
            var h = size * 0.5f;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            void Face(Vector3 normal, Vector3 u, Vector3 v)
            {
                // u, v: half-extent vectors spanning the face; the grain runs along the longer one.
                bool swap = v.magnitude > u.magnitude;
                Vector3 c = Vector3.Scale(normal, h);
                int i0 = verts.Count;
                Vector3[] corners = { c - u - v, c + u - v, c + u + v, c - u + v };
                foreach (var p in corners)
                {
                    verts.Add(p);
                    norms.Add(normal);
                    float a = Vector3.Dot(p, u.normalized), b = Vector3.Dot(p, v.normalized);
                    uvs.Add((swap ? new Vector2(b, a) : new Vector2(a, b)) * UvPerMetre);
                }
                // Wind so the face points along its normal (Unity's front face: Cross(b - a, c - a) faces the viewer).
                if (Vector3.Dot(Vector3.Cross(u, v), normal) > 0f) tris.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
                else tris.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
            }
            Vector3 X = new Vector3(h.x, 0, 0), Y = new Vector3(0, h.y, 0), Z = new Vector3(0, 0, h.z);
            Face(Vector3.right, Z, Y); Face(Vector3.left, Z, Y);
            Face(Vector3.up, X, Z); Face(Vector3.down, X, Z);
            Face(Vector3.forward, X, Y); Face(Vector3.back, X, Y);

            var m = new Mesh { name = name };
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        /// <summary>Stores the mesh as an asset, updating an existing one in place so scene references keep working.</summary>
        static Mesh SaveMesh(Mesh m)
        {
            string path = $"{MeshDir}/{m.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(m, path);
                return m;
            }
            EditorUtility.CopySerialized(m, existing);
            Object.DestroyImmediate(m);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
