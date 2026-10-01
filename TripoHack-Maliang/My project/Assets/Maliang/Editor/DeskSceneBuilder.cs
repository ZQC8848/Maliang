using System.Collections.Generic;
using System.IO;
using System.Linq;
using Maliang.Core;
using Maliang.Drawing;
using Maliang.Ritual;
using Maliang.VR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;

namespace Maliang.EditorTools
{
    /// <summary>
    /// Builds Assets/Maliang/Scenes/M1_Desk.unity from scratch (Phase 2: draw + seal + levitate).
    /// Re-run after changing the layout constants; the scene is regenerated, not patched.
    /// Player stands at the origin facing +Z; the desk is in front of them.
    /// </summary>
    public static class DeskSceneBuilder
    {
        const string ScenePath = "Assets/Maliang/Scenes/M1_Desk.unity";
        const string MatDir = "Assets/Maliang/Art/Materials";
        const string XrOriginPrefab = "Assets/Samples/XR Interaction Toolkit/3.6.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
        const string DeskFbx = "Assets/Maliang/Art/NodeBrush/Models/table/中式书桌.fbx";
        const string PenFbx = "Assets/Maliang/Art/NodeBrush/Animations/pen.FBX";
        const string PenAnimator = "Assets/Maliang/Art/NodeBrush/Animations/BrushRiggedAnimator.controller";
        const string BrushTexDir = "Assets/Maliang/Art/NodeBrush/Textures/BrushTextures";
        const string SealWu = "Assets/Maliang/Art/Seals/seal_wu.png";
        const string SealJing = "Assets/Maliang/Art/Seals/seal_jing.png";
        const string SealObjectModel = "Assets/Maliang/Art/Seals/Models/SealObject_Tripo.glb";
        const string SealWorldModel = "Assets/Maliang/Art/Seals/Models/SealWorld_Tripo.glb";
        const float SealHeight = 0.095f;   // desk seal, base to top of the knob (m)
        const float SealModelYaw = -90f;   // the carved logo is on the Tripo models' local -X side; turn it toward the player (-Z)
        const string InkstoneModel = "Assets/Maliang/Art/Environment/Inkstone_Tripo.glb";
        const float InkstoneLength = 0.16f;  // longest side of the Tripo inkstone that replaces the desk's round 古砚 (m)
        const float InkstoneYaw = 0f;        // turn the Tripo model so its wells sit the way the reference shows
        const string CandleStandModel = "Assets/Maliang/Art/Environment/CandleStand_Tripo.glb";
        const float CandleStandHeight = 0.22f;  // lotus candle stand on the desk (m); becomes the Phase 4 candle
        // Hand-placed in the editor (world metres, x/z on the desk top; the desk itself is fixed by DeskFrontZ/DeskYaw).
        static readonly Vector2 PaintFirstDishXZ = new Vector2(0.7028f, 0.4822f);   // 墨, front-right dish of the grid
        static readonly Vector2 InkstoneXZ = new Vector2(-0.4828f, 0.6460f);        // desk left, where the grey tray was
        static readonly Vector2 CandleStandXZ = new Vector2(0.47f, 0.91f);         // desk right, behind the paints
        static readonly Vector2 SealWorldXZ = new Vector2(-0.597f, 0.5089f);        // 境 on the left ...
        static readonly Vector2 SealObjectXZ = new Vector2(-0.492f, 0.5089f);       // ... 物 on its right
        const string LotusFloorModel = "Assets/Maliang/Art/Environment/LotusPlatform_Tripo.glb";
        const float LotusTopDiameter = 4.5f;                      // walkable inner disc (m)
        static readonly Vector3 LotusCenter = new Vector3(0f, 0f, 0.45f); // between the player and the desk
        const string StoneAlbedo = "Assets/Maliang/Art/Textures/stone_albedo.png";
        const string StoneNormal = "Assets/Maliang/Art/Textures/stone_normal.png";
        const string SkyTexture = "Assets/Maliang/Art/Sky/ShanshuiSky.png";   // 8192x4096 lat-long panorama
        const int SkyMaxSize = 8192;    // keep full resolution: ~22 texels per degree in the headset
        const float SkyFrontU = 0.45f;  // panorama column (0..1) to put straight ahead of the player: the main peak
        const string ScrollPrefabPath = "Assets/Maliang/Prefabs/Scroll.prefab";
        const string ScrollQuadPath = "Assets/Maliang/Art/Desk/ScrollQuad.asset";
        static readonly Vector2 SpareScrollXZ = new Vector2(0f, 0.97f); // rolled spare, lying across the back of the desk
        const string RollerWoodTex = "Assets/Maliang/Art/NodeBrush/Models/table/古木.jpg";

