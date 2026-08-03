"""Generate a minimalist app icon for PortPingTool.

Design v3: A square outline (port) with concentric signal waves
emanating from one corner (ping). Reads as 'port + ping' without
copying any existing app icon.

Square 256x256, white rounded background, Apple-blue accents.
"""
from PIL import Image, ImageDraw
import os

OUT_DIR = "/workspace/port-ping-tool/Assets"
os.makedirs(OUT_DIR, exist_ok=True)

ACCENT = (0x00, 0x7A, 0xFF, 255)
ACCENT_LIGHT = (0x4D, 0xA0, 0xFF, 255)
BG = (0xFF, 0xFF, 0xFF, 255)
TRANSPARENT = (0, 0, 0, 0)

SIZE = 256
CORNER_RADIUS = 56


def rounded_rect_mask(size, radius):
    mask = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(mask)
    d.rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=255)
    return mask


def draw_icon(img, size):
    d = ImageDraw.Draw(img)
    pad = int(size * 0.18)
    # Square outline (port)
    sq_size = size - 2 * pad
    stroke = max(2, int(size * 0.05))
    d.rounded_rectangle(
        (pad, pad, pad + sq_size, pad + sq_size),
        radius=int(size * 0.08),
        outline=ACCENT,
        width=stroke,
    )
    # Concentric signal waves in the top-right corner
    origin = (pad + sq_size, pad)
    radii = [int(size * 0.10), int(size * 0.18), int(size * 0.26)]
    wave_stroke = max(2, int(size * 0.035))
    for r in radii:
        d.arc(
            (origin[0] - r, origin[1] - r, origin[0] + r, origin[1] + r),
            start=180, end=270, fill=ACCENT_LIGHT, width=wave_stroke,
        )
    # Solid dot at the origin (the 'ping' source)
    dot_r = int(size * 0.05)
    d.ellipse(
        (origin[0] - dot_r, origin[1] - dot_r, origin[0] + dot_r, origin[1] + dot_r),
        fill=ACCENT,
    )


def make_icon(size=256):
    bg = Image.new("RGBA", (size, size), TRANSPARENT)
    bg_draw = ImageDraw.Draw(bg)
    bg_draw.rounded_rectangle((0, 0, size - 1, size - 1),
                              radius=int(size * CORNER_RADIUS / SIZE),
                              fill=BG)
    fg = Image.new("RGBA", (size, size), TRANSPARENT)
    draw_icon(fg, size)
    out = Image.alpha_composite(bg, fg)
    mask = rounded_rect_mask(size, int(size * CORNER_RADIUS / SIZE))
    final = Image.new("RGBA", (size, size), TRANSPARENT)
    final.paste(out, (0, 0), mask)
    return final


master = make_icon(SIZE)
master.save(os.path.join(OUT_DIR, "icon.png"), "PNG")

sizes = [16, 32, 48, 64, 128, 256]
imgs = [make_icon(s) for s in sizes]
master.save(
    os.path.join(OUT_DIR, "icon.ico"),
    format="ICO",
    sizes=[(s, s) for s in sizes],
    append_images=imgs[:-1],
)

make_icon(512).save(os.path.join(OUT_DIR, "icon@2x.png"), "PNG")

print("Generated:")
for f in sorted(os.listdir(OUT_DIR)):
    p = os.path.join(OUT_DIR, f)
    print(f"  {f}  ({os.path.getsize(p)} bytes)")
