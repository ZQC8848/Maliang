"""Usage: python Tools/seals/make_logo_seals.py

Builds the two carved red seals (the design is cut out of a red block, 阴文) as four-cell seals:
three traditional characters + the partner logo, read the traditional way (right column top→bottom, then left):

    ┌──────┬──────┐
    │  印  │ 造/創 │
    ├──────┼──────┤
    │ logo │ 物/世 │
    └──────┴──────┘

Writes Assets/Maliang/Art/Seals/seal_wu.png (「造物印」 + Tripo, Object) and seal_jing.png (「創世印」 + World Labs, World).
Only the logo shapes are used; their original colours are discarded."""
import os
import random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageChops

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "TripoHack-Maliang", "My project", "Assets", "Maliang", "Art", "Seals")
FONT = "C:/Windows/Fonts/simkai.ttf"  # KaiTi: has the traditional glyphs (創)
S = 512
RED = (178, 34, 34)
MARGIN = 36        # block edge inside the texture
INNER_PAD = 30     # block edge to the character grid
CELL_PAD = 10      # breathing room inside each cell


# ------------------------------------------------------------------ logo masks

def logo_mask(path, is_foreground):
    """Soft 0..255 coverage of the logo shape, cropped to its bounds."""
    rgb = np.asarray(Image.open(path).convert("RGB")).astype(np.float32) / 255.0
    mask = Image.fromarray((np.clip(is_foreground(rgb), 0, 1) * 255).astype(np.uint8))
    # Bounds from the solid part of the mark (a low threshold would catch the background gradient), plus a small pad
    # so the anti-aliased rim is kept.
    x0, y0, x1, y1 = mask.point(lambda v: 255 if v > 127 else 0).getbbox()
    pad = max(2, round(max(mask.size) * 0.02))
    return mask.crop((max(0, x0 - pad), max(0, y0 - pad), min(mask.width, x1 + pad), min(mask.height, y1 + pad)))


def white_on_purple(rgb):
    # Purple background (red/green ~0.31), near-white mark (~0.97). The darker of red/green is a linear coverage
    # estimate, so anti-aliased edges and the thin pointed tips keep their true extent.
    rg = np.minimum(rgb[..., 0], rgb[..., 1])
    return np.clip((rg - 0.33) / (0.95 - 0.33), 0, 1)


def light_on_dark(rgb):
    # Dark navy gradient background; the mark is white + yellow (both bright).
    return np.clip((rgb.max(axis=2) - 0.45) / 0.2, 0, 1)


def fit_mask(mask, box):
    """Scale the soft coverage to fit a box, then cut at 50% so edges stay crisp."""
    w, h = mask.size
    k = min(box / w, box / h)
    big = mask.resize((max(1, round(w * k)), max(1, round(h * k))), Image.BICUBIC)
    big = big.filter(ImageFilter.GaussianBlur(k * 0.25))
    return big.point(lambda v: 255 if v > 127 else 0).filter(ImageFilter.GaussianBlur(1.0))


# ------------------------------------------------------------------ glyphs

def glyph(ch, box):
    """One character, thickened a little so the carved strokes read at stamp size, fitted to a box."""
    font = ImageFont.truetype(FONT, int(box * 1.15))
    img = Image.new("L", (box * 2, box * 2), 0)
    d = ImageDraw.Draw(img)
    d.text((box // 2, box // 2), ch, font=font, fill=255)
    img = img.filter(ImageFilter.MaxFilter(7))
    img = img.crop(img.getbbox())
    k = min(box / img.width, box / img.height)
    img = img.resize((max(1, round(img.width * k)), max(1, round(img.height * k))), Image.LANCZOS)
    return img.point(lambda v: 255 if v > 110 else 0).filter(ImageFilter.GaussianBlur(0.8))


def paste_center(canvas, img, cx, cy):
    canvas.paste(img, (int(cx - img.width / 2), int(cy - img.height / 2)), img)


# ------------------------------------------------------------------ seal

def seal(chars, logo, seed, path):
    rnd = random.Random(seed)
    block = Image.new("L", (S, S), 0)
    ImageDraw.Draw(block).rounded_rectangle([MARGIN, MARGIN, S - MARGIN, S - MARGIN], radius=26, fill=255)

    grid0 = MARGIN + INNER_PAD
    cell = (S - 2 * grid0) / 2
    box = int(cell - 2 * CELL_PAD)
    centers = {  # (column from left, row from top)
        "TL": (grid0 + cell * 0.5, grid0 + cell * 0.5), "TR": (grid0 + cell * 1.5, grid0 + cell * 0.5),
        "BL": (grid0 + cell * 0.5, grid0 + cell * 1.5), "BR": (grid0 + cell * 1.5, grid0 + cell * 1.5),
    }

    carve = Image.new("L", (S, S), 0)
    # Traditional order: right column top→bottom, then left column.
    paste_center(carve, glyph(chars[0], box), *centers["TR"])
    paste_center(carve, glyph(chars[1], box), *centers["BR"])
    paste_center(carve, glyph(chars[2], box), *centers["TL"])
    paste_center(carve, fit_mask(logo, int(box * 0.92)), *centers["BL"])
    alpha = ImageChops.subtract(block, carve)

    # Worn-stamp look: faint speckle and a rough border.
    noise = Image.new("L", (S, S), 255)
    nd = ImageDraw.Draw(noise)
    for _ in range(140):
        x, y = rnd.randrange(S), rnd.randrange(S)
        r = rnd.choice([1, 1, 1, 2, 2, 3])
        nd.ellipse([x - r, y - r, x + r, y + r], fill=rnd.randrange(60, 200))
    for _ in range(120):
        side = rnd.randrange(4)
        t = rnd.randrange(MARGIN, S - MARGIN)
        r = rnd.randrange(2, 9)
        x, y = [(t, MARGIN), (t, S - MARGIN), (MARGIN, t), (S - MARGIN, t)][side]
        nd.ellipse([x - r, y - r, x + r, y + r], fill=0)
    alpha = ImageChops.multiply(alpha, noise.filter(ImageFilter.GaussianBlur(1.0)))

    img = Image.new("RGBA", (S, S), RED + (0,))
    img.putalpha(alpha)
    img.save(path)
    print("wrote", os.path.normpath(path))


seal("造物印", logo_mask(os.path.join(HERE, "logo_object_tripo.png"), light_on_dark), 1, os.path.join(OUT, "seal_wu.png"))
seal("創世印", logo_mask(os.path.join(HERE, "logo_world_worldlabs.png"), white_on_purple), 2, os.path.join(OUT, "seal_jing.png"))
