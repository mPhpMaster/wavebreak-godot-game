"""Render icon.svg's shapes (rounded square, two rings, a dot) to a
multi-size wavebreak.ico for the installer and shortcuts."""
import pathlib
from PIL import Image, ImageDraw

S = 1024                       # draw large, downsample for clean edges
k = S / 128
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle([0, 0, S - 1, S - 1], radius=20 * k, fill="#1b1d26")


def ring(r, width, colour):
    c = 64 * k
    d.ellipse([c - r * k, c - r * k, c + r * k, c + r * k], outline=colour, width=round(width * k))


ring(40 + 3, 6, "#7b5cff")     # svg strokes are centred on r; PIL draws inward
ring(22 + 2.5, 5, "#3fe0c8")
c = 64 * k
d.ellipse([c - 7 * k, c - 7 * k, c + 7 * k, c + 7 * k], fill="#ffd166")

out = pathlib.Path(__file__).with_name("wavebreak.ico")
img.resize((256, 256), Image.LANCZOS).save(out, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print(out)
