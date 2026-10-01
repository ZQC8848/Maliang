using System.Collections;
using System.IO;
using System.Linq;
using Maliang.Core;
using Maliang.Drawing;
using Maliang.Loading;
using Maliang.Ritual;
using Maliang.VR;
using UnityEngine;

namespace Maliang.Library
{
    /// <summary>
    /// The library in the drawers (Phase3Design 8.4): the left drawer holds the newest works stamped 「物」 as rolled
    /// scrolls, side by side (six at most), each tied with a red ribbon and tagged with its seal. They ride with the
    /// drawer, can be taken out like the spare, and laid on the drawing spot they unroll to the original drawing,
    /// rise by themselves and burn for a fixed 10 s into the original object (<see cref="ScrollRitual.BeginReplay"/>).
    /// A replay does not use the work up: a fresh scroll appears in its place afterwards (D20). New works add a scroll
    /// at once. 「境」 (right drawer) follows in Phase 6. Starts by itself in a scene with a <see cref="ScrollStation"/>.
    /// </summary>
    public class LibraryShelf : MonoBehaviour
    {
        public const int PerDrawer = 6;
        const float FloorY = 0.017f;       // drawer floor top above the drawer's origin (m)
        const float CentreZ = 0.284f;      // interior centre, from the drawer's origin towards its back (m)
        const float Spacing = 0.075f;      // between scrolls, side by side (m)

        public Drawer drawer;
        public ScrollStation station;
        public SealType seal = SealType.Object;
        public Color ribbonColor = new Color(0.7f, 0.08f, 0.06f);

        readonly Transform[] _slots = new Transform[PerDrawer];
        readonly ScrollRitual[] _inSlot = new ScrollRitual[PerDrawer];
        Material _ribbon, _tagPaper, _tagSeal;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            var station = FindAnyObjectByType<ScrollStation>();
            if (station == null || station.scrollPrefab == null) return;
            var drawers = FindObjectsByType<Drawer>().OrderBy(d => d.transform.position.x).ToArray();
            if (drawers.Length == 0) return;
            var shelf = new GameObject("Library Shelf (Object)").AddComponent<LibraryShelf>();
            shelf.drawer = drawers[0]; // the left drawer
            shelf.station = station;
        }

        void Start()
        {
            for (int i = 0; i < PerDrawer; i++)
            {
                var slot = new GameObject($"Library Slot {i}").transform;
                slot.SetParent(drawer.transform, false);
                slot.localPosition = new Vector3((i - (PerDrawer - 1) * 0.5f) * Spacing, FloorY + 0.005f, CentreZ);
                slot.localRotation = Quaternion.identity;
                _slots[i] = slot;
            }
            Fill();
            ArtLibrary.Added += OnAdded;
        }

        void OnDestroy() => ArtLibrary.Added -= OnAdded;

        void OnAdded(LibraryEntry e)
        {
            if (e.seal != seal.ToString()) return;
            Fill();
            Sfx.Play(SfxId.ScrollThud, drawer.transform.position, 0.3f); // a soft knock in the drawer
        }

        /// <summary>Lays out the newest works; slots whose scroll is out on the desk are left alone.</summary>
        void Fill()
        {
            var works = ArtLibrary.List(seal, PerDrawer);
            for (int i = 0; i < PerDrawer; i++)
            {
                var current = _inSlot[i];
                bool inDrawer = current != null && current.State == ScrollState.Rolled && current.transform.parent == _slots[i];
                if (current != null && !inDrawer) continue; // out on the desk; refilled when it is done
                if (current != null) Destroy(current.gameObject);
                _inSlot[i] = i < works.Count ? Spawn(works[i], i) : null;
            }
        }

        ScrollRitual Spawn(LibraryEntry work, int slot)
        {
            var t = _slots[slot];
            var ritual = Instantiate(station.scrollPrefab, t.position, t.rotation, t);
            ritual.name = $"Library Scroll ({work.subject})";
            ritual.head = station.head;
            ritual.startMode = ScrollStartMode.Rolled;
            var pickup = ritual.GetComponent<ScrollPickup>();
            pickup.station = station;
            pickup.pickableOnStart = true;
            pickup.restPose = t; // put back in the drawer, wherever the drawer is

            // The drawing and seal exactly as they were.
            bool ok = ritual.canvas.LoadLayers(Read(work.PathOf(work.files.ink)), Read(work.PathOf(work.files.seal)));
            if (!ok) MaliangLog.Warn("Library", $"{work.id}: drawing layers unreadable");
            var job = Replay(work);
            ritual.BeginReplay(job, seal);
            ritual.BurnedAway += () => StartCoroutine(Refill(slot, ritual, 1.5f));
            ritual.Failed += _ => StartCoroutine(Refill(slot, ritual, 16f));
            Decorate(ritual.transform);
            return ritual;
        }

