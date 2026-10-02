"""Phase 6 spike: one drawing through the 「境」 pipeline outside Unity.

  python Tools/agent/world_spike.py <ink.png> [--model marble-1.0-draft] [--prompt "refine prompt"] [--text "world text"]
                                    [--refined <already refined.png>] [--name <output folder name>]

1. Refine the ink drawing into a realistic landscape image (gpt-image, 1536x1024).
2. World Labs: prepare_upload -> PUT the image -> worlds:generate -> poll the operation -> world.
3. Download every SPZ size (and the collider mesh, to see what it is) and save the world JSON.
Writes TripoHack-Maliang/My project/TestData/World/<name>_<time>/. Keys come from maliang.config.json (never printed).
"""
import base64
import json
import os
import sys
import time

import requests

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(HERE, "..", "..", "TripoHack-Maliang", "My project"))
CFG = json.load(open(os.path.join(PROJECT, "maliang.config.json"), encoding="utf-8"))
OPENAI = "https://api.openai.com/v1"
WL = "https://api.worldlabs.ai/marble/v1"
IMAGE_MODEL = CFG.get("imageGen", {}).get("model") or "gpt-image-2.5-sunburst"

DEFAULT_REFINE = (
    "Redraw this ink sketch as a realistic, photographic landscape of the same place: keep its layout, the shapes of "
    "the mountains, water, trees and buildings and where they sit, and any colours the painter used. Natural daylight, "
    "rich detail, a wide view with a clear foreground ground the viewer could stand on, middle distance and far distance. "
    "No ink-painting style, no brush strokes, no paper, no text, no frame, no seal, no people.")


def wl_headers():
    return {"WLT-Api-Key": CFG["worldLabs"]["apiKey"]}


def refine(ink_path, out_path, prompt):
    t = time.time()
    with open(ink_path, "rb") as f:
        r = requests.post(f"{OPENAI}/images/edits", headers={"Authorization": "Bearer " + CFG["vision"]["apiKey"]},
                          timeout=300, files={"image": ("ink.png", f, "image/png")},
                          data={"model": IMAGE_MODEL, "prompt": prompt, "size": "1536x1024", "quality": "high"})
    r.raise_for_status()
    open(out_path, "wb").write(base64.b64decode(r.json()["data"][0]["b64_json"]))
    print(f"refined in {time.time() - t:.1f}s -> {out_path}")


def upload(path):
    r = requests.post(f"{WL}/media-assets:prepare_upload", headers=wl_headers(), timeout=60,
                      json={"file_name": os.path.basename(path), "kind": "image", "extension": "png"})
    r.raise_for_status()
    info = r.json()
    asset_id = info["media_asset"]["media_asset_id"]
    up = info["upload_info"]
    headers = up.get("required_headers") or {}
    with open(path, "rb") as f:
        u = requests.request(up.get("upload_method", "PUT"), up["upload_url"], data=f.read(), headers=headers, timeout=120)
    u.raise_for_status()
    print(f"uploaded {os.path.basename(path)} as media asset {asset_id} (headers {list(headers)})")
    return asset_id


def generate(asset_id, model, text):
    body = {"display_name": "Maliang spike", "model": model,
            "world_prompt": {"type": "image", "image_prompt": {"source": "media_asset", "media_asset_id": asset_id}}}
    if text:
        body["world_prompt"]["text_prompt"] = text
    r = requests.post(f"{WL}/worlds:generate", headers=wl_headers(), json=body, timeout=60)
    if not r.ok:
        print("generate failed", r.status_code, r.text[:500])
        r.raise_for_status()
    op = r.json()
    print(f"operation {op['operation_id']} started ({model})")
    return op["operation_id"]


def poll(op_id, out_dir, timeout=1200):
    t0 = time.time()
    last = None
    while time.time() - t0 < timeout:
        r = requests.get(f"{WL}/operations/{op_id}", headers=wl_headers(), timeout=60)
        r.raise_for_status()
        op = r.json()
        meta = op.get("metadata")
        if json.dumps(meta) != last:
            print(f"  {time.time() - t0:5.0f}s metadata {meta}")
            last = json.dumps(meta)
        if op.get("done"):
            json.dump(op, open(os.path.join(out_dir, "operation.json"), "w"), indent=2)
            if op.get("error"):
                print("operation failed:", op["error"])
                return None
            print(f"done in {time.time() - t0:.0f}s, cost {op.get('cost')}")
            return op["response"]
        time.sleep(5)
    print("timed out")
    return None


def download(url, path):
    t = time.time()
    with requests.get(url, stream=True, timeout=300) as r:
        r.raise_for_status()
        with open(path, "wb") as f:
            for chunk in r.iter_content(1 << 20):
                f.write(chunk)
    print(f"  {os.path.basename(path)}: {os.path.getsize(path) / 1e6:.1f} MB in {time.time() - t:.1f}s")


def main(args):
    ink = args[0]
    model = args[args.index("--model") + 1] if "--model" in args else "marble-1.0-draft"
    prompt = args[args.index("--prompt") + 1] if "--prompt" in args else DEFAULT_REFINE
    text = args[args.index("--text") + 1] if "--text" in args else None
    given = args[args.index("--refined") + 1] if "--refined" in args else None
    name = args[args.index("--name") + 1] if "--name" in args else os.path.splitext(os.path.basename(ink))[0]
    out = os.path.join(PROJECT, "TestData", "World", f"{name}_{time.strftime('%H%M%S')}")
    os.makedirs(out, exist_ok=True)
    t0 = time.time()
    refined = os.path.join(out, "refined.png")
    if given:  # reuse a reference image (e.g. a library work's), no refine call
        open(refined, "wb").write(open(given, "rb").read())
    else:
        refine(ink, refined, prompt)
    world = poll(generate(upload(refined), model, text), out)
    if world is None:
        return
    json.dump(world, open(os.path.join(out, "world.json"), "w"), indent=2)
    assets = world.get("assets") or {}
    splats = assets.get("splats") or {}
    print("caption:", (assets.get("caption") or "")[:200])
    print("semantics:", splats.get("semantics_metadata"))
    print("spz sizes:", list((splats.get("spz_urls") or {}).keys()), "mesh:", list((assets.get("mesh") or {}).keys()))
    for size, url in (splats.get("spz_urls") or {}).items():
        download(url, os.path.join(out, f"world_{size}.spz"))
    collider = (assets.get("mesh") or {}).get("collider_mesh_url")
    if collider:
        download(collider, os.path.join(out, "collider.glb"))
    pano = (assets.get("imagery") or {}).get("pano_url")
    if pano:
        download(pano, os.path.join(out, "pano" + os.path.splitext(pano.split("?")[0])[1]))
    print(f"total {time.time() - t0:.0f}s -> {out}")


if __name__ == "__main__":
    main(sys.argv[1:])
