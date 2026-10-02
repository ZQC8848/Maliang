"""Copies chosen library works into StreamingAssets/Library/ as the bundled works (Phase 7, Phase3Design 8.8).

  python Tools/library/bundle_works.py [Tools/library/bundled.json]

The manifest lists, per work: the source work id in the player's library (persistentDataPath/Library), the bundled id
(folder name; also the drawer order), the keywords the fallback matches against, and for a world optionally a folder
from Tools/agent/world_spike.py whose world_full_res.spz and world.json (semantics) replace the work's splats.
Only what a replay needs is copied (layers, model, clips, sound, world, ambience); the refined image and plan stay out.
"""
import json
import os
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(HERE, "..", "..", "TripoHack-Maliang", "My project"))
SOURCE = os.path.join(os.environ["USERPROFILE"], "AppData", "LocalLow", "DefaultCompany", "My project", "Library")
TARGET = os.path.join(PROJECT, "Assets", "StreamingAssets", "Library")


def bundle(item):
    src = os.path.join(SOURCE, item["source"])
    entry = json.load(open(os.path.join(src, "entry.json"), encoding="utf-8"))
    dst = os.path.join(TARGET, item["id"])
    if os.path.isdir(dst):
        shutil.rmtree(dst)
    os.makedirs(dst)

    files = entry["files"]
    keep = [files.get(k) for k in ("ink", "seal", "model", "sound", "world", "ambience")] + list(files.get("clips") or [])
    for name in filter(None, keep):
        shutil.copy2(os.path.join(src, name), os.path.join(dst, name))
    files["refined"] = None
    files["plan"] = None

    world_dir = item.get("world")
    if world_dir:  # a regenerated world (e.g. marble-1.1) replaces the work's splats
        world_dir = os.path.join(PROJECT, "TestData", "World", world_dir)
        shutil.copy2(os.path.join(world_dir, "world_full_res.spz"), os.path.join(dst, "world.spz"))
        world = json.load(open(os.path.join(world_dir, "world.json"), encoding="utf-8"))
        splats = (world.get("assets") or {}).get("splats") or {}
        semantics = splats.get("semantics_metadata") or {}
        entry["world"].update({
            "splatSize": "full_res",
            "metricScale": semantics.get("metric_scale_factor"),
            "groundOffset": semantics.get("ground_plane_offset"),
            "worldId": world.get("world_id"),
            "marbleUrl": world.get("world_marble_url"),
            "caption": (world.get("assets") or {}).get("caption"),
            "model": world.get("model") or item.get("model", "marble-1.1"),
        })

    entry["id"] = item["id"]
    entry["bundledFrom"] = item["source"]  # the player's original, hidden in favour of this copy
    entry["keywords"] = item["keywords"]
    json.dump(entry, open(os.path.join(dst, "entry.json"), "w", encoding="utf-8"), indent=2, ensure_ascii=False)
    size = sum(os.path.getsize(os.path.join(dst, f)) for f in os.listdir(dst)) / 1e6
    print(f"{item['id']}: \"{entry.get('subject')}\" ({entry['seal']}) {size:.1f} MB")


def main(args):
    manifest = args[0] if args else os.path.join(HERE, "bundled.json")
    items = json.load(open(manifest, encoding="utf-8"))
    os.makedirs(TARGET, exist_ok=True)
    wanted = {i["id"] for i in items}
    for old in os.listdir(TARGET):  # works dropped from the manifest go (with their .meta)
        path = os.path.join(TARGET, old)
        if os.path.isdir(path) and old not in wanted:
            shutil.rmtree(path)
            if os.path.exists(path + ".meta"):
                os.remove(path + ".meta")
    for item in items:
        bundle(item)


if __name__ == "__main__":
    main(sys.argv[1:])
