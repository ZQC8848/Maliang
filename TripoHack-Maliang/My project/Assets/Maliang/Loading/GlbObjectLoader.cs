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
    /// in player builds; the first clip loops. Tripo returns one clip per retarget request, so further clips on the
    /// same rig come from extra GLBs and are merged onto the first model's Animation (same bone names and paths).
    /// </summary>
    public static class GlbObjectLoader
    {
        public class Result
        {
            public GameObject Root;
            public Animation Animation;        // null for a static model
            public string[] ClipNames = new string[0];   // in order: the model's own clip, then the merged ones
            public Bounds Bounds;              // renderer bounds in world space after loading
        }

        public static async Task<Result> LoadAsync(string path, Transform parent = null, CancellationToken cancel = default,
            params string[] extraClipPaths)
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
            // The import owns the meshes, materials, textures and clips; release them with the object.
            var owner = root.AddComponent<GltfImportOwner>();
            owner.Imports.Add(gltf);

            var result = new Result { Root = root, Animation = root.GetComponentInChildren<Animation>() };
            if (result.Animation != null)
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (AnimationState state in result.Animation) names.Add(state.name);
                for (int i = 0; i < (extraClipPaths?.Length ?? 0); i++)
                {
                    var clip = await LoadClipAsync(extraClipPaths[i], owner, cancel);
                    if (clip == null) continue;
                    string name = "extra_" + (i + 1);
                    result.Animation.AddClip(clip, name);
                    names.Add(name);
                }
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

        /// <summary>Loads a GLB only for its first animation clip; the import stays owned by <paramref name="owner"/>.</summary>
        static async Task<AnimationClip> LoadClipAsync(string path, GltfImportOwner owner, CancellationToken cancel)
        {
            var gltf = new GltfImport();
            if (!await gltf.LoadFile(path, null, new ImportSettings { AnimationMethod = AnimationMethod.Legacy }, cancel))
            {
                MaliangLog.Warn("Glb", $"Failed to load clip from {path}");
                gltf.Dispose();
                return null;
            }
            var clips = gltf.GetAnimationClips();
            if (clips == null || clips.Length == 0)
            {
                gltf.Dispose();
                return null;
            }
            owner.Imports.Add(gltf);
            return clips[0];
        }

        /// <summary>Keeps the glTFast imports alive while the object exists and disposes them afterwards.</summary>
        class GltfImportOwner : MonoBehaviour
        {
            public readonly System.Collections.Generic.List<GltfImport> Imports = new System.Collections.Generic.List<GltfImport>();
            void OnDestroy()
            {
                foreach (var i in Imports) i.Dispose();
            }
        }
    }
}
