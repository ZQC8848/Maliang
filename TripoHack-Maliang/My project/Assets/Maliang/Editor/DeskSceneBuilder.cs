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
        const string StoneAlbedo = "Assets/Maliang/Art/Textures/stone_albedo.png";
        const string StoneNormal = "Assets/Maliang/Art/Textures/stone_normal.png";

        // Decorative desk props that overlap our tools or look grabbable (loose brushes, brush pot, brush rest),
        // plus the felt mat and its two paperweights, which sit exactly where the scroll goes.
        static readonly string[] HiddenProps =
        {
            "1", "02", "Object01", "Object02", "Tube01", "Circle11", "Circle12",
            "Line1607", "Line1608", "Line1609", "Line1610", "Line1611", "Line1612",
            "Line01.002", "Line17", "Line1613",
        };

        // Desk props the player can pick up (they glide back when released, like the brush).
        // Each book is two meshes (cover + pages) that must move together.
        static readonly (string label, string[] parts)[] GrabbableProps =
        {
            ("Book 1", new[] { "B-16", "B-15" }),
            ("Book 2", new[] { "B-17", "B-18" }),
            ("Inkstone Tray", new[] { "Box06" }),
            ("Inkstone 古砚", new[] { "古砚" }),
            ("Inkstone Stand 红木砚托", new[] { "红木砚托" }),
        };

        // Layout (metres)
        const float DeskFrontZ = 0.38f;          // near edge of the tabletop, in front of the player
        const float DeskYaw = -135f;             // the FBX is modelled at 45°, stool on one side; this faces the stool to -Z
        static readonly Vector2 ScrollSize = new Vector2(0.72f, 0.36f);
        const float ScrollFrontInset = 0.07f;    // gap between desk front edge and scroll
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
                sun.color = new Color(1f, 0.95f, 0.86f);
                sun.shadows = LightShadows.Soft;
            }

            // Floor
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(1f, 1f, 1f);
            floor.GetComponent<Renderer>().sharedMaterial = Lit("Floor", new Color(0.23f, 0.2f, 0.18f), 0.2f);
            floor.isStatic = true;

            // XR rig
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(XrOriginPrefab));
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var head = rig.GetComponentInChildren<Camera>().transform;

            // Desk
            var deskTop = BuildDesk(report, out var deskRoot);

            // Scroll
            var scrollRoot = new GameObject("Scroll");
            float scrollZ = deskTop.min.z + ScrollFrontInset + ScrollSize.y * 0.5f;
            scrollRoot.transform.position = new Vector3(deskTop.center.x, deskTop.max.y + 0.004f, scrollZ);
            var canvasGo = new GameObject("InkCanvas", typeof(MeshFilter), typeof(MeshRenderer));
            canvasGo.transform.SetParent(scrollRoot.transform, false);
            var canvas = canvasGo.AddComponent<InkCanvas>();
            canvas.size = ScrollSize;
            canvas.displayMaterial = ScrollMaterial();
            canvas.BuildMesh(); // edit-mode preview; replaced at runtime
            canvasGo.GetComponent<MeshRenderer>().sharedMaterial = canvas.displayMaterial;
            canvas.stampShader = Shader.Find("Hidden/Maliang/BrushStamp");
            canvasGo.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            BuildRollers(scrollRoot.transform);

            var ritual = scrollRoot.AddComponent<ScrollRitual>();
            ritual.canvas = canvas;
            ritual.scrollRoot = scrollRoot.transform;
            ritual.head = head;
            scrollRoot.AddComponent<DeskDebugKeys>().ritual = ritual;

            float surfaceY = deskTop.max.y;
            float rightX = deskTop.center.x + ScrollSize.x * 0.5f;
            float leftX = deskTop.center.x - ScrollSize.x * 0.5f;

            // Paints: two rows of five at the front right, between the scroll roller and the desk edge
            var stone = StoneMaterial();
            for (int i = 0; i < Paints.Length; i++)
            {
                int col = i % PaintColumns, row = i / PaintColumns;
                var p = new Vector3(deskTop.max.x - 0.07f - col * DishSpacing, surfaceY, deskTop.min.z + 0.06f + row * DishSpacing);
                BuildInkPot(Paints[i].name, Paints[i].color, p, stone);
            }

            // Brush, standing upright behind the paints, clear of the scroll roller and the inkstone
            var pen = BuildBrush(canvas, new Vector3(rightX + 0.09f, surfaceY + 0.03f, deskTop.min.z + 0.06f + 2.4f * DishSpacing));

            // Seals side by side at the front left (the grey inkstone tray sits behind them)
            BuildSeal("Seal 物 (Object)", SealType.Object, SealWu, canvas, ritual, new Vector3(leftX - 0.26f, surfaceY, deskTop.min.z + 0.08f));
            BuildSeal("Seal 境 (World)", SealType.World, SealJing, canvas, ritual, new Vector3(leftX - 0.14f, surfaceY, deskTop.min.z + 0.08f));

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.55f, 0.6f);
            RenderSettings.ambientEquatorColor = new Color(0.4f, 0.37f, 0.33f);
            RenderSettings.ambientGroundColor = new Color(0.15f, 0.13f, 0.12f);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            report.Add($"Saved {ScenePath}; tabletop {deskTop.min}..{deskTop.max}, scroll at {scrollRoot.transform.position}, pen at {pen.transform.position}");
            return string.Join("\n", report);
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

        // ------------------------------------------------------------------ scroll

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

        static void BuildRollers(Transform scroll)
        {
            var wood = Lit("ScrollRoller", new Color(0.28f, 0.14f, 0.08f), 0.45f);
            foreach (float side in new[] { -1f, 1f })
            {
                var roller = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                roller.name = side < 0 ? "Roller L" : "Roller R";
                Object.DestroyImmediate(roller.GetComponent<Collider>());
                roller.transform.SetParent(scroll, false);
                roller.transform.localPosition = new Vector3(side * (ScrollSize.x * 0.5f + 0.011f), 0.006f, 0f);
                roller.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                roller.transform.localScale = new Vector3(0.022f, ScrollSize.y * 0.5f + 0.025f, 0.022f);
                roller.GetComponent<Renderer>().sharedMaterial = wood;
            }
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
            paint.GetComponent<Renderer>().sharedMaterial = Lit("Paint", Color.white, 0.85f);

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

        static void BuildSeal(string name, SealType type, string texPath, InkCanvas canvas, ScrollRitual ritual, Vector3 deskPoint)
        {
            const float w = 0.045f, h = 0.07f;
            var root = new GameObject(name);
            root.transform.position = deskPoint + Vector3.up * 0.0005f;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            body.transform.localScale = new Vector3(w, h, w);
            body.GetComponent<Renderer>().sharedMaterial = type == SealType.Object
                ? Lit("SealStone Object", new Color(0.72f, 0.62f, 0.45f), 0.6f)
                : Lit("SealStone World", new Color(0.35f, 0.55f, 0.48f), 0.7f);

            var knob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            knob.name = "Knob";
            Object.DestroyImmediate(knob.GetComponent<Collider>());
            knob.transform.SetParent(root.transform, false);
            knob.transform.localPosition = new Vector3(0f, h + 0.008f, 0f);
            knob.transform.localScale = new Vector3(0.03f, 0.02f, 0.03f);
            knob.GetComponent<Renderer>().sharedMaterial = body.GetComponent<Renderer>().sharedMaterial;

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            // Label on the side facing the player so the two seals can be told apart on the desk.
            var label = GameObject.CreatePrimitive(PrimitiveType.Quad);
            label.name = "Label";
            Object.DestroyImmediate(label.GetComponent<Collider>());
            label.transform.SetParent(root.transform, false);
            label.transform.localPosition = new Vector3(0f, h * 0.55f, -w * 0.5f - 0.0005f);
            label.transform.localScale = new Vector3(w * 0.8f, w * 0.8f, 1f);
            label.GetComponent<Renderer>().sharedMaterial = Unlit("SealLabel " + type, tex);

            var face = new GameObject("Face");
            face.transform.SetParent(root.transform, false);
            face.transform.localPosition = Vector3.zero;
            face.transform.localRotation = Quaternion.Euler(180f, 0f, 0f); // +Y out of the face, i.e. down

            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, h * 0.5f + 0.005f, 0f);
            col.size = new Vector3(w, h + 0.01f, w);

            ConfigureGrab(root);

            var seal = root.AddComponent<SealStamp>();
            seal.type = type;
            seal.sealTexture = tex;
            seal.face = face.transform;
            seal.faceSize = new Vector2(w, w);
            seal.canvas = canvas;
            seal.ritual = ritual;
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

        static Material Unlit(string name, Texture tex)
        {
            string path = $"{MatDir}/{Sanitize(name)}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", tex);
            mat.SetFloat("_Surface", 1f); // transparent
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
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