        /// <summary>The replay's load: the object is built out of sight while the scroll burns; broken files fail as faded.</summary>
        static ReplayJob Replay(LibraryEntry work)
        {
            ReplayJob job = null;
            job = new ReplayJob(async cancel =>
            {
                if (work.MissingFiles().Count > 0) return false;
                job.Prepared = await Summoning.PrepareAsync(ObjectSpawner.Request.From(work), cancel);
                return job.Prepared != null;
            });
            return job;
        }

        IEnumerator Refill(int slot, ScrollRitual used, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_inSlot[slot] != used) yield break;
            _inSlot[slot] = null;
            Fill();
        }

        static byte[] Read(string path) => path != null && File.Exists(path) ? File.ReadAllBytes(path) : null;

        // ------------------------------------------------------------------ ribbon and seal tag

        /// <summary>A red ribbon round the rolled bundle and a paper tag at its end with the seal on it; gone once unrolled.</summary>
        void Decorate(Transform scroll)
        {
            var ritual = scroll.GetComponent<ScrollRitual>();
            var u = ritual.unroll;
            float r = u != null ? u.rolledRadius : 0.016f;
            float drop = u != null ? u.restDrop : 0.005f;
            float length = ritual.canvas.size.y;
            var deco = new GameObject("Library Marks").transform;
            deco.SetParent(scroll, false);

            if (_ribbon == null)
            {
                _ribbon = Lit(ribbonColor, 0.35f);
                _tagPaper = Lit(new Color(0.93f, 0.88f, 0.76f), 0.1f);
            }
            // Ribbon: a thin band round both rods, a little off-centre.
            var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(band.GetComponent<Collider>());
            band.name = "Ribbon";
            band.transform.SetParent(deco, false);
            band.transform.localPosition = new Vector3(0f, r - drop, length * 0.18f);
            band.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            band.transform.localScale = new Vector3(r * 4.6f, 0.003f, r * 2.5f);
            band.GetComponent<Renderer>().sharedMaterial = _ribbon;

            // Tag: a small paper card hanging from the ribbon at the side of the roll, the seal printed on it.
            var tagRoot = new GameObject("Seal Tag").transform;
            tagRoot.SetParent(deco, false);
            tagRoot.localPosition = new Vector3(r * 2.4f, (r - drop) * 0.6f, length * 0.18f);
            tagRoot.localRotation = Quaternion.Euler(0f, 0f, -70f);
            var paper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(paper.GetComponent<Collider>());
            paper.name = "Paper";
            paper.transform.SetParent(tagRoot, false);
            paper.transform.localScale = new Vector3(0.03f, 0.0015f, 0.022f);
            paper.GetComponent<Renderer>().sharedMaterial = _tagPaper;
            var sealTex = SealTexture();
            if (sealTex != null)
            {
                if (_tagSeal == null) _tagSeal = new Material(Shader.Find("Sprites/Default")) { mainTexture = sealTex };
                var stamp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(stamp.GetComponent<Collider>());
                stamp.name = "Seal";
                stamp.transform.SetParent(tagRoot, false);
                stamp.transform.localPosition = new Vector3(0f, 0.0009f, 0f);
                stamp.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // faces out of the card
                stamp.transform.localScale = new Vector3(0.02f, 0.016f, 1f);
                stamp.GetComponent<Renderer>().sharedMaterial = _tagSeal;
            }
            ritual.Unrolled += () => { if (deco != null) deco.gameObject.SetActive(false); };
        }

        Texture SealTexture()
        {
            foreach (var s in FindObjectsByType<SealStamp>())
                if (s.type == seal) return s.sealTexture;
            return null;
        }

        static Material Lit(Color c, float smoothness)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }
    }
}
