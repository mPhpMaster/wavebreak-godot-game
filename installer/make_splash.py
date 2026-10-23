"""Boot splash for the game window (replaces Godot's logo): the icon, the
name and the tagline on the menu's night colour. Writes assets/splash.png."""
import pathlib
from PIL import Image, ImageDraw, ImageFont

ROOT = pathlib.Path(__file__).resolve().parent.parent
BG = (20, 22, 31, 255)
W, H = 1200, 520

img = Image.new("RGBA", (W, H), BG)
icon = Image.open(ROOT / "installer" / "wavebreak.ico").convert("RGBA").resize((220, 220), Image.LANCZOS)
img.alpha_composite(icon, ((W - 220) // 2, 30))

d = ImageDraw.Draw(img)
bold = ImageFont.truetype(r"C:\Windows\Fonts\segoeuib.ttf", 120)
semi = ImageFont.truetype(r"C:\Windows\Fonts\seguisb.ttf", 40)


def centred(text, font, y, fill, stroke=0, stroke_fill=None):
    w = d.textlength(text, font=font)
    d.text(((W - w) / 2, y), text, font=font, fill=fill, stroke_width=stroke, stroke_fill=stroke_fill)


centred("WAVEBREAK", bold, 255, (238, 242, 255), stroke=4, stroke_fill=(10, 10, 18))
centred("hold the line until the tide turns", semi, 420, (110, 210, 255))

out = ROOT / "assets" / "splash.png"
img.save(out)
print(out)
