"""Downscale the textures embedded in a GLB (Tripo exports ship 4096² maps) and rewrite the file in place.

Usage:
    python Tools/tripo/compress_glb_textures.py --max 1024 <file.glb> [<file.glb> ...]

- Every embedded image is resized so its longer side is at most --max (Lanczos). Images already small enough are kept.
- JPEG images are re-encoded as JPEG (quality --jpeg-quality); PNG images stay PNG (normal maps stay lossless).
- The original is copied to --backup-dir first (default: TestData/TripoOriginals, git-ignored) unless a backup exists.
Geometry, materials and all other buffer data are left untouched.
"""
import argparse
import io
import json
import os
import shutil
import struct

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_BACKUP = os.path.join(HERE, "..", "..", "TripoHack-Maliang", "My project", "TestData", "TripoOriginals")
GLB_MAGIC, CHUNK_JSON, CHUNK_BIN = 0x46546C67, 0x4E4F534A, 0x004E4942


def read_glb(path):
    data = open(path, "rb").read()
    magic, version, _ = struct.unpack_from("<III", data, 0)
    if magic != GLB_MAGIC or version != 2:
        raise ValueError(f"{path}: not a glTF 2.0 binary")
    offset, gltf, blob = 12, None, b""
    while offset < len(data):
        length, ctype = struct.unpack_from("<II", data, offset)
        chunk = data[offset + 8: offset + 8 + length]
        if ctype == CHUNK_JSON:
            gltf = json.loads(chunk)
        elif ctype == CHUNK_BIN:
            blob = chunk
        offset += 8 + length
    return gltf, blob


def write_glb(path, gltf, blob):
    js = json.dumps(gltf, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    js += b" " * (-len(js) % 4)
    blob += b"\0" * (-len(blob) % 4)
    total = 12 + 8 + len(js) + 8 + len(blob)
    with open(path, "wb") as f:
        f.write(struct.pack("<III", GLB_MAGIC, 2, total))
        f.write(struct.pack("<II", len(js), CHUNK_JSON) + js)
        f.write(struct.pack("<II", len(blob), CHUNK_BIN) + blob)


def shrink(raw, mime, max_side, jpeg_quality):
    img = Image.open(io.BytesIO(raw))
    if max(img.size) <= max_side:
        return raw, img.size, img.size
    k = max_side / max(img.size)
    new_size = (max(1, round(img.width * k)), max(1, round(img.height * k)))
    small = img.resize(new_size, Image.LANCZOS)
    buf = io.BytesIO()
    if mime == "image/jpeg":
        small.convert("RGB").save(buf, format="JPEG", quality=jpeg_quality, optimize=True, subsampling=0)
    else:
        small.save(buf, format="PNG", optimize=True)
    return buf.getvalue(), img.size, new_size


def compress(path, max_side, jpeg_quality, backup_dir):
    os.makedirs(backup_dir, exist_ok=True)
    backup = os.path.join(backup_dir, os.path.basename(path))
    if not os.path.exists(backup):
        shutil.copy2(path, backup)

    gltf, blob = read_glb(path)
    before = os.path.getsize(path)
    image_views = {img["bufferView"]: img.get("mimeType", "image/png") for img in gltf.get("images", []) if "bufferView" in img}

    # Rebuild the binary chunk view by view, swapping in the shrunk images and re-packing offsets (4-byte aligned).
    new_blob = bytearray()
    for i, bv in enumerate(gltf["bufferViews"]):
        start = bv.get("byteOffset", 0)
        raw = blob[start: start + bv["byteLength"]]
        if i in image_views:
            raw, old, new = shrink(raw, image_views[i], max_side, jpeg_quality)
            print(f"  image view {i}: {image_views[i]} {old[0]}x{old[1]} -> {new[0]}x{new[1]} ({len(raw) // 1024} KB)")
        new_blob += b"\0" * (-len(new_blob) % 4)
        bv["byteOffset"] = len(new_blob)
        bv["byteLength"] = len(raw)
        new_blob += raw
    gltf["buffers"][0]["byteLength"] = len(new_blob)

    write_glb(path, gltf, bytes(new_blob))
    print(f"{os.path.basename(path)}: {before / 1e6:.1f} MB -> {os.path.getsize(path) / 1e6:.1f} MB (original kept at {os.path.normpath(backup)})")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--max", type=int, required=True, help="longest texture side after compression, e.g. 1024")
    ap.add_argument("--jpeg-quality", type=int, default=90)
    ap.add_argument("--backup-dir", default=DEFAULT_BACKUP)
    ap.add_argument("files", nargs="+")
    args = ap.parse_args()
    for f in args.files:
        compress(f, args.max, args.jpeg_quality, args.backup_dir)


if __name__ == "__main__":
    main()