        // Decorative desk props that overlap our tools or look grabbable (loose brushes, brush pot, brush rest),
        // plus the felt mat and its two paperweights, which sit exactly where the scroll goes.
        static readonly string[] HiddenProps =
        {
            "1", "02", "Object01", "Object02", "Tube01", "Circle11", "Circle12",
            "Line1607", "Line1608", "Line1609", "Line1610", "Line1611", "Line1612",
            "Line01.002", "Line17", "Line1613",
            "Box06", "红木砚托", // grey inkstone tray and rosewood inkstone stand, replaced by the Tripo inkstone
        };

        // Desk props the player can pick up (they glide back when released, like the brush).
        // Each book is two meshes (cover + pages) that must move together.
        static readonly (string label, string[] parts)[] GrabbableProps =
        {
            ("Book 1", new[] { "B-16", "B-15" }),
            ("Book 2", new[] { "B-17", "B-18" }),
            ("Inkstone 古砚", new[] { "古砚" }),
        };

        // Layout (metres)
        const float PlayerStepOffset = 0.3f;     // highest step the player can walk up (the rig ships with 0.5)
        const float DeskFrontZ = 0.38f;          // near edge of the tabletop, in front of the player
        const float DeskYaw = -135f;             // the FBX is modelled at 45°, stool on one side; this faces the stool to -Z
        static readonly Vector2 ScrollSize = new Vector2(0.72f, 0.36f);
        const float ScrollFrontInset = 0.07f;    // gap between desk front edge and scroll
        const float RodRadius = 0.011f;          // scroll rods
        public const float BrushHairLength = 0.02f; // bristle part of the pen mesh, from the tip (see BrushMeshSplitter)

        static readonly (string name, Color color)[] Paints =
        {
            ("墨 Ink", new Color(0.08f, 0.07f, 0.07f)),
            ("朱砂 Vermilion", new Color(0.78f, 0.18f, 0.10f)),
            ("石青 Azurite", new Color(0.13f, 0.30f, 0.62f)),
            ("石绿 Malachite", new Color(0.16f, 0.52f, 0.36f)),
            ("藤黄 Gamboge", new Color(0.93f, 0.70f, 0.12f)),
            ("赭石 Ochre", new Color(0.60f, 0.36f, 0.18f)),
            ("花青 Indigo", new Color(0.16f, 0.22f, 0.35f)),
            ("曙红 Rose", new Color(0.84f, 0.20f, 0.36f)),
            ("朱磦 Orange", new Color(0.92f, 0.45f, 0.16f)),
            ("紫 Violet", new Color(0.44f, 0.24f, 0.58f)),
        };
        const int PaintColumns = 5;
        const float DishDiameter = 0.06f;
        const float DishSpacing = 0.07f;

        [MenuItem("Maliang/Build M1 Desk Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildInternal();
        }

        /// <summary>No prompts; used by tooling. Returns a short report.</summary>
        public static string BuildInternal()
        {
            Directory.CreateDirectory(MatDir);
            var report = new List<string>();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // The default camera would fight the XR rig camera.
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include)) Object.DestroyImmediate(cam.gameObject);
            var sun = Object.FindAnyObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                sun.intensity = 1.1f;
                sun.color = new Color(1f, 0.965f, 0.92f); // the panorama's cream (R +3%, B -4% against G)
                sun.shadows = LightShadows.Soft;
            }

            // Floor: the Tripo lotus platform (falls back to a plain plane if the model is missing)
            report.Add(BuildLotusFloor());

