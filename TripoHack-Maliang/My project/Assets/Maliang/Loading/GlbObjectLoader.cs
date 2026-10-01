using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using Maliang.Core;
using UnityEngine;

namespace Maliang.Loading
{
    /// <summary>
    /// Loads a generated GLB at runtime with glTFast: a static model, or a Tripo rigged model with baked animation
    /// clips. Animation uses the legacy <see cref="Animation"/> component, the only animation path glTFast supports
    /// in player builds; the first clip loops.
    /// </summary>
    public static class GlbObjectLoader
    {
        public class Result
        {
            public GameObject Root;
            public Animation Animation;        // null for a static model
            public string[] ClipNames = new string[0];
            public Bounds Bounds;              // renderer bounds in world space after loading
        }

        public static async Task<Result> LoadAsync(string path, Transform parent = null, CancellationToken cancel = default)
        {
            var gltf = new GltfImport();
            var settings = new ImportSettings { AnimationMethod = AnimationMethod.Legacy };
            if (!await gltf.LoadFile(path, null, settings, cancel))
            {
                MaliangLog.Warn("Glb", $"Failed to load {path}");
                gltf.Dispose();
                return null;
            }

            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            if (parent != null) root.transform.SetParent(parent, false);
            if (!await gltf.InstantiateMainSceneAsync(root.transform, cancel))
            {
                MaliangLog.Warn("Glb", $"Failed to instantiate {path}");
                Object.Destroy(root);
                gltf.Dispose();
                return null;
            }
            // The import owns the meshes, materials and textures; release them with the object.
            root.AddComponent<GltfImportOwner>().Import = gltf;

            var result = new Result { Root = root, Animation = root.GetComponentInChildren<Animation>() };
            if (result.Animation != null)
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (AnimationState state in result.Animation) names.Add(state.name);
                result.ClipNames = names.ToArray();
                if (result.Animation.clip != null)
                {
                    result.Animation.wrapMode = WrapMode.Loop;
                    result.Animation.Play(result.Animation.clip.name);
                }
            }
            result.Bounds = RendererBounds(root);
            return result;
        }

        public static Bounds RendererBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Keeps the glTFast import alive while the object exists and disposes it afterwards.</summary>
        class GltfImportOwner : MonoBehaviour
        {
            public GltfImport Import;
            void OnDestroy() => Import?.Dispose();
        }
    }
}
