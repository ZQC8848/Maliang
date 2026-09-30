using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Gsplat;
using Maliang.Core;
using UnityEngine;

namespace Maliang.Loading
{
    /// <summary>World Labs alignment data (semantics_metadata) plus a manual rotation used while tuning.</summary>
    [Serializable]
    public struct WorldAlignment
    {
        public float metricScaleFactor;
        public float groundPlaneOffset;
        public Vector3 extraEulerRotation;

        public static WorldAlignment Identity => new WorldAlignment { metricScaleFactor = 1f, groundPlaneOffset = 0f, extraEulerRotation = Vector3.zero };
    }

    /// <summary>
    /// Loads a Gaussian splat world (SPZ) and an optional collider GLB under one root transform,
    /// then applies scale / ground alignment to that root (TechPlan §8.2).
    /// </summary>
    public class SplatWorldLoader : MonoBehaviour
    {
        [Tooltip("Parent of the splat renderer and collider. Alignment is applied to this transform.")]
        public Transform worldRoot;

        GsplatRenderer _renderer;
        GsplatAssetSpz _asset;
        GameObject _colliderRoot;

        public bool HasWorld => _asset != null;
        public uint SplatCount => _renderer != null ? _renderer.SplatCount : 0;
        public int LoadedSplats { get; private set; }
        public double LastLoadSeconds { get; private set; }

        /// <summary>True once the renderer has a valid asset and has started uploading/drawing.</summary>
        public bool IsRendering => _renderer != null && _renderer.isActiveAndEnabled && _renderer.Valid;
        public Vector3 BoundsSize => _renderer != null && _renderer.Valid ? _renderer.Bounds.size : Vector3.zero;

        Transform Root => worldRoot != null ? worldRoot : transform;

        /// <summary>Synchronous decode; blocks the main thread for the duration of the load (a few seconds for ~1M splats).</summary>
        public void LoadSpz(string spzPath, SourceCoordinates sourceCoordinates)
        {
            if (!File.Exists(spzPath)) throw new FileNotFoundException("SPZ not found", spzPath);

            var sw = Stopwatch.StartNew();
            var asset = ScriptableObject.CreateInstance<GsplatAssetSpz>();
            asset.LoadFromSpz(spzPath, sourceCoordinates);
            sw.Stop();

            if (_renderer == null)
            {
                var go = new GameObject("GsplatWorld");
                go.transform.SetParent(Root, false);
                _renderer = go.AddComponent<GsplatRenderer>();
            }

            var old = _asset;
            _renderer.GsplatAsset = asset;
            _asset = asset;
            if (old != null) Destroy(old);

            LoadedSplats = (int)asset.SplatCount;
            LastLoadSeconds = sw.Elapsed.TotalSeconds;
            MaliangLog.Info("SplatWorld", $"Loaded {LoadedSplats:N0} splats (SH bands {asset.SHBands}) as {sourceCoordinates} in {LastLoadSeconds:F1}s from {spzPath}");
        }

        /// <summary>Loads the collider GLB under the same root and keeps only the colliders, no renderers.</summary>
        public async Task LoadColliderAsync(string glbPath)
        {
            if (!File.Exists(glbPath)) throw new FileNotFoundException("Collider GLB not found", glbPath);

            if (_colliderRoot != null) Destroy(_colliderRoot);
            _colliderRoot = new GameObject("GsplatCollider");
            _colliderRoot.transform.SetParent(Root, false);

            var gltf = new GLTFast.GltfImport();
            if (!await gltf.Load(glbPath))
            {
                MaliangLog.Error("SplatWorld", $"glTFast could not load {glbPath}");
                return;
            }
            if (!await gltf.InstantiateMainSceneAsync(_colliderRoot.transform))
            {
                MaliangLog.Error("SplatWorld", $"glTFast could not instantiate {glbPath}");
                return;
            }

            int count = 0;
            foreach (var mf in _colliderRoot.GetComponentsInChildren<MeshFilter>())
            {
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = false;
                count++;
            }
            MaliangLog.Info("SplatWorld", $"Collider ready: {count} mesh collider(s) from {glbPath}");
        }

        /// <summary>Scale applies to the whole root (points and their sizes); the ground offset lifts or lowers the world so the floor sits at y = 0.</summary>
        public void Align(WorldAlignment a)
        {
            var root = Root;
            float s = a.metricScaleFactor > 0f ? a.metricScaleFactor : 1f;
            root.localScale = Vector3.one * s;
            root.localRotation = Quaternion.Euler(a.extraEulerRotation);
            root.localPosition = new Vector3(0f, a.groundPlaneOffset, 0f);
        }

        public void Unload()
        {
            if (_renderer != null) Destroy(_renderer.gameObject);
            if (_colliderRoot != null) Destroy(_colliderRoot);
            if (_asset != null) Destroy(_asset);
            _renderer = null; _colliderRoot = null; _asset = null;
        }

        void OnDestroy() => Unload();
    }
}
