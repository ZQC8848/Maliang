"""Phase 3 spike tool: runs each step of the object agent pipeline outside Unity (Phase3Design.md).

Reads keys from TripoHack-Maliang/My project/maliang.config.json (git-ignored) and the shared prompt files from
Assets/StreamingAssets/Prompts/. Writes everything to TripoHack-Maliang/My project/TestData/Agent/ (git-ignored).

    python Tools/agent/agent_spike.py testset                 # synthetic ink drawings + scribbles for the spikes
    python Tools/agent/agent_spike.py plan <png> [<png> ...]  # GPT vision plan (structured output)
    python Tools/agent/agent_spike.py refine <name>           # image refine from the plan's refine_prompt
    python Tools/agent/agent_spike.py sound <name>            # ElevenLabs sound from the plan
    python Tools/agent/agent_spike.py tripo <name> [--model M] [--no-animate]   # generate, rig-check, rig, retarget

<name> is a folder under TestData/Agent/runs/ created by "plan" (the PNG's file name without extension).
"""
import base64
import json
import os
import random
import sys
import time

import requests
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(HERE, "..", "..", "TripoHack-Maliang", "My project"))
PROMPTS = os.path.join(PROJECT, "Assets", "StreamingAssets", "Prompts")
OUT = os.path.join(PROJECT, "TestData", "Agent")
RUNS = os.path.join(OUT, "runs")

CFG = json.load(open(os.path.join(PROJECT, "maliang.config.json"), encoding="utf-8"))
OPENAI = "https://api.openai.com/v1"
TRIPO = "https://openapi.tripo3d.ai/v3"
TEXT_MODEL = CFG["vision"].get("model") or "gpt-6.1-sol"
IMAGE_MODEL = "gpt-image-2.5-sunburst"


def oa_headers():
    return {"Authorization": "Bearer " + CFG["vision"]["apiKey"]}


def tripo_headers():
    return {"Authorization": "Bearer " + CFG["tripo"]["apiKey"]}


def run_dir(name):
    d = os.path.join(RUNS, name)
    os.makedirs(d, exist_ok=True)
    return d


def save_json(path, obj):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, indent=2, ensure_ascii=False)


def timed(label, fn):
    t = time.time()
    result = fn()
    print(f"  [{label}] {time.time() - t:.1f}s")
    return result


# ------------------------------------------------------------------ test set

TEST_SUBJECTS = {
    # clear
    "horse": "a horse standing", "person": "a robed immortal standing", "koi": "a koi fish",
    "crane": "a red-crowned crane", "lantern": "a paper lantern", "tiger": "a walking tiger",
    "dragon": "a long Chinese dragon", "teapot": "a teapot", "spider": "a spider", "lotus": "a lotus flower",
    # loose
    "loose_mountain": "a distant mountain, only three strokes", "loose_bird": "a small bird, very few strokes",
    "loose_boat": "a fishing boat, a few rough strokes", "loose_bamboo": "two stalks of bamboo, quick strokes",
    "loose_cat": "a sitting cat, childlike and clumsy",
}


def cmd_testset():
    d = os.path.join(OUT, "testset")
    os.makedirs(d, exist_ok=True)
    for name, subject in TEST_SUBJECTS.items():
        path = os.path.join(d, name + ".png")
        if os.path.exists(path):
            continue
        prompt = (f"A quick hand-painted Chinese ink brush sketch of {subject}, painted by an amateur in VR with one soft "
                  "round brush, black ink with a little colour at most, on plain cream rice paper. Loose, simple, few "
                  "strokes, no background scenery, no text, no seal, no frame.")
        r = requests.post(f"{OPENAI}/images/generations", headers=oa_headers(), timeout=300,
                          json={"model": "gpt-image-2.5-flare", "prompt": prompt, "size": "1536x1024", "quality": "low"})
        if not r.ok:
            print(name, "FAILED", r.status_code, r.text[:300])
            continue
        open(path, "wb").write(base64.b64decode(r.json()["data"][0]["b64_json"]))
        print("drawing", name)
    # scribbles: random brush strokes on cream paper
    rng = random.Random(7)
    for i in range(5):
        path = os.path.join(d, f"scribble_{i + 1}.png")
        if os.path.exists(path):
            continue
        img = Image.new("RGB", (1536, 1024), (242, 234, 214))
        g = ImageDraw.Draw(img)
        for _ in range(rng.randint(3, 12)):
            pts = [(rng.randint(150, 1386), rng.randint(120, 904))]
            for _ in range(rng.randint(3, 9)):
                x, y = pts[-1]
                pts.append((min(1450, max(80, x + rng.randint(-260, 260))), min(950, max(60, y + rng.randint(-200, 200)))))
            g.line(pts, fill=(25, 22, 20), width=rng.randint(6, 26), joint="curve")
        if i == 4:  # nearly blank
            img = Image.new("RGB", (1536, 1024), (242, 234, 214))
            ImageDraw.Draw(img).line([(700, 500), (760, 515)], fill=(25, 22, 20), width=8)
        img.filter(ImageFilter.GaussianBlur(1.2)).save(path)
        print("scribble", i + 1)


