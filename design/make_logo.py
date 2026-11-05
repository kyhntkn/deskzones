"""DeskZones logosu: SVG + PNG + ICO + MSIX görselleri üretir."""
import numpy as np
from PIL import Image, ImageDraw

PANELS = [  # x, y, w, h, beyaz opaklık, ikon sütun, ikon satır
    (88, 88, 156, 336, 0.94, 3, 4),
    (268, 88, 156, 156, 0.60, 2, 2),
    (268, 268, 156, 156, 0.36, 2, 2),
]
ICON_COLORS = ["#F5B83D", "#7DD3C0", "#12495C", "#F5B83D", "#7DD3C0", "#12495C"]
TOP, BOTTOM = (15, 46, 61), (10, 31, 43)
BAR = "#12495C"

def hex2rgb(h):
    h = h.lstrip("#"); return tuple(int(h[i:i+2], 16) for i in (0, 2, 4))

def icon_cells(px, py, cols, rows):
    return [(px + 20 + c * 44, py + 52 + r * 52, 32, 32) for r in range(rows) for c in range(cols)]

def cell_style(a, i):
    if a > 0.9:
        c = ICON_COLORS[i % len(ICON_COLORS)]
        return (c, 0.30) if c == "#12495C" else (c, 1.0)
    return "#0F2E3D", 0.55

def svg():
    s = ['<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">',
         '<defs><linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">'
         f'<stop offset="0" stop-color="rgb{TOP}"/><stop offset="1" stop-color="rgb{BOTTOM}"/></linearGradient></defs>',
         '<rect width="512" height="512" rx="112" fill="url(#bg)"/>']
    for (x, y, w, h, a, cols, rows) in PANELS:
        s.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="24" fill="#fff" fill-opacity="{a}"/>')
        s.append(f'<rect x="{x+20}" y="{y+18}" width="56" height="10" rx="5" fill="{BAR}" fill-opacity="0.55"/>')
        for i, (cx, cy, cw, ch) in enumerate(icon_cells(x, y, cols, rows)):
            color, op = cell_style(a, i)
            s.append(f'<rect x="{cx}" y="{cy}" width="{cw}" height="{ch}" rx="9" fill="{color}" fill-opacity="{op}"/>')
    s.append('</svg>')
    return "\n".join(s)

def render(size, ss=4):
    S = size * ss; k = S / 512
    t = np.linspace(0, 1, S)[:, None, None]
    grad = (np.array(TOP) * (1 - t) + np.array(BOTTOM) * t) * np.ones((1, S, 1))
    base = Image.fromarray(grad.astype("uint8"), "RGB").convert("RGBA")
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=int(112 * k), fill=255)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0)); img.paste(base, (0, 0), mask)

    def layer(box, r, fill):
        l = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        ImageDraw.Draw(l).rounded_rectangle([v * k for v in box], radius=r * k, fill=fill)
        return l
    for (x, y, w, h, a, cols, rows) in PANELS:
        img = Image.alpha_composite(img, layer((x, y, x + w, y + h), 24, (255, 255, 255, int(255 * a))))
        img = Image.alpha_composite(img, layer((x + 20, y + 18, x + 76, y + 28), 5, (*hex2rgb(BAR), int(255 * .55))))
        for i, (cx, cy, cw, ch) in enumerate(icon_cells(x, y, cols, rows)):
            color, op = cell_style(a, i)
            img = Image.alpha_composite(img, layer((cx, cy, cx + cw, cy + ch), 9, (*hex2rgb(color), int(255 * op))))
    return img.resize((size, size), Image.LANCZOS)

open("design/logo.svg", "w").write(svg())
open("src/DeskZones/Assets/logo.svg", "w").write(svg())
render(512).save("design/logo.png")
render(256).save("src/DeskZones/Assets/icon.ico", sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
for name, s in (("Square44x44Logo", 44), ("Square150x150Logo", 150), ("StoreLogo", 50)):
    render(s).save(f"packaging/Assets/{name}.png")
print("ok")
