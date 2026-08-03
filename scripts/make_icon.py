"""Generate a minimalist app icon for PortPingTool using the Phosphor
'terminal-window' icon as the base — a rounded rectangle with a '>'
prompt and a cursor line. Reads unmistakably as a terminal / command
line, which is exactly what a network operator's tool is.

We embed the icon on a white rounded-square background, recolored
to Apple system blue.
"""
from PIL import Image, ImageDraw
import os
import urllib.request

OUT_DIR = "/workspace/port-ping-tool/Assets"
os.makedirs(OUT_DIR, exist_ok=True)

# Apple system blue
ACCENT = (0x00, 0x7A, 0xFF, 255)
BG = (0xFF, 0xFF, 0xFF, 255)
TRANSPARENT = (0, 0, 0, 0)

SIZE = 256
CORNER_RADIUS = 56
SVG_URL = "https://raw.githubusercontent.com/phosphor-icons/core/main/raw/regular/terminal-window.svg"
SVG_PATH = os.path.join(OUT_DIR, "terminal.svg")


def fetch_svg():
    if not os.path.exists(SVG_PATH):
        print(f"Fetching {SVG_URL}...")
        urllib.request.urlretrieve(SVG_URL, SVG_PATH)
    return SVG_PATH


def rounded_rect_mask(size, radius):
    mask = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(mask)
    d.rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=255)
    return mask


def make_icon(size=256):
    """Render the terminal-window icon as Apple-blue on white rounded bg."""
    # Load SVG via cairosvg if available, else fall back to manual draw
    try:
        import cairosvg
        svg_bytes = open(SVG_PATH, "rb").read()
        png_bytes = cairosvg.svg2png(
            bytestring=svg_bytes,
            output_width=size,
            output_height=size,
        )
        from io import BytesIO
        icon = Image.open(BytesIO(png_bytes)).convert("RGBA")
    except ImportError:
        # Fallback: draw the icon manually using the same geometry as the SVG
        # Phosphor terminal-window: rounded rect + > prompt + cursor line
        icon = Image.new("RGBA", (size, size), TRANSPARENT)
        d = ImageDraw.Draw(icon)
        # Rounded outer rect (the terminal window)
        rect_pad = int(size * 0.125)
        d.rounded_rectangle(
            (rect_pad, rect_pad, size - rect_pad, size - rect_pad),
            radius=int(size * 0.06),
            outline=ACCENT, width=int(size * 0.0625),
        )
        # > prompt (polyline going down-right then down-left)
        prompt_stroke = int(size * 0.0625)
        # Approximate the > shape: 3 points
        cx, cy = size * 0.31, size * 0.5
        arm = size * 0.07
        d.line([(cx - arm, cy - arm), (cx + arm, cy), (cx - arm, cy + arm)],
               fill=ACCENT, width=prompt_stroke)
        # Cursor line _
        d.line([(cx + arm * 1.8, cy + arm * 1.2), (cx + arm * 3.5, cy + arm * 1.2)],
               fill=ACCENT, width=prompt_stroke)

    # Recolor black/gray pixels to Apple blue (svg2png returns black on transparent)
    pixels = icon.load()
    for y in range(icon.size[1]):
        for x in range(icon.size[0]):
            r, g, b, a = pixels[x, y]
            if a > 0 and r < 200 and g < 200 and b < 200:
                pixels[x, y] = (ACCENT[0], ACCENT[1], ACCENT[2], a)

    # Composite onto white rounded background
    bg = Image.new("RGBA", (size, size), TRANSPARENT)
    bg_draw = ImageDraw.Draw(bg)
    bg_draw.rounded_rectangle((0, 0, size - 1, size - 1),
                              radius=int(size * CORNER_RADIUS / SIZE),
                              fill=BG)
    out = Image.alpha_composite(bg, icon)

    # Apply rounded mask
    mask = rounded_rect_mask(size, int(size * CORNER_RADIUS / SIZE))
    final = Image.new("RGBA", (size, size), TRANSPARENT)
    final.paste(out, (0, 0), mask)
    return final


def main():
    fetch_svg()
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


if __name__ == "__main__":
    main()
