using Maliang.Effects;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maliang.EditorTools
{
    /// <summary>
    /// Builds a red candle (wax + wick) and its flame: particle layers drawn with the additive
    /// <c>Maliang/Flame</c> shader, a flickering point light and a <see cref="CandleFlame"/> controller.
    /// Sizes are in metres for a desk candle; the flame particles simulate in world space so the flame
    /// trails behind when the candle stand is carried.
    /// </summary>
    public static class CandleFlameBuilder
    {
        const string MatDir = "Assets/Maliang/Art/Materials";
        public const float CandleDiameter = 0.024f;
        public const float CandleHeight = 0.045f;
        const float WickHeight = 0.006f;
        const float WickDiameter = 0.0016f;

        /// <summary>Stands the candle on <paramref name="basePos"/> (world) under <paramref name="parent"/> and lights it.</summary>
        public static CandleFlame Build(Transform parent, Vector3 basePos)
        {
            var wax = Lit("Candle Wax", new Color(0.55f, 0.07f, 0.05f), 0.45f);
            var wickMat = Lit("Candle Wick", new Color(0.04f, 0.03f, 0.03f), 0.1f);

            var candle = Cylinder("Candle", parent, basePos + Vector3.up * (CandleHeight * 0.5f), CandleDiameter, CandleHeight, wax);
            var wickBase = basePos + Vector3.up * CandleHeight;
            Cylinder("Wick", candle.transform, wickBase + Vector3.up * (WickHeight * 0.5f), WickDiameter, WickHeight, wickMat);

            // The flame sits on the upper half of the wick.
            var root = new GameObject("Flame");
            root.transform.SetParent(parent, true);
            root.transform.position = wickBase + Vector3.up * (WickHeight * 0.5f);

            var teardrop = FlameMaterial("Flame Teardrop", 0f, 1.6f, 1.6f);
            var round = FlameMaterial("Flame Round", 1f, 1f, 2.4f);
            var ember = FlameMaterial("Flame Ember", 1f, 2f, 1.2f);

            var layers = new[]
            {
                // Soft orange halo around the flame
                Layer("Glow", root.transform, 0.012f, round, ParticleSystemSimulationSpace.Local, rate: 8f,
                    life: new Vector2(0.5f, 0.7f), sizeX: new Vector2(0.06f, 0.07f), sizeY: Vector2.zero,
                    colors: Grad(new Color(1f, 0.55f, 0.2f), new Color(1f, 0.45f, 0.15f), 0f, 0.1f, 0.08f),
                    riseSpeed: 0f, noise: 0f),
                // Main tongue of flame: yellow turning orange towards the tip
                Layer("Body", root.transform, 0.012f, teardrop, ParticleSystemSimulationSpace.World, rate: 40f,
                    life: new Vector2(0.18f, 0.26f), sizeX: new Vector2(0.010f, 0.013f), sizeY: new Vector2(0.024f, 0.030f),
                    colors: Grad(new Color(1f, 0.9f, 0.62f), new Color(1f, 0.45f, 0.15f), 0f, 0.9f, 0.7f),
                    riseSpeed: 0.03f, noise: 0.004f),
                // Bright white-yellow heart
                Layer("Heart", root.transform, 0.009f, teardrop, ParticleSystemSimulationSpace.World, rate: 30f,
                    life: new Vector2(0.15f, 0.22f), sizeX: new Vector2(0.005f, 0.006f), sizeY: new Vector2(0.012f, 0.015f),
                    colors: Grad(new Color(1f, 0.98f, 0.9f), new Color(1f, 0.85f, 0.55f), 0f, 0.8f, 0.6f),
                    riseSpeed: 0.02f, noise: 0.002f),
                // Blue root just above the wick
                Layer("Base", root.transform, 0.003f, teardrop, ParticleSystemSimulationSpace.World, rate: 25f,
                    life: new Vector2(0.15f, 0.2f), sizeX: new Vector2(0.006f, 0.007f), sizeY: new Vector2(0.009f, 0.011f),
                    colors: Grad(new Color(0.35f, 0.5f, 1f), new Color(0.3f, 0.4f, 0.9f), 0f, 0.55f, 0.3f),
                    riseSpeed: 0.01f, noise: 0.001f),
                Embers(root.transform, ember),
            };

            var lightGo = new GameObject("Flame Light");
            lightGo.transform.SetParent(root.transform, false);
            // Above the flame rather than in it: a point light a few cm from the cup and wax would blow them out.
            lightGo.transform.localPosition = new Vector3(0f, 0.07f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.62f, 0.3f);
            light.range = 1.5f;
            light.intensity = 0.8f;
            light.shadows = LightShadows.None;

            var tip = new GameObject("Tip").transform;
            tip.SetParent(root.transform, false);
            tip.localPosition = new Vector3(0f, 0.028f, 0f);

            var flame = root.AddComponent<CandleFlame>();
            flame.flameLight = light;
            flame.layers = layers;
            flame.tip = tip;
            flame.baseIntensity = light.intensity;
            return flame;
        }

        static ParticleSystem Layer(string name, Transform parent, float height, Material mat, ParticleSystemSimulationSpace space,
            float rate, Vector2 life, Vector2 sizeX, Vector2 sizeY, Gradient colors, float riseSpeed, float noise)
        {
            var ps = NewSystem(name, parent, height, mat, space);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = 0f;
            main.maxParticles = Mathf.CeilToInt(rate * life.y) + 4;
            if (sizeY != Vector2.zero)
            {
                main.startSize3D = true;
                main.startSizeX = new ParticleSystem.MinMaxCurve(sizeX.x, sizeX.y);
                main.startSizeY = new ParticleSystem.MinMaxCurve(sizeY.x, sizeY.y);
                main.startSizeZ = 1f;
            }
            else main.startSize = new ParticleSystem.MinMaxCurve(sizeX.x, sizeX.y);

            var emission = ps.emission;
            emission.rateOverTime = rate;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = colors;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.8f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.6f)));

            if (riseSpeed > 0f)
            {
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = 0f; vel.y = riseSpeed; vel.z = 0f;
            }
            if (noise > 0f)
            {
                var n = ps.noise;
                n.enabled = true;
                n.strength = noise;
                n.frequency = 4f;
                n.scrollSpeed = 1.5f;
                n.octaveCount = 1;
                n.quality = ParticleSystemNoiseQuality.Medium;
            }
            return ps;
        }

        static ParticleSystem Embers(Transform parent, Material mat)
        {
            var ps = NewSystem("Embers", parent, 0.02f, mat, ParticleSystemSimulationSpace.World);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.0012f, 0.0022f);
            main.maxParticles = 12;

            var emission = ps.emission;
            emission.rateOverTime = 1.2f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1, 3, 0, 0.7f) { probability = 0.25f } });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.002f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // cone opens upwards

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Grad(new Color(1f, 0.8f, 0.4f), new Color(1f, 0.35f, 0.08f), 1f, 1f, 0.8f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

            var n = ps.noise;
            n.enabled = true;
            n.strength = 0.03f;
            n.frequency = 2f;
            n.octaveCount = 1;
            n.quality = ParticleSystemNoiseQuality.Medium;
            return ps;
        }

        static ParticleSystem NewSystem(string name, Transform parent, float height, Material mat, ParticleSystemSimulationSpace space)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.simulationSpace = space;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.gravityModifier = 0f;
            main.startColor = Color.white;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.0005f;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.alignment = ParticleSystemRenderSpace.Facing; // face the eye but keep world-up, so the flame stays upright
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        /// <summary>Colour from <paramref name="start"/> to <paramref name="end"/>; alpha a0 → peak (at 20 %) → mid (at 60 %) → 0.</summary>
        static Gradient Grad(Color start, Color end, float a0, float peak, float mid)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(start, 0f), new GradientColorKey(Color.Lerp(start, end, 0.5f), 0.5f), new GradientColorKey(end, 1f) },
                new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(peak, 0.2f), new GradientAlphaKey(mid, 0.6f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        static GameObject Cylinder(string name, Transform parent, Vector3 centre, float diameter, float height, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, true);
            go.transform.position = centre;
            go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter); // the primitive is 2 units tall
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static Material FlameMaterial(string name, float shape, float intensity, float softness)
        {
            var mat = LoadOrCreate(name, Shader.Find("Maliang/Flame"));
            mat.SetFloat("_Shape", shape);
            mat.SetFloat("_Intensity", intensity);
            mat.SetFloat("_Softness", softness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material Lit(string name, Color color, float smoothness)
        {
            var mat = LoadOrCreate(name, Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LoadOrCreate(string name, Shader shader)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader) mat.shader = shader;
            return mat;
        }
    }
}