# ------------------------------------------------------------------ vision plan

def system_prompt():
    text = open(os.path.join(PROMPTS, "vision_object.txt"), encoding="utf-8").read()
    caps = json.load(open(os.path.join(PROMPTS, "object_capabilities.json"), encoding="utf-8"))
    caps.pop("_comment", None)
    return text.replace("{{CAPABILITIES}}", json.dumps(caps, indent=1))


def validate(plan):
    """Client-side checks from Phase3Design 4.3: drop anything outside the allow-lists."""
    caps = json.load(open(os.path.join(PROMPTS, "object_capabilities.json"), encoding="utf-8"))["categories"]
    notes = []
    if plan["status"] != "ok":
        return notes
    min_conf = CFG["vision"].get("minConfidence", 0.6)
    if plan.get("confidence", 1.0) < min_conf:
        notes.append(f"'{plan['subject']}' at confidence {plan['confidence']:.2f} < {min_conf}: unrecognizable")
        plan["status"], plan["reason"] = "fail", "unrecognizable"
        return notes
    cat = caps.get(plan.get("category") or "", {})
    anim = plan["animate"]
    if anim["wanted"]:
        allowed = set(cat.get("presets", []))
        kept = [a for a in anim["animations"] if a in allowed]
        if kept != anim["animations"]:
            notes.append(f"dropped presets {set(anim['animations']) - set(kept)}")
        anim["animations"] = kept[:2]
        if not cat.get("riggable") or not kept:
            notes.append("animation off (category not riggable or no valid preset)")
            anim["wanted"] = False
    s = plan["sound"]
    if s["wanted"] and s["duration_s"] is not None:
        s["duration_s"] = min(30.0, max(0.5, s["duration_s"]))
    if plan.get("size_m") is not None:
        plan["size_m"] = min(1.2, max(0.15, plan["size_m"]))
    return notes


def cmd_plan(paths):
    # a directory means every PNG in it
    expanded = []
    for p in paths:
        expanded += sorted(os.path.join(p, f) for f in os.listdir(p) if f.lower().endswith(".png")) if os.path.isdir(p) else [p]
    paths = expanded
    sysp = system_prompt()
    schema = json.load(open(os.path.join(PROMPTS, "vision_object.schema.json"), encoding="utf-8"))
    for path in paths:
        name = os.path.splitext(os.path.basename(path))[0]
        d = run_dir(name)
        Image.open(path).convert("RGB").save(os.path.join(d, "ink.png"))
        b64 = base64.b64encode(open(path, "rb").read()).decode()
        body = {
            "model": TEXT_MODEL,
            "input": [
                {"role": "system", "content": [{"type": "input_text", "text": sysp}]},
                {"role": "user", "content": [{"type": "input_text", "text": "Seal: OBJECT"},
                                             {"type": "input_image", "image_url": "data:image/png;base64," + b64}]},
            ],
            "text": {"format": {"type": "json_schema", "name": "vision_plan", "strict": True, "schema": schema}},
        }
        t = time.time()
        r = requests.post(f"{OPENAI}/responses", headers=oa_headers(), json=body, timeout=180)
        dt = time.time() - t
        if not r.ok:
            print(f"{name}: HTTP {r.status_code} {r.text[:400]}")
            continue
        data = r.json()
        text = next(c["text"] for o in data["output"] if o.get("type") == "message"
                    for c in o["content"] if c.get("type") == "output_text")
        plan = json.loads(text)
        notes = validate(plan)
        save_json(os.path.join(d, "plan.json"), plan)
        usage = data.get("usage", {})
        anim = plan["animate"]
        snd = plan["sound"]
        print(f"{name}: {plan['status']:4} {dt:5.1f}s c={plan.get('confidence', 0):.2f}  "
              + (f"reason={plan['reason']}  seen='{plan['seen']}'" if plan["status"] == "fail" else
                 f"{plan['subject']} [{plan['category']}] anim={anim['animations'] if anim['wanted'] else '-'} "
                 f"sound={(snd['kind'] + '/' + snd['trigger'] + ': ' + snd['prompt']) if snd['wanted'] else '-'} size={plan['size_m']}")
              + (f"  NOTES {notes}" if notes else "")
              + f"  tokens in/out {usage.get('input_tokens')}/{usage.get('output_tokens')}")


