"""Generate a 3D model from an image with the Tripo API (v3) and download the GLB.

Usage:
    python Tools/tripo/image_to_model.py [--face-limit N] <image> <output.glb> [<image> <output.glb> ...]

The API key is read from TRIPO_API_KEY, or from "tripo.apiKey" in TripoHack-Maliang/My project/maliang.config.json
(git-ignored). Images that are not PNG/JPEG (e.g. WebP) are converted to PNG before upload.
All jobs run in parallel; each GLB is downloaded as soon as its task succeeds (output URLs expire after ~5 minutes).
"""
import io
import json
import os
import sys
import threading
import time

import requests
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
CONFIG = os.path.join(HERE, "..", "..", "TripoHack-Maliang", "My project", "maliang.config.json")
API = "https://openapi.tripo3d.ai/v3"

# Newest high-precision model and the quality settings used for hero props.
TASK_SETTINGS = {
    "model": "v3.1-20260211",
    "texture": True,
    "pbr": True,
    "texture_quality": "detailed",
    "geometry_quality": "detailed",
    "face_limit": 50000,  # hand-held VR prop: keep it light; fine engraving detail lives in the PBR maps
    "orientation": "default",
}
POLL_SECONDS = 5
TIMEOUT_SECONDS = 900


def api_key():
    key = os.environ.get("TRIPO_API_KEY")
    if key:
        return key
    with open(CONFIG, encoding="utf-8") as f:
        key = json.load(f).get("tripo", {}).get("apiKey")
    if not key:
        sys.exit(f"No Tripo API key: set TRIPO_API_KEY or tripo.apiKey in {os.path.normpath(CONFIG)}")
    return key


def headers():
    return {"Authorization": f"Bearer {api_key()}"}


def check(resp, what):
    try:
        body = resp.json()
    except ValueError:
        body = {"raw": resp.text[:500]}
    if resp.status_code != 200 or body.get("code", 0) != 0:
        raise RuntimeError(f"{what} failed: HTTP {resp.status_code} {json.dumps(body, ensure_ascii=False)[:800]}")
    return body["data"]


def upload(path):
    img = Image.open(path)
    if img.format not in ("PNG", "JPEG"):
        buf = io.BytesIO()
        img.convert("RGBA" if img.mode in ("RGBA", "LA", "P") else "RGB").save(buf, format="PNG")
        name, data, mime = os.path.splitext(os.path.basename(path))[0] + ".png", buf.getvalue(), "image/png"
    else:
        with open(path, "rb") as f:
            data = f.read()
        name, mime = os.path.basename(path), "image/png" if img.format == "PNG" else "image/jpeg"
    resp = requests.post(f"{API}/files", headers=headers(), files={"file": (name, data, mime)}, timeout=120)
    return check(resp, f"upload {name}")["file_token"]


def create_task(file_token):
    body = dict(TASK_SETTINGS, input=file_token)
    resp = requests.post(f"{API}/generation/image-to-model", headers=headers(), json=body, timeout=60)
    return check(resp, "create task")["task_id"]


def wait(task_id, label):
    start = time.time()
    last = None
    while time.time() - start < TIMEOUT_SECONDS:
        data = check(requests.get(f"{API}/tasks/{task_id}", headers=headers(), timeout=60), "query task")
        status, progress = data.get("status"), data.get("progress")
        if (status, progress) != last:
            print(f"[{label}] {status} {progress}%", flush=True)
            last = (status, progress)
        if status == "success":
            return data
        if status in ("failed", "cancelled", "banned", "expired", "unknown"):
            raise RuntimeError(f"[{label}] task {task_id} ended with status {status}: {json.dumps(data, ensure_ascii=False)[:800]}")
        time.sleep(POLL_SECONDS)
    raise TimeoutError(f"[{label}] task {task_id} still running after {TIMEOUT_SECONDS}s")


def download(url, out_path):
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    with requests.get(url, stream=True, timeout=300) as r:
        r.raise_for_status()
        with open(out_path, "wb") as f:
            for chunk in r.iter_content(1 << 16):
                f.write(chunk)
    return os.path.getsize(out_path)


def run(image, out_glb, results):
    label = os.path.basename(out_glb)
    try:
        token = upload(image)
        task_id = create_task(token)
        print(f"[{label}] task {task_id} created", flush=True)
        data = wait(task_id, label)
        output = data.get("output", {})
        size = download(output["model_url"], out_glb)
        preview = output.get("rendered_image_url")
        if preview:
            download(preview, os.path.splitext(out_glb)[0] + "_preview" + os.path.splitext(preview.split("?")[0])[1])
        results[label] = f"OK {size / 1e6:.1f} MB, credits {data.get('credits_consumed')}, task {task_id}"
    except Exception as e:  # report per job, keep the other jobs running
        results[label] = f"FAILED: {e}"


def main(argv):
    if len(argv) >= 2 and argv[0] == "--face-limit":
        TASK_SETTINGS["face_limit"] = int(argv[1])
        argv = argv[2:]
    if len(argv) < 2 or len(argv) % 2:
        sys.exit(__doc__)
    results = {}
    jobs = [threading.Thread(target=run, args=(argv[i], argv[i + 1], results)) for i in range(0, len(argv), 2)]
    for j in jobs:
        j.start()
    for j in jobs:
        j.join()
    for k, v in results.items():
        print(f"{k}: {v}")
    if any(v.startswith("FAILED") for v in results.values()):
        sys.exit(1)


if __name__ == "__main__":
    main(sys.argv[1:])