            // XR rig
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(XrOriginPrefab));
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var head = rig.GetComponentInChildren<Camera>().transform;
            report.Add(KeepPlayerOffDesk(rig));

            // Desk
            var deskTop = BuildDesk(report, out var deskRoot);

            float surfaceY = deskTop.max.y;

            // Drawing spot (empty at the start) and a rolled scroll at the back of the desk, to be laid on it
            var stationGo = new GameObject("Scroll Station");
            float scrollZ = deskTop.min.z + ScrollFrontInset + ScrollSize.y * 0.5f;
            stationGo.transform.position = new Vector3(deskTop.center.x, surfaceY + 0.004f, scrollZ);
            var station = stationGo.AddComponent<ScrollStation>();
            var scrollPrefab = BuildScrollPrefab();
            station.scrollPrefab = scrollPrefab;
            station.head = head;

            // The drawing spot starts empty: the player lays the rolled scroll from the back of the desk there to begin.
            station.active = null;

            var spareSpot = new GameObject("Spare Spot").transform;
            spareSpot.SetParent(stationGo.transform, false);
            spareSpot.SetPositionAndRotation(new Vector3(SpareScrollXZ.x, surfaceY + 0.004f, SpareScrollXZ.y), Quaternion.Euler(0f, 90f, 0f));
            station.spareSpot = spareSpot;
            var spare = PlaceScroll(scrollPrefab, "Scroll (Spare)", spareSpot.position, spareSpot.rotation, head, ScrollStartMode.Rolled, true, station);
            station.spare = spare.GetComponent<ScrollPickup>();
            stationGo.AddComponent<DeskDebugKeys>().station = station;
            float rightX = deskTop.center.x + ScrollSize.x * 0.5f;

            // Paints: two rows of five at the front right, between the scroll roller and the desk edge
            var stone = StoneMaterial();
            for (int i = 0; i < Paints.Length; i++)
            {
                int col = i % PaintColumns, row = i / PaintColumns;
                var p = new Vector3(PaintFirstDishXZ.x - col * DishSpacing, surfaceY, PaintFirstDishXZ.y + row * DishSpacing);
                BuildInkPot(Paints[i].name, Paints[i].color, p, stone);
            }

            // Brush, standing upright behind the paints, clear of the scroll roller and the inkstone
            var pen = BuildBrush(null, new Vector3(rightX + 0.055f, surfaceY + 0.03f, deskTop.min.z + 0.06f + 2.2f * DishSpacing));
            pen.station = station; // paints on whichever scroll is on the desk

            // Tripo inkstone in place of the desk model's round 古砚 (desk left), lotus candle stand at the back right
            report.Add(BuildInkstone(surfaceY));
            report.Add(BuildCandleStand(surfaceY));

            // Seals side by side at the front left, in front of the inkstone
            BuildSeal("Seal 物 (Object)", SealType.Object, SealWu, SealObjectModel, null, null, new Vector3(SealObjectXZ.x, surfaceY, SealObjectXZ.y));
            BuildSeal("Seal 境 (World)", SealType.World, SealJing, SealWorldModel, null, null, new Vector3(SealWorldXZ.x, surfaceY, SealWorldXZ.y));
            foreach (var seal in Object.FindObjectsByType<SealStamp>(FindObjectsInactive.Include)) seal.station = station;

            report.Add(SetupSky());

            // Ambient to match the sky: the hues are the panorama's measured band averages (above the horizon,
            // around it, the cloud sea below), all a bright warm cream; levels are toned down so the desk keeps
            // its shading, but kept high because everything sits in bright mist. Trilight rather than Skybox
            // ambient so a build looks the same without baked lighting data.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.637f, 0.62f, 0.596f);
            RenderSettings.ambientEquatorColor = new Color(0.514f, 0.5f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.31f, 0.3f, 0.286f);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            report.Add($"Saved {ScenePath}; tabletop {deskTop.min}..{deskTop.max}, drawing spot at {stationGo.transform.position}, rolled scroll at {spare.transform.position}, pen at {pen.transform.position}");
            return string.Join("\n", report);
        }

        // ------------------------------------------------------------------ sky

        /// <summary>
        /// The shanshui panorama as a 360° lat-long skybox, turned so <see cref="SkyFrontU"/> is straight ahead (+Z).
        /// No mipmaps: a panoramic skybox samples across the wrap seam, where mips would draw a thin line.
        /// </summary>
        static string SetupSky()
        {
            if (AssetImporter.GetAtPath(SkyTexture) is TextureImporter ti)
            {
                bool dirty = ti.mipmapEnabled || ti.wrapModeU != TextureWrapMode.Repeat || ti.wrapModeV != TextureWrapMode.Clamp ||
                             ti.maxTextureSize != SkyMaxSize || ti.textureCompression != TextureImporterCompression.CompressedHQ;
                if (dirty)
                {
                    ti.textureType = TextureImporterType.Default;
                    ti.sRGBTexture = true;
                    ti.mipmapEnabled = false;
                    ti.wrapModeU = TextureWrapMode.Repeat;
                    ti.wrapModeV = TextureWrapMode.Clamp;
                    ti.filterMode = FilterMode.Bilinear;
                    ti.maxTextureSize = SkyMaxSize;
                    ti.textureCompression = TextureImporterCompression.CompressedHQ;
                    ti.SaveAndReimport();
                }
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(SkyTexture);
            if (tex == null) return "Sky texture missing; kept the default skybox";

            string path = MatDir + "/Sky Shanshui.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Skybox/Panoramic"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_MainTex", tex);
            mat.SetFloat("_Mapping", 1f);   // latitude-longitude layout
            mat.SetFloat("_ImageType", 0f); // 360°
            mat.SetFloat("_Exposure", 1f);
            mat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f)); // neutral
            // Unity's panorama puts u = 0.25 at +Z; turning the sky by +θ° moves the view θ/360 further along u.
            mat.SetFloat("_Rotation", Mathf.Repeat((0.25f - SkyFrontU) * 360f, 360f));
            EditorUtility.SetDirty(mat);
            RenderSettings.skybox = mat;
            return $"Sky: {tex.width}x{tex.height} panorama, rotation {mat.GetFloat("_Rotation"):F0}°";
        }

        // ------------------------------------------------------------------ player

        /// <summary>
        /// The player must never end up on the desk (tabletop 0.745 m, open drawers ~0.68 m): a human-sized step
        /// instead of the rig's 0.5 m, and no jumping in this scene (a jump reaches the drawers, then the tabletop).
        /// </summary>
        static string KeepPlayerOffDesk(GameObject rig)
        {
            var cc = rig.GetComponent<CharacterController>();
            if (cc != null) cc.stepOffset = PlayerStepOffset;
            var jump = rig.GetComponentInChildren<JumpProvider>(true);
            if (jump != null) jump.gameObject.SetActive(false);
            return $"Player: step offset {(cc != null ? cc.stepOffset : -1f)} m, jump {(jump != null ? "disabled" : "not found")}";
        }

        // ------------------------------------------------------------------ candle stand

        /// <summary>
        /// The Tripo lotus candle stand, scaled to <see cref="CandleStandHeight"/> and stood on the desk at
        /// <see cref="CandleStandXZ"/>, its lowest point resting on the desk top. Grabbable with return-to-rest like the other desk props
        /// (Phase 4 turns it into the candle).
        /// </summary>
        static string BuildCandleStand(float surfaceY)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CandleStandModel);
            if (prefab == null) return "Candle stand skipped (model missing)";

            var root = new GameObject("Prop Candle Stand 烛台");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);

            var b = RendererBounds(model);
            model.transform.localScale = Vector3.one * (CandleStandHeight / b.size.y);
            b = RendererBounds(model);
            model.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z); // base centred on the root
            b = RendererBounds(model);

            root.transform.position = new Vector3(CandleStandXZ.x, surfaceY, CandleStandXZ.y);

            // Box collider: the model is too detailed for a convex hull (Unity caps hulls at 256 polygons).
            var col = root.AddComponent<BoxCollider>();
            col.center = root.transform.InverseTransformPoint(RendererBounds(model).center);
            col.size = b.size;

            // A lit red candle in the lotus cup: the cup floor is the highest point of the mesh close to its axis.
            b = RendererBounds(model);
            float cupFloor = float.MinValue;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var w = mf.transform.TransformPoint(v);
                    if (new Vector2(w.x - b.center.x, w.z - b.center.z).magnitude < 0.012f) cupFloor = Mathf.Max(cupFloor, w.y);
                }
            var flame = CandleFlameBuilder.Build(root.transform, new Vector3(b.center.x, cupFloor, b.center.z));

            ConfigureGrab(root);
            root.AddComponent<GrabbableTool>();
            return $"Candle stand at {root.transform.position}, size {b.size}; candle in the cup at y={cupFloor:F3}, flame tip at {flame.Tip.position}";
        }

        // ------------------------------------------------------------------ inkstone

        /// <summary>
        /// Swaps the desk model's round 古砚 for the Tripo inkstone: the old mesh is removed from its grabbable prop and
        /// the Tripo model (longest side <see cref="InkstoneLength"/>) goes in, stood on the desk at <see cref="InkstoneXZ"/>.
        /// The prop keeps its grab / return-to-rest setup.
        /// </summary>
        static string BuildInkstone(float surfaceY)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(InkstoneModel);
            var root = GameObject.Find("Prop Inkstone 古砚");
            if (prefab == null || root == null) return "Inkstone swap skipped (model or 古砚 missing)";

            foreach (Transform child in root.transform.Cast<Transform>().ToList()) Object.DestroyImmediate(child.gameObject);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localRotation = Quaternion.Euler(0f, InkstoneYaw, 0f);
            var b = RendererBounds(model);
            model.transform.localScale = Vector3.one * (InkstoneLength / Mathf.Max(b.size.x, b.size.z));
            b = RendererBounds(model);
            model.transform.position -= new Vector3(b.center.x - root.transform.position.x, b.min.y - root.transform.position.y, b.center.z - root.transform.position.z);
            b = RendererBounds(model);

            root.transform.position = new Vector3(InkstoneXZ.x, surfaceY, InkstoneXZ.y);

            // A box fits the slab well and stays cheap (a convex hull of the dense Tripo mesh hits Unity's 256-polygon cap).
            var col = root.AddComponent<BoxCollider>();
            b = RendererBounds(model);
            col.center = root.transform.InverseTransformPoint(b.center);
            col.size = b.size;
            return $"Inkstone (Tripo) at {root.transform.position}, size {b.size}";
        }

        // ------------------------------------------------------------------ floor

        /// <summary>
        /// Stands the player on the lotus platform: the model is scaled so its flat top disc is
        /// <see cref="LotusTopDiameter"/> across, and moved so that disc is at y = 0 (the XR rig's floor) centred on
        /// <see cref="LotusCenter"/>. Mesh colliders make the top walkable / teleportable.
        /// </summary>
        static string BuildLotusFloor()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LotusFloorModel);
            if (prefab == null)
            {
                var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
                plane.name = "Floor";
                plane.GetComponent<Renderer>().sharedMaterial = Lit("Floor", new Color(0.23f, 0.2f, 0.18f), 0.2f);
                plane.isStatic = true;
                return "Lotus floor model missing; used a plane";
            }

            var floor = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            floor.name = "Floor (Lotus Platform)";
            foreach (var mf in floor.GetComponentsInChildren<MeshFilter>())
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;

            // Measure at the model's own scale: top height at the centre, then walk outwards until the surface
            // stops being flat (the rim / petals) to get the radius of the walkable disc.
            var b = RendererBounds(floor);
            Physics.SyncTransforms();
            float TopAt(Vector3 xz) => Physics.Raycast(new Vector3(xz.x, b.max.y + 1f, xz.z), Vector3.down, out var hit, b.size.y + 2f) ? hit.point.y : float.NaN;
            float centreY = TopAt(b.center);
            float radius = 0f;
            float step = b.extents.x / 200f;
            for (float r = step; r < b.extents.x; r += step)
            {
                bool flat = true;
                for (int a = 0; a < 8 && flat; a++)
                {
                    float ang = a * Mathf.PI / 4f;
                    float y = TopAt(b.center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r);
                    flat = !float.IsNaN(y) && Mathf.Abs(y - centreY) < b.size.y * 0.02f;
                }
                if (!flat) break;
                radius = r;
            }
            if (radius <= 0f) radius = b.extents.x * 0.8f;

            float scale = LotusTopDiameter * 0.5f / radius;
            floor.transform.localScale = Vector3.one * scale;
            Vector3 centreTop = new Vector3(b.center.x, centreY, b.center.z) * scale;
            floor.transform.position = LotusCenter - centreTop;
            foreach (var t in floor.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            Physics.SyncTransforms();

            var nb = RendererBounds(floor);
            return $"Lotus floor: flat top radius {radius:F3} (model units) → scale {scale:F2}; size {nb.size}, top at y=0, base at y={nb.min.y:F2}";
        }

        // ------------------------------------------------------------------ desk

        static Bounds BuildDesk(List<string> report, out GameObject root)
        {
            root = new GameObject("Desk");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DeskFbx));
            // Unpack so desk parts can be lifted out into their own grabbable objects (not allowed inside a prefab instance).
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.transform.SetParent(root.transform, false);
            root.transform.rotation = Quaternion.Euler(0f, DeskYaw, 0f);

            var top = model.transform.Find("Box25");
            var topBounds = top.GetComponent<Renderer>().bounds;

            // The stool: everything in front of the tabletop's near edge and below it. It stays visible but gets
            // no colliders, so the player can stand where it is.
            var stool = new HashSet<GameObject>();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                if (r.bounds.center.z < topBounds.min.z - 0.02f && r.bounds.max.y < topBounds.min.y)
                    stool.Add(r.gameObject);
            report.Add("Stool parts (no collider): " + string.Join(", ", stool.Select(g => g.name)));

            foreach (var name in HiddenProps)
            {
                var t = model.transform.Find(name);
                if (t != null) t.gameObject.SetActive(false);
                else report.Add("Prop not found: " + name);
            }

            // Move so the tabletop's near edge is at DeskFrontZ, centred on x = 0.
            Vector3 shift = new Vector3(-topBounds.center.x, 0f, DeskFrontZ - topBounds.min.z);
            root.transform.position += shift;
            topBounds.center += shift;

            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                if (mf.gameObject.activeSelf && !stool.Contains(mf.gameObject))
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;

            foreach (var (label, parts) in GrabbableProps)
                report.Add(MakeGrabbableProp(model.transform, label, parts));

            // The two drawers under the tabletop (the FBX only has their fronts, merged into one mesh)
            DrawerBuilder.Build(model.transform, report);

            report.Add($"Tabletop bounds after placement: min={topBounds.min} max={topBounds.max}");
            return topBounds;
        }

        /// <summary>
        /// Lifts desk-model parts out into their own grabbable object: pivot at the bottom centre, convex colliders,
        /// kinematic rigidbody, XR grab, and return-to-rest on release (<see cref="GrabbableTool"/>).
        /// </summary>
        static string MakeGrabbableProp(Transform model, string label, string[] partNames)
        {
            var parts = partNames.Select(n => model.Find(n)).Where(t => t != null).ToList();
            if (parts.Count == 0) return "Grabbable prop not found: " + label;

            var renderers = parts.SelectMany(t => t.GetComponentsInChildren<Renderer>(true)).ToList();
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            var root = new GameObject("Prop " + label);
            root.transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
            foreach (var t in parts)
            {
                t.SetParent(root.transform, true);
                foreach (var c in t.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var tr in t.GetComponentsInChildren<Transform>(true)) tr.gameObject.isStatic = false;
                foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = true; // required on a moving rigidbody
                }
            }
            ConfigureGrab(root);
            root.AddComponent<GrabbableTool>();
            return $"Grabbable: {root.name} ({string.Join("+", parts.Select(p => p.name))}) size {b.size}";
        }

        static Material StoneMaterial()
        {
            if (AssetImporter.GetAtPath(StoneNormal) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.SaveAndReimport();
            }
            var mat = Lit("PaintDish Stone", Color.white, 0.32f);
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(StoneAlbedo));
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(StoneNormal));
            mat.SetFloat("_BumpScale", 0.6f);
            mat.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Matte pigment paste: low smoothness plus a faint grainy normal so it does not read as glossy plastic.</summary>
        static Material PaintMaterial()
        {
            var mat = Lit("Paint", Color.white, 0.2f); // colour comes from each InkPot's property block
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(StoneNormal));
            mat.SetTextureScale("_BumpMap", new Vector2(2f, 2f));
            mat.SetFloat("_BumpScale", 0.3f);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ scroll

        /// <summary>
        /// Builds the scroll (paper canvas, rods with their paper rolls, ritual + unroll, grab collider around the rolled
        /// bundle) and saves it as <see cref="ScrollPrefabPath"/>; the station spawns new spares from it.
        /// </summary>
        static ScrollRitual BuildScrollPrefab()
        {
            var root = new GameObject("Scroll");
            var canvasGo = new GameObject("InkCanvas", typeof(MeshFilter), typeof(MeshRenderer));
            canvasGo.transform.SetParent(root.transform, false);
            var canvas = canvasGo.AddComponent<InkCanvas>();
            canvas.size = ScrollSize;
            canvas.displayMaterial = ScrollMaterial();
            canvas.BuildMesh(); // edit-mode preview (rebuilt at runtime); saved so the prefab keeps it
            var mf = canvasGo.GetComponent<MeshFilter>();
            mf.sharedMesh.name = Path.GetFileNameWithoutExtension(ScrollQuadPath); // asset name must match its file
            mf.sharedMesh = SaveMeshAsset(mf.sharedMesh, ScrollQuadPath);
            var mr = canvasGo.GetComponent<MeshRenderer>();
            mr.sharedMaterial = canvas.displayMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            canvas.stampShader = Shader.Find("Hidden/Maliang/BrushStamp");
            var unroll = BuildRollers(root.transform, canvas);

            var ritual = root.AddComponent<ScrollRitual>();
            ritual.canvas = canvas;
            ritual.scrollRoot = root.transform;
            ritual.unroll = unroll;

            // Grabbed by the rolled-up bundle (both rods together in the middle); off once it lies open on the desk.
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, unroll.rolledRadius - unroll.restDrop, 0f);
            box.size = new Vector3(unroll.rolledRadius * 4f + 0.004f, unroll.rolledRadius * 2f + 0.002f, ScrollSize.y + 0.05f);
            ConfigureGrab(root);
            var pickup = root.AddComponent<ScrollPickup>();
            pickup.grabCollider = box;

            EnsureFolder(ParentFolder(ScrollPrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, ScrollPrefabPath);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<ScrollRitual>();
        }

        static ScrollRitual PlaceScroll(ScrollRitual prefab, string name, Vector3 position, Quaternion rotation, Transform head,
            ScrollStartMode mode, bool pickable, ScrollStation station)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject);
            go.name = name;
            go.transform.SetPositionAndRotation(position, rotation);
            var ritual = go.GetComponent<ScrollRitual>();
            ritual.head = head;
            ritual.startMode = mode;
            if (mode == ScrollStartMode.Rolled) ritual.unroll.SetProgress(0f); // looks rolled up in the editor too
            var pickup = go.GetComponent<ScrollPickup>();
            pickup.pickableOnStart = pickable;
            pickup.station = station;
            return ritual;
        }

        static Mesh SaveMeshAsset(Mesh mesh, string path)
        {
            EnsureFolder(ParentFolder(path));
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static string ParentFolder(string assetPath) => assetPath.Substring(0, assetPath.LastIndexOf('/'));

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            EnsureFolder(ParentFolder(path));
            AssetDatabase.CreateFolder(ParentFolder(path), path.Substring(path.LastIndexOf('/') + 1));
        }

        static Material ScrollMaterial()
        {
            string path = MatDir + "/ScrollDisplay.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Maliang/ScrollDisplay"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_PaperTint", new Color(0.95f, 0.91f, 0.82f));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// The two rods at the scroll's short edges, each with a roll of paper around it (hidden while the scroll is open;
        /// <see cref="ScrollUnroll"/> thickens it and brings both rods to the middle when the scroll is rolled up).
        /// </summary>
        static ScrollUnroll BuildRollers(Transform scroll, InkCanvas canvas)
        {
            // Aged rosewood; the texture's grain runs along V, which is the cylinder's length.
            var wood = Lit("ScrollRoller", new Color(0.95f, 0.9f, 0.88f), 0.35f);
            wood.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(RollerWoodTex));
            wood.SetTextureScale("_BaseMap", new Vector2(0.25f, 1.4f)); // ~30 cm of wood per tile around × along the rod
            EditorUtility.SetDirty(wood);
            var paper = Lit("ScrollPaperRoll", new Color(0.93f, 0.89f, 0.8f), 0.1f);
            var rods = new Transform[2];
            var wraps = new Transform[2];
            foreach (float side in new[] { -1f, 1f })
            {
                var roller = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                roller.name = side < 0 ? "Roller L" : "Roller R";
                Object.DestroyImmediate(roller.GetComponent<Collider>());
                roller.transform.SetParent(scroll, false);
                roller.transform.localPosition = new Vector3(side * (ScrollSize.x * 0.5f + RodRadius), RodRadius - 0.005f, 0f);
                roller.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                roller.transform.localScale = new Vector3(RodRadius * 2f, ScrollSize.y * 0.5f + 0.025f, RodRadius * 2f);
                roller.GetComponent<Renderer>().sharedMaterial = wood;
                rods[side < 0 ? 0 : 1] = roller.transform;

                var wrap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                wrap.name = side < 0 ? "Paper Roll L" : "Paper Roll R";
                Object.DestroyImmediate(wrap.GetComponent<Collider>());
                wrap.transform.SetParent(scroll, false);
                wrap.transform.localPosition = roller.transform.localPosition;
                wrap.transform.localRotation = roller.transform.localRotation;
                wrap.transform.localScale = new Vector3(RodRadius * 2f, ScrollSize.y * 0.5f, RodRadius * 2f); // as long as the paper is deep
                var wr = wrap.GetComponent<Renderer>();
                wr.sharedMaterial = paper;
                wr.enabled = false; // the scene is laid out open
                wraps[side < 0 ? 0 : 1] = wrap.transform;
            }

            var unroll = scroll.gameObject.AddComponent<ScrollUnroll>();
            unroll.canvas = canvas;
            unroll.rodLeft = rods[0];
            unroll.rodRight = rods[1];
            unroll.wrapLeft = wraps[0];
            unroll.wrapRight = wraps[1];
            unroll.rodRadius = RodRadius;
            return unroll;
        }

        // ------------------------------------------------------------------ brush

        static BrushPen BuildBrush(InkCanvas canvas, Vector3 restPosition)
        {
            var root = new GameObject("Brush");
            root.transform.position = restPosition;

            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PenFbx));
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;       // FBX root sits at the bristle tip
            model.transform.localRotation = Quaternion.Euler(270f, 0f, 0f); // upright: tip down, handle up
            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>(); // Unity fake-null: "??" would not work here
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PenAnimator);
            // Split mesh: submesh 0 = handle (wood), submesh 1 = bristles (take the ink colour at runtime).
            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            smr.sharedMesh = BrushMeshSplitter.Split(BrushHairLength);
            smr.sharedMaterials = new[]
            {
                Lit("BrushHandle", new Color(0.35f, 0.2f, 0.1f), 0.5f),
                Lit("BrushHair", new Color(0.9f, 0.88f, 0.82f), 0.35f),
            };

            var nib = new GameObject("Nib");
            nib.transform.SetParent(root.transform, false);
            nib.transform.localPosition = Vector3.zero;
            var nibCol = nib.AddComponent<SphereCollider>();
            nibCol.radius = 0.008f;
            nibCol.isTrigger = true;

            var grabCol = root.AddComponent<CapsuleCollider>();
            grabCol.direction = 1;
            grabCol.radius = 0.012f;
            grabCol.height = 0.24f;
            grabCol.center = new Vector3(0f, 0.12f, 0f);

            ConfigureGrab(root);

            var pen = root.AddComponent<BrushPen>();
            pen.canvas = canvas;
            pen.nib = nib.transform;
            pen.nibCollider = nibCol;
            pen.boneAnimator = animator;
            pen.hairRenderer = smr;
            pen.hairMaterialIndex = 1;
            pen.styles = AssetDatabase.FindAssets("t:Texture2D", new[] { BrushTexDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => Path.GetFileName(p) == "brushTexture.png" ? 0 : 1).ThenBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<Texture>)
                .ToArray();
            pen.inkColor = Paints[0].color;
            return pen;
        }

        // ------------------------------------------------------------------ paints

        static void BuildInkPot(string name, Color color, Vector3 deskPoint, Material dishMaterial)
        {
            var root = new GameObject("Paint " + name);
            root.transform.position = deskPoint;

            var dish = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dish.name = "Dish";
            Object.DestroyImmediate(dish.GetComponent<Collider>());
            dish.transform.SetParent(root.transform, false);
            dish.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            dish.transform.localScale = new Vector3(DishDiameter, 0.012f, DishDiameter);
            dish.GetComponent<Renderer>().sharedMaterial = dishMaterial;

            var paint = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            paint.name = "Paint Surface";
            Object.DestroyImmediate(paint.GetComponent<Collider>());
            paint.transform.SetParent(root.transform, false);
            paint.transform.localPosition = new Vector3(0f, 0.0235f, 0f);
            paint.transform.localScale = new Vector3(DishDiameter * 0.82f, 0.0015f, DishDiameter * 0.82f);
            paint.GetComponent<Renderer>().sharedMaterial = PaintMaterial();

            // Dip zone: from the paint surface up to ~2 cm above it.
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 0.032f, 0f);
            box.size = new Vector3(DishDiameter * 0.8f, 0.03f, DishDiameter * 0.8f);
            var ink = root.AddComponent<InkPot>();
            ink.color = color;
            ink.paintSurface = paint.GetComponent<Renderer>();
        }

        // ------------------------------------------------------------------ seals

        /// <summary>
        /// A desk seal using the Tripo-generated model (bronze 造物印 / jade 創世印). The model is scaled to
        /// <see cref="SealHeight"/>, stood on the root's origin, and its flat base footprint becomes the stamping face.
        /// </summary>
        static void BuildSeal(string name, SealType type, string texPath, string modelPath, InkCanvas canvas, ScrollRitual ritual, Vector3 deskPoint)
        {
            var root = new GameObject(name);
            root.transform.position = deskPoint + Vector3.up * 0.0005f;

            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localRotation = Quaternion.Euler(0f, SealModelYaw, 0f);

            // Scale to desk size, then stand the base on the root origin, centred.
            var b = RendererBounds(model);
            model.transform.localScale = Vector3.one * (SealHeight / b.size.y);
            b = RendererBounds(model);
            model.transform.position += new Vector3(root.transform.position.x - b.center.x, root.transform.position.y - b.min.y, root.transform.position.z - b.center.z);
            b = RendererBounds(model);

            // Stamping face = the flat base: vertices within 3 mm of the bottom.
            Vector2 footprint = BaseFootprint(model, b.min.y + 0.003f);

            var face = new GameObject("Face");
            face.transform.SetParent(root.transform, false);
            face.transform.localPosition = Vector3.zero;
            face.transform.localRotation = Quaternion.Euler(180f, 0f, 0f); // +Y out of the face, i.e. down

            var col = root.AddComponent<BoxCollider>();
            col.center = root.transform.InverseTransformPoint(b.center);
            col.size = b.size;

            ConfigureGrab(root);

            var seal = root.AddComponent<SealStamp>();
            seal.type = type;
            seal.sealTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            seal.face = face.transform;
            // Square imprint texture: use the smaller side so the print never overhangs the base.
            float side = Mathf.Min(footprint.x, footprint.y);
            seal.faceSize = new Vector2(side, side);
            seal.canvas = canvas;
            seal.ritual = ritual;
        }

        static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>World-space X/Z extent of the mesh vertices below <paramref name="maxY"/>.</summary>
        static Vector2 BaseFootprint(GameObject go, float maxY)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var w = mf.transform.TransformPoint(v);
                    if (w.y > maxY) continue;
                    minX = Mathf.Min(minX, w.x); maxX = Mathf.Max(maxX, w.x);
                    minZ = Mathf.Min(minZ, w.z); maxZ = Mathf.Max(maxZ, w.z);
                }
            }
            return minX > maxX ? Vector2.zero : new Vector2(maxX - minX, maxZ - minZ);
        }

        // ------------------------------------------------------------------ helpers

        static void ConfigureGrab(GameObject go)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var grab = go.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.useDynamicAttach = true;
            grab.throwOnDetach = false;
        }

        static Material Lit(string name, Color color, float smoothness)
        {
            string path = $"{MatDir}/{Sanitize(name)}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static string Sanitize(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

        static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
