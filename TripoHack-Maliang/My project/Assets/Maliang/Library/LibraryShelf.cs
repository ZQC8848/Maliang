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
    /// at once. The right drawer holds the 「境」 works (cyan ribbon), which burn into their world around the throne;
    /// its leftmost slot always keeps the sky scroll: unrolled it shows the sky, burned it clears the world away
    /// (Phase 6). Starts by itself in a scene with a <see cref="ScrollStation"/>.
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
        [Tooltip("Keep the sky scroll in the leftmost slot (the 「境」 drawer).")]
        public bool skyScroll;

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
            var objects = new GameObject("Library Shelf (Object)").AddComponent<LibraryShelf>();
            objects.drawer = drawers[0]; // the left drawer
            objects.station = station;
            if (drawers.Length < 2) return;
            var worlds = new GameObject("Library Shelf (World)").AddComponent<LibraryShelf>();
            worlds.drawer = drawers[drawers.Length - 1]; // the right drawer
            worlds.station = station;
            worlds.seal = SealType.World;
            worlds.ribbonColor = new Color(0.1f, 0.55f, 0.6f);
            worlds.skyScroll = true;
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

        /// <summary>Lays out the newest works (after the sky scroll, if kept); slots whose scroll is out on the desk are left alone.</summary>
        void Fill()
        {
            int first = skyScroll ? 1 : 0;
            var works = ArtLibrary.List(seal, PerDrawer - first);
            for (int i = 0; i < PerDrawer; i++)
            {
                var current = _inSlot[i];
                bool inDrawer = current != null && current.State == ScrollState.Rolled && current.transform.parent == _slots[i];
                if (current != null && !inDrawer) continue; // out on the desk; refilled when it is done
                if (skyScroll && i == 0)
                {
                    if (current == null) _inSlot[0] = SpawnSky();
                    continue;
                }
                if (current != null) Destroy(current.gameObject);
                int w = i - first;
                _inSlot[i] = w < works.Count ? Spawn(works[w], i) : null;
            }
        }

        ScrollRitual Spawn(LibraryEntry work, int slot)
        {
            var ritual = NewScroll(slot, $"Library Scroll ({work.subject})");
            // The drawing and seal exactly as they were.
            bool ok = ritual.canvas.LoadLayers(Read(work.PathOf(work.files.ink)), Read(work.PathOf(work.files.seal)));
            if (!ok) MaliangLog.Warn("Library", $"{work.id}: drawing layers unreadable");
            ritual.BeginReplay(Replay(work), seal);
            return Finish(ritual, slot);
        }

        /// <summary>The sky scroll: shows the sky, can be neither painted nor sealed; burned, the world fades and the sky returns.</summary>
        ScrollRitual SpawnSky()
        {
            var ritual = NewScroll(0, "Library Scroll (Sky)");
            var sky = Resources.Load<Texture2D>("SkyScroll");
            if (sky != null) ritual.canvas.LoadLayers(sky, null);
            var job = new ReplayJob(_ => System.Threading.Tasks.Task.FromResult(true))
            {
                OnBurnedAway = _ => WorldStage.Instance.Clear(),
            };
            ritual.BeginReplay(job, SealType.World);
            return Finish(ritual, 0);
        }

        ScrollRitual NewScroll(int slot, string name)
        {
            var t = _slots[slot];
            var ritual = Instantiate(station.scrollPrefab, t.position, t.rotation, t);
            ritual.name = name;
            ritual.head = station.head;
            ritual.startMode = ScrollStartMode.Rolled;
            var pickup = ritual.GetComponent<ScrollPickup>();
            pickup.station = station;
            pickup.pickableOnStart = true;
            pickup.restPose = t; // put back in the drawer, wherever the drawer is
            return ritual;
        }

        ScrollRitual Finish(ScrollRitual ritual, int slot)
        {
            ritual.BurnedAway += () => StartCoroutine(Refill(slot, ritual, 1.5f));
            ritual.Failed += _ => StartCoroutine(Refill(slot, ritual, 16f));
            Decorate(ritual.transform);
            return ritual;
        }

        /// <summary>
        /// The replay's load. An object is built out of sight while the scroll burns; a world only needs its files,
        /// and rises once the scroll has burned away. Broken or missing files fail as faded.
        /// </summary>
        ReplayJob Replay(LibraryEntry work)
        {
            ReplayJob job = null;
            if (seal == SealType.World)
            {
                job = new ReplayJob(_ => System.Threading.Tasks.Task.FromResult(work.MissingFiles().Count == 0))
                {
                    OnBurnedAway = _ => WorldStage.Instance.Show(work),
                };
                return job;
            }
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
            ritual.Unrolling += () => { if (deco != null) deco.gameObject.SetActive(false); }; // the seal breaks as it opens
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