# ------------------------------------------------------------------ image refine

def cmd_refine(name, model=IMAGE_MODEL):
    d = run_dir(name)
    plan = json.load(open(os.path.join(d, "plan.json"), encoding="utf-8"))
    if plan["status"] != "ok":
        print(name, "plan failed; nothing to refine")
        return
    t = time.time()
    with open(os.path.join(d, "ink.png"), "rb") as f:
        r = requests.post(f"{OPENAI}/images/edits", headers=oa_headers(), timeout=300,
                          files={"image": ("ink.png", f, "image/png")},
                          data={"model": model, "prompt": plan["refine_prompt"], "size": "1024x1024", "quality": "high"})
    if not r.ok:
        print(name, "refine FAILED", r.status_code, r.text[:400])
        return
    open(os.path.join(d, "refined.png"), "wb").write(base64.b64decode(r.json()["data"][0]["b64_json"]))
    print(f"{name}: refined in {time.time() - t:.1f}s -> refined.png  usage={r.json().get('usage')}")


# ------------------------------------------------------------------ sound

def cmd_sound(name):
    d = run_dir(name)
    plan = json.load(open(os.path.join(d, "plan.json"), encoding="utf-8"))
    s = plan["sound"]
    if plan["status"] != "ok" or not s["wanted"]:
        print(name, "no sound planned")
        return
    t = time.time()
    r = requests.post("https://api.elevenlabs.io/v1/sound-generation?output_format=mp3_44100_128",
                      headers={"xi-api-key": CFG["sound"]["apiKey"]}, timeout=120,
                      json={"text": s["prompt"], "duration_seconds": s["duration_s"], "loop": s["kind"] == "loop",
                            "prompt_influence": 0.5, "model_id": "eleven_text_to_sound_v2"})
    if not r.ok:
        print(name, "sound FAILED", r.status_code, r.text[:300])
        return
    open(os.path.join(d, "sound.mp3"), "wb").write(r.content)
    print(f"{name}: sound {len(r.content) // 1024} KB in {time.time() - t:.1f}s ({s['kind']}, {s['duration_s']}s): {s['prompt']}")


# ------------------------------------------------------------------ tripo

def tripo_check(r, what):
    body = r.json() if r.headers.get("content-type", "").startswith("application/json") else {"raw": r.text[:300]}
    if r.status_code != 200 or body.get("code", 0) != 0:
        raise RuntimeError(f"{what}: HTTP {r.status_code} {json.dumps(body)[:500]}")
    return body["data"]


def tripo_wait(task_id, label, timeout=900):
    t0, last = time.time(), None
    while time.time() - t0 < timeout:
        data = tripo_check(requests.get(f"{TRIPO}/tasks/{task_id}", headers=tripo_headers(), timeout=60), "poll")
        state = (data.get("status"), data.get("progress"))
        if state != last:
            print(f"    {label}: {state[0]} {state[1]}%", flush=True)
            last = state
        if data.get("status") == "success":
            print(f"    {label}: done in {time.time() - t0:.0f}s, credits {data.get('credits_consumed')}")
            return data
        if data.get("status") in ("failed", "cancelled", "banned", "expired", "unknown"):
            raise RuntimeError(f"{label} ended {data.get('status')}: {json.dumps(data)[:500]}")
        time.sleep(4)
    raise TimeoutError(label)


def download(url, path):
    with requests.get(url, stream=True, timeout=300) as r:
        r.raise_for_status()
        with open(path, "wb") as f:
            for chunk in r.iter_content(1 << 16):
                f.write(chunk)
    return os.path.getsize(path)


