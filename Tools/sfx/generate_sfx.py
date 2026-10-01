"""Generates the pre-made sound effects in sfx_list.json with ElevenLabs (eleven_text_to_sound_v2), then turns them
into game-ready WAVs with ffmpeg.

Usage:
  python Tools/sfx/generate_sfx.py            # every sound that does not exist yet
  python Tools/sfx/generate_sfx.py --all      # regenerate everything
  python Tools/sfx/generate_sfx.py burn_loop  # just these ids
  python Tools/sfx/generate_sfx.py --process  # only redo the WAVs from the downloaded MP3s (no API calls)

The key is read from TripoHack-Maliang/My project/maliang.config.json (sound.apiKey); it is never printed.
Downloads: Tools/sfx/raw/<id>.mp3 (not committed). Game files: Assets/Maliang/Audio/SFX/<id>.wav
Processing: decoded to WAV (drops the MP3 encoder delay, so loops wrap without a gap); one-shots lose their leading
silence (a stamp sounds the moment it lands) and get a short fade-out; "gain_db" raises quiet sounds behind a limiter.
"""
import json
import os
import subprocess
import sys
import time

import requests

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(HERE, "..", "..", "TripoHack-Maliang", "My project"))
RAW = os.path.join(HERE, "raw")
OUT = os.path.join(PROJECT, "Assets", "Maliang", "Audio", "SFX")
URL = "https://api.elevenlabs.io/v1/sound-generation?output_format=mp3_44100_128"


def generate(s, key):
    body = {
        "text": s["prompt"],
        "duration_seconds": s["seconds"],
        "prompt_influence": s.get("influence", 0.5),
        "loop": bool(s.get("loop", False)),
        "model_id": "eleven_text_to_sound_v2",
    }
    t = time.time()
    r = requests.post(URL, headers={"xi-api-key": key}, json=body, timeout=120)
    if not r.ok:
        print(f"{s['id']}: HTTP {r.status_code} {r.text[:300]}")
        return False
    open(os.path.join(RAW, s["id"] + ".mp3"), "wb").write(r.content)
    print(f"{s['id']}: {len(r.content) // 1024} KB in {time.time() - t:.1f}s{' (loop)' if s.get('loop') else ''}")
    return True


def process(s):
    src = os.path.join(RAW, s["id"] + ".mp3")
    if not os.path.exists(src):
        return
    filters = []
    if not s.get("loop"):
        filters.append("silenceremove=start_periods=1:start_threshold=-50dB")
    if s.get("gain_db"):
        filters.append(f"volume={s['gain_db']}dB")
        filters.append("alimiter=limit=0.89:level=false:latency=true")
    if not s.get("loop"):
        filters.append("areverse,afade=t=in:d=0.02,areverse")  # 20 ms fade-out at the end
    cmd = ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", src]
    if filters:
        cmd += ["-af", ",".join(filters)]
    cmd += ["-ar", "44100", os.path.join(OUT, s["id"] + ".wav")]
    subprocess.run(cmd, check=True)


def main(args):
    sounds = json.load(open(os.path.join(HERE, "sfx_list.json"), encoding="utf-8"))["sounds"]
    os.makedirs(RAW, exist_ok=True)
    os.makedirs(OUT, exist_ok=True)
    only = [a for a in args if not a.startswith("--")]
    if "--process" not in args:
        key = json.load(open(os.path.join(PROJECT, "maliang.config.json"), encoding="utf-8"))["sound"]["apiKey"]
        for s in sounds:
            if only and s["id"] not in only:
                continue
            if not only and "--all" not in args and os.path.exists(os.path.join(RAW, s["id"] + ".mp3")):
                continue
            generate(s, key)
    for s in sounds:
        if not only or s["id"] in only:
            process(s)
    print("WAVs in", OUT)


if __name__ == "__main__":
    main(sys.argv[1:])