def cmd_tripo(name, model=None, animate=True):
    d = run_dir(name)
    plan = json.load(open(os.path.join(d, "plan.json"), encoding="utf-8"))
    caps = json.load(open(os.path.join(PROMPTS, "object_capabilities.json"), encoding="utf-8"))["categories"]
    wants_anim = animate and plan["animate"]["wanted"]
    model = model or ("P1-20260311" if wants_anim else "v3.1-20260211")
    image = os.path.join(d, "refined.png") if os.path.exists(os.path.join(d, "refined.png")) else os.path.join(d, "ink.png")
    tag = model.split("-")[0]
    log = {"model": model, "image": os.path.basename(image)}

    with open(image, "rb") as f:
        token = tripo_check(requests.post(f"{TRIPO}/files", headers=tripo_headers(), timeout=120,
                                          files={"file": (os.path.basename(image), f, "image/png")}), "upload")["file_token"]
    t = time.time()
    gen = tripo_check(requests.post(f"{TRIPO}/generation/image-to-model", headers=tripo_headers(), timeout=60,
                                    json={"input": token, "model": model, "texture": True, "pbr": True}), "generate")["task_id"]
    print(f"  generate {model}: task {gen}")
    data = tripo_wait(gen, "generate")
    size = download(data["output"]["model_url"], os.path.join(d, f"model_{tag}.glb"))
    log.update(generate_task=gen, generate_s=round(time.time() - t), generate_credits=data.get("credits_consumed"), glb_kb=size // 1024)
    print(f"  model_{tag}.glb {size / 1e6:.1f} MB")

    if wants_anim:
        t = time.time()
        chk = tripo_check(requests.post(f"{TRIPO}/animations/rig-check", headers=tripo_headers(), timeout=60, json={"input": gen}), "rig-check")["task_id"]
        out = tripo_wait(chk, "rig-check")["output"]
        log["rig_check"] = out
        print(f"  rig-check: {out}  (GPT wanted {plan['animate']['rig_type']})")
        if out.get("riggable"):
            rig_type = out.get("rig_type") or plan["animate"]["rig_type"]
            rig_model = caps.get(rig_type, {}).get("rigModel", "v2.5-20260210")
            rig = tripo_check(requests.post(f"{TRIPO}/animations/rig", headers=tripo_headers(), timeout=60,
                                            json={"input": gen, "model": rig_model, "rig_type": rig_type, "spec": "tripo", "out_format": "glb"}), "rig")["task_id"]
            rdata = tripo_wait(rig, f"rig {rig_type} {rig_model}")
            log.update(rig_task=rig, rig_type=rig_type, rig_model=rig_model, rig_credits=rdata.get("credits_consumed"))
            presets = [a for a in plan["animate"]["animations"] if a in caps.get(rig_type, {}).get("presets", [])] or caps.get(rig_type, {}).get("presets", [])[:1]
            # One request per preset: a multi-preset request returns a GLB with only the last clip (but bills all).
            log.update(presets=presets, retarget=[])
            for i, preset in enumerate(presets):
                ret = tripo_check(requests.post(f"{TRIPO}/animations/retarget", headers=tripo_headers(), timeout=60,
                                                json={"input": rig, "animation": preset, "out_format": "glb",
                                                      "bake_animation": True, "animate_in_place": True}), "retarget")["task_id"]
                adata = tripo_wait(ret, f"retarget {preset}")
                url = adata.get("output", {}).get("model_url")
                log["retarget"].append({"task": ret, "preset": preset, "credits": adata.get("credits_consumed")})
                if url:
                    size = download(url, os.path.join(d, f"animated_{tag}_{i}.glb"))
                    print(f"  animated_{tag}_{i}.glb {size / 1e6:.1f} MB ({preset})")
            log["anim_s"] = round(time.time() - t)
    save_json(os.path.join(d, f"tripo_{tag}.json"), log)
    print("  log:", json.dumps(log))


def main(argv):
    if not argv:
        sys.exit(__doc__)
    cmd, args = argv[0], argv[1:]
    if cmd == "testset":
        cmd_testset()
    elif cmd == "plan":
        cmd_plan(args)
    elif cmd == "refine":
        for n in args:
            cmd_refine(n)
    elif cmd == "sound":
        for n in args:
            cmd_sound(n)
    elif cmd == "tripo":
        model = args[args.index("--model") + 1] if "--model" in args else None
        cmd_tripo(args[0], model=model, animate="--no-animate" not in args)
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main(sys.argv[1:])
