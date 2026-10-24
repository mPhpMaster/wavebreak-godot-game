"""Builds the white-glyph icon set in assets/icons/.

    python tools/make_icons.py <kenney-download-root> [<out-dir>]
    python tools/make_icons.py --procedural [<name> ...]   (original glyphs only, no download)

Every icon is a white silhouette with a dark outline on a transparent 128x128
canvas. The game tints the white at draw time (Control modulate multiplies, so
the outline stays dark), which is how one glyph serves several colours: a sword
is orange for Damage and the weapon's element colour on a melee weapon card.

Two sources:

  * Kenney "Board Game Icons" and "Game Icons" (CC0). Only the PNGs listed in
    KENNEY below are read; nothing else in the download is touched.
  * Original glyphs drawn here with Pillow (the PROCEDURAL table). These are
    part of this project and CC0 like the music.

Glyphs are normalised to the same optical size so a card's icon does not jump
in scale between a Kenney shape and a drawn one.
"""

import math
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

S = 512           # working size (4x supersampled)
OUT = 128         # shipped size
FIT = 404         # glyph box inside the working canvas
OUTLINE = 15      # outline thickness at working size (about 3.75 px shipped)

BOARD = "kenney-board-game-icons/PNG/Double (128px)"
GAME = "kenney-game-icons/PNG/White/2x"

# name -> (pack folder, file)
KENNEY = {
    "sword": (BOARD, "sword.png"),
    "shield": (BOARD, "shield.png"),
    "heart": (BOARD, "suit_hearts.png"),
    "heart_broken": (BOARD, "suit_hearts_broken.png"),
    "clover": (BOARD, "suit_clubs.png"),
    "flame": (BOARD, "fire.png"),
    "skull": (BOARD, "skull.png"),
    "bow": (BOARD, "bow.png"),
    "crown": (BOARD, "crown_a.png"),
    "potion": (BOARD, "flask_full.png"),
    "hourglass": (BOARD, "hourglass.png"),
    "pouch": (BOARD, "pouch.png"),
    "tower": (BOARD, "structure_tower.png"),
    "burst": (BOARD, "exploding.png"),
    "campfire": (BOARD, "campfire.png"),
    "award": (BOARD, "award.png"),
    "dice": (BOARD, "dice_detailed.png"),
    "lock": (BOARD, "lock_closed.png"),
    "target": (GAME, "target.png"),
    "star": (GAME, "star.png"),
    "fastforward": (GAME, "fastForward.png"),
    "gear": (GAME, "gear.png"),
    "expand": (GAME, "larger.png"),
    "question": (GAME, "question.png"),
    "wrench": (GAME, "wrench.png"),
    "up": (GAME, "up.png"),
    "next": (GAME, "next.png"),
}


# ------------------------------------------------------------------ helpers

def canvas():
    return Image.new("L", (S, S), 0)


def rot(points, deg, cx=S / 2, cy=S / 2):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    return [(cx + (x - cx) * c - (y - cy) * s, cy + (x - cx) * s + (y - cy) * c) for x, y in points]


def ellipse_poly(cx, cy, rx, ry, deg=0, n=64):
    pts = [(cx + rx * math.cos(2 * math.pi * i / n), cy + ry * math.sin(2 * math.pi * i / n)) for i in range(n)]
    return rot(pts, deg, cx, cy)


def circle(d, cx, cy, r, fill=255):
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=fill)


def thick_line(d, pts, w, fill=255):
    d.line(pts, fill=fill, width=int(w), joint="curve")
    for x, y in (pts[0], pts[-1]):
        circle(d, x, y, w / 2, fill)


def rotate_img(im, deg):
    return im.rotate(-deg, resample=Image.BICUBIC, center=(S / 2, S / 2))


def arrow_head(d, tip, angle_deg, length, width, fill=255):
    a = math.radians(angle_deg)
    bx, by = tip[0] - length * math.cos(a), tip[1] - length * math.sin(a)
    px, py = -math.sin(a) * width / 2, math.cos(a) * width / 2
    d.polygon([tip, (bx + px, by + py), (bx - px, by - py)], fill=fill)


# ------------------------------------------------------------- procedural

def g_boot():
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle((170, 70, 310, 320), 26, fill=255)
    d.polygon([(170, 250), (310, 250), (330, 290), (420, 312), (448, 350), (448, 420), (150, 420), (150, 300)], fill=255)
    d.rectangle((150, 140, 330, 156), fill=0)        # cuff
    d.rectangle((140, 384, 460, 398), fill=0)        # sole
    return im


def g_dash():
    im = g_boot().resize((380, 380), Image.LANCZOS)
    out = canvas(); out.paste(im, (140, 70))
    d = ImageDraw.Draw(out)
    for y, x0 in ((170, 40), (250, 10), (330, 40)):
        thick_line(d, [(x0, y), (x0 + 120, y)], 30)
    return out


def g_magnet():
    im = canvas(); d = ImageDraw.Draw(im)
    cx, cy = 256, 280
    d.pieslice((cx - 170, cy - 170, cx + 170, cy + 170), 0, 180, fill=255)
    d.pieslice((cx - 78, cy - 78, cx + 78, cy + 78), 0, 180, fill=0)
    d.rectangle((cx - 170, 70, cx - 78, cy), fill=255)
    d.rectangle((cx + 78, 70, cx + 170, cy), fill=255)
    d.rectangle((cx - 180, 140, cx - 70, 158), fill=0)
    d.rectangle((cx + 70, 140, cx + 180, 158), fill=0)
    return im


def g_snowflake():
    im = canvas(); d = ImageDraw.Draw(im)
    for k in range(6):
        a = math.radians(90 + 60 * k)
        ex, ey = 256 + 210 * math.cos(a), 256 + 210 * math.sin(a)
        thick_line(d, [(256, 256), (ex, ey)], 34)
        for t, ln in ((0.55, 78), (0.82, 56)):
            mx, my = 256 + 210 * t * math.cos(a), 256 + 210 * t * math.sin(a)
            for side in (-1, 1):
                b = a + side * math.radians(45)
                thick_line(d, [(mx, my), (mx + ln * math.cos(b), my + ln * math.sin(b))], 26)
    circle(d, 256, 256, 44)
    return im


def g_bolt():
    im = canvas(); d = ImageDraw.Draw(im)
    d.polygon([(318, 34), (126, 290), (242, 290), (186, 482), (392, 214), (276, 214), (352, 34)], fill=255)
    return im


def g_drop():
    im = canvas(); d = ImageDraw.Draw(im)
    circle(d, 256, 316, 140)
    d.polygon([(256, 40), (130, 250), (382, 250)], fill=255)
    d.arc((170, 240, 330, 400), 110, 170, fill=0, width=18)   # highlight
    return im


def g_feather_shape(cx, cy, length, width, deg):
    return ellipse_poly(cx, cy, length / 2, width / 2, deg)


def g_spring():
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle((110, 420, 402, 466), 16, fill=255)
    pts = [(256, 420)]
    for k in range(5):
        y = 400 - k * 50
        pts += [(150 if k % 2 == 0 else 362, y), (362 if k % 2 == 0 else 150, y - 25)]
    pts.append((256, 160))
    thick_line(d, pts, 30)
    d.polygon([(256, 30), (140, 160), (372, 160)], fill=255)
    return im


def g_feather():
    im = canvas(); d = ImageDraw.Draw(im)
    d.polygon(g_feather_shape(270, 230, 400, 150, -50), fill=255)
    a = math.radians(-50)
    p0 = (270 - 250 * math.cos(a), 230 - 250 * math.sin(a))
    p1 = (270 + 150 * math.cos(a), 230 + 150 * math.sin(a))
    thick_line(d, [p0, p1], 14, 0)
    thick_line(d, [p0, (p0[0] + 60 * math.cos(a), p0[1] + 60 * math.sin(a))], 22, 255)
    for t in (-80, -10, 60):
        mx, my = 270 + t * math.cos(a), 230 + t * math.sin(a)
        d.line([(mx, my), (mx + 70, my + 30)], fill=0, width=10)
    return im


def g_coin():
    im = canvas(); d = ImageDraw.Draw(im)
    circle(d, 256, 256, 200)
    d.ellipse((256 - 150, 256 - 150, 256 + 150, 256 + 150), outline=0, width=18)
    d.rounded_rectangle((232, 160, 280, 352), 20, fill=0)
    return im


def _dart(d, length=360, shaft=40, head=130):
    x0 = 256 - length / 2
    x1 = 256 + length / 2
    d.rectangle((x0, 256 - shaft / 2, x1 - head + 20, 256 + shaft / 2), fill=255)
    d.polygon([(x1, 256), (x1 - head, 256 - head * 0.62), (x1 - head, 256 + head * 0.62)], fill=255)
    d.polygon([(x0, 256 - shaft / 2), (x0 + 70, 256 - shaft / 2), (x0 + 30, 256 - 90), (x0 - 40, 256 - 90)], fill=255)
    d.polygon([(x0, 256 + shaft / 2), (x0 + 70, 256 + shaft / 2), (x0 + 30, 256 + 90), (x0 - 40, 256 + 90)], fill=255)


def g_dart():
    im = canvas(); d = ImageDraw.Draw(im)
    _dart(d, 340)
    d2 = ImageDraw.Draw(im)
    return rotate_img(im, -38)


def g_multi():
    im = canvas(); d = ImageDraw.Draw(im)
    pivot = (256, 470)
    shaft = [(240, 160), (272, 160), (272, 440), (240, 440)]
    head = [(256, 50), (196, 170), (316, 170)]
    for deg in (-30, 0, 30):
        d.polygon(rot(shaft, deg, *pivot), fill=255)
        d.polygon(rot(head, deg, *pivot), fill=255)
    return im


def g_homing():
    im = canvas(); d = ImageDraw.Draw(im)
    # trail curving up and to the right
    cx, cy, r = 330, 430, 270
    d.arc((cx - r, cy - r, cx + r, cy + r), 190, 268, fill=255, width=46)
    a = math.radians(268)
    tip = (cx + r * math.cos(a) + 70, cy + r * math.sin(a))
    end = (cx + r * math.cos(a), cy + r * math.sin(a))
    d.polygon([(tip[0] + 30, tip[1]), (end[0] - 20, end[1] - 75), (end[0] - 20, end[1] + 75)], fill=255)
    for k, (px, py) in enumerate(((84, 400), (120, 470))):
        circle(d, px, py, 26 - k * 6)
    # target ring the arrow is chasing
    d.ellipse((370, 300, 470, 400), outline=255, width=26)
    return im


def g_aura():
    im = canvas(); d = ImageDraw.Draw(im)
    circle(d, 256, 256, 64)
    d.ellipse((256 - 148, 256 - 148, 256 + 148, 256 + 148), outline=255, width=34)
    for k in range(8):
        a0 = k * 45 + 8
        d.arc((256 - 228, 256 - 228, 256 + 228, 256 + 228), a0, a0 + 28, fill=255, width=30)
    return im


def g_bomb():
    im = canvas(); d = ImageDraw.Draw(im)
    circle(d, 236, 300, 168)
    d.polygon(rot([(300, 90), (390, 90), (390, 170), (300, 170)], 40, 345, 130), fill=255)
    d.arc((340, 20, 470, 150), 180, 300, fill=255, width=22)
    d.arc((130, 200, 290, 360), 190, 260, fill=0, width=20)
    for k in range(6):
        a = math.radians(k * 60 + 15)
        thick_line(d, [(445 + 20 * math.cos(a), 52 + 20 * math.sin(a)), (445 + 52 * math.cos(a), 52 + 52 * math.sin(a))], 16)
    return im


def g_orbit():
    im = canvas(); d = ImageDraw.Draw(im)
    circle(d, 256, 256, 82)
    d.ellipse((50, 50, 462, 462), outline=255, width=20)
    for deg in (30, 150, 270):
        a = math.radians(deg)
        x, y = 256 + 206 * math.cos(a), 256 + 206 * math.sin(a)
        circle(d, x, y, 66, 0)
        circle(d, x, y, 50, 255)
    return im


def g_chakram():
    im = canvas(); d = ImageDraw.Draw(im)
    circle(d, 256, 256, 176)
    for k in range(4):
        a = math.radians(k * 90)
        b = a + math.radians(70)
        d.polygon([(256 + 150 * math.cos(a), 256 + 150 * math.sin(a)),
                   (256 + 236 * math.cos(b), 256 + 236 * math.sin(b)),
                   (256 + 150 * math.cos(a + math.radians(80)), 256 + 150 * math.sin(a + math.radians(80)))], fill=255)
    circle(d, 256, 256, 92, 0)
    return im


def g_beam():
    im = canvas(); d = ImageDraw.Draw(im)
    circle(d, 120, 256, 86)
    d.polygon([(150, 200), (470, 90), (470, 422), (150, 312)], fill=255)
    d.polygon([(220, 236), (470, 196), (470, 316), (220, 276)], fill=0)
    return im


def g_hammer():
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle((110, 100, 402, 236), 22, fill=255)
    d.rounded_rectangle((226, 230, 286, 470), 18, fill=255)
    d.rectangle((110, 160, 402, 172), fill=0)
    return rotate_img(im, -32)


def g_chain():
    im = canvas(); d = ImageDraw.Draw(im)
    for k, (cx, cy) in enumerate(((150, 362), (256, 256), (362, 150))):
        deg = -45
        outer = ellipse_poly(cx, cy, 108, 66, deg)
        inner = ellipse_poly(cx, cy, 62, 24, deg)
        d.polygon(outer, fill=255)
        d.polygon(inner, fill=0)
    # separate the links so they read as interlocked
    for cx, cy in ((203, 309), (309, 203)):
        thick_line(d, [(cx - 30, cy - 30), (cx + 30, cy + 30)], 14, 0)
    return im


def g_wave():
    im = canvas(); d = ImageDraw.Draw(im)
    for k, y0 in enumerate((150, 256, 362)):
        pts = [(x, y0 + 40 * math.sin((x - 60) / 392 * 2 * math.pi * 1.5 + k * 0.6)) for x in range(60, 456, 6)]
        thick_line(d, pts, 40)
    return im


def g_bell():
    im = canvas(); d = ImageDraw.Draw(im)
    d.pieslice((140, 70, 372, 300), 180, 360, fill=255)
    d.polygon([(140, 185), (372, 185), (420, 390), (92, 390)], fill=255)
    d.rounded_rectangle((70, 370, 442, 410), 18, fill=255)
    circle(d, 256, 450, 40)
    d.rounded_rectangle((232, 36, 280, 80), 10, fill=255)
    return im


def g_vortex():
    im = canvas(); d = ImageDraw.Draw(im)
    pts = []
    for i in range(0, 520):
        t = i / 520 * 2.6 * 2 * math.pi
        r = 18 + 205 * (i / 520)
        pts.append((256 + r * math.cos(t), 256 + r * math.sin(t)))
    thick_line(d, pts, 36)
    return im


def _cloud(d, dy=0):
    circle(d, 170, 250 + dy, 92)
    circle(d, 270, 200 + dy, 118)
    circle(d, 360, 262 + dy, 84)
    d.rounded_rectangle((90, 250 + dy, 430, 342 + dy), 46, fill=255)


def g_cloud():
    im = canvas(); d = ImageDraw.Draw(im)
    _cloud(d, 20)
    return im


def g_storm():
    im = canvas(); d = ImageDraw.Draw(im)
    _cloud(d, -50)
    d.polygon([(286, 250), (186, 390), (250, 390), (214, 492), (330, 350), (266, 350), (316, 250)], fill=0)
    d.polygon([(290, 270), (200, 380), (262, 380), (226, 480), (318, 360), (256, 360), (306, 270)], fill=255)
    return im


def g_mine():
    im = canvas(); d = ImageDraw.Draw(im)
    for k in range(8):
        a = math.radians(k * 45)
        thick_line(d, [(256, 256), (256 + 200 * math.cos(a), 256 + 200 * math.sin(a))], 30)
        circle(d, 256 + 200 * math.cos(a), 256 + 200 * math.sin(a), 30)
    circle(d, 256, 256, 140)
    circle(d, 210, 210, 30, 0)
    return im


def g_spikes():
    im = canvas(); d = ImageDraw.Draw(im)
    for k in range(14):
        a = math.radians(k * (360 / 14))
        b1, b2 = a - math.radians(12), a + math.radians(12)
        d.polygon([(256 + 100 * math.cos(b1), 256 + 100 * math.sin(b1)),
                   (256 + 236 * math.cos(a), 256 + 236 * math.sin(a)),
                   (256 + 100 * math.cos(b2), 256 + 100 * math.sin(b2))], fill=255)
    circle(d, 256, 256, 130)
    return im


def g_horn():
    im = canvas(); d = ImageDraw.Draw(im)
    d.polygon([(70, 300), (90, 250), (340, 120), (360, 400)], fill=255)
    d.polygon(ellipse_poly(350, 260, 60, 150, 0), fill=255)
    d.polygon(ellipse_poly(354, 260, 30, 110, 0), fill=0)
    for k, r in enumerate((60, 110)):
        d.arc((380 - r, 260 - r * 1.6, 380 + r, 260 + r * 1.6), -40, 40, fill=255, width=22)
    return im


def g_needles():
    im = canvas(); d = ImageDraw.Draw(im)
    for off in (-110, 0, 110):
        d.polygon([(256 + off, 40), (256 + off + 26, 256), (256 + off, 472), (256 + off - 26, 256)], fill=255)
        circle(d, 256 + off, 120, 12, 0)
    return rotate_img(im, 40)


def g_sling():
    im = canvas(); d = ImageDraw.Draw(im)
    circle(d, 340, 190, 110)
    for k, (x0, y0) in enumerate(((60, 300), (110, 380), (40, 220))):
        thick_line(d, [(x0, y0), (x0 + 170, y0 - 70)], 26)
    d.arc((60, 330, 470, 520), 200, 340, fill=255, width=24)
    return im


def g_cluster():
    im = canvas(); d = ImageDraw.Draw(im)
    for (x, y, r) in ((200, 300, 110), (330, 330, 90), (300, 170, 86)):
        circle(d, x, y, r + 14, 0)
        circle(d, x, y, r)
    for (x, y) in ((120, 110), (60, 200)):
        thick_line(d, [(x, y), (x + 80, y + 40)], 22)
    return im


def g_mushroom():
    im = canvas(); d = ImageDraw.Draw(im)
    d.pieslice((60, 70, 452, 400), 180, 360, fill=255)
    d.rounded_rectangle((60, 220, 452, 250), 14, fill=255)
    d.rounded_rectangle((196, 240, 316, 450), 40, fill=255)
    d.rectangle((60, 252, 452, 266), fill=0)
    for (x, y, r) in ((170, 160, 32), (290, 120, 26), (360, 190, 30)):
        circle(d, x, y, r, 0)
    return im


def g_lance():
    im = canvas(); d = ImageDraw.Draw(im)
    d.rectangle((236, 40, 276, 330), fill=255)
    d.polygon([(256, 480), (190, 330), (256, 290), (322, 330)], fill=255)
    d.rounded_rectangle((170, 70, 342, 110), 16, fill=255)
    return rotate_img(im, -30)


def g_lantern():
    im = canvas(); d = ImageDraw.Draw(im)
    d.arc((196, 26, 316, 140), 180, 360, fill=255, width=22)
    d.polygon([(170, 120), (342, 120), (372, 170), (140, 170)], fill=255)
    d.rounded_rectangle((150, 160, 362, 420), 30, fill=255)
    d.rounded_rectangle((186, 196, 326, 384), 18, fill=0)
    circle(d, 256, 320, 46)
    d.polygon([(256, 214), (218, 306), (294, 306)], fill=255)
    d.rounded_rectangle((130, 410, 382, 456), 16, fill=255)
    return im


def g_regen():
    im = load_kenney("heart")
    d = ImageDraw.Draw(im)
    d.rectangle((236, 150, 276, 330), fill=0)
    d.rectangle((166, 220, 346, 260), fill=0)
    return im


def _blade():
    im = canvas(); d = ImageDraw.Draw(im)
    d.polygon([(256, 20), (290, 80), (290, 330), (222, 330), (222, 80)], fill=255)
    d.rounded_rectangle((160, 326, 352, 366), 14, fill=255)
    d.rectangle((238, 366, 274, 446), fill=255)
    circle(d, 256, 460, 28)
    return im


def g_swords():
    front = rotate_img(_blade(), 42)
    back = rotate_img(_blade(), -42)
    back = ImageChops.subtract(back, front.filter(ImageFilter.MaxFilter(29)))
    return ImageChops.lighter(back, front)


def g_fang():
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle((70, 90, 442, 170), 36, fill=255)
    for side in (-1, 1):
        cx = 256 + side * 92
        d.polygon([(cx - 70, 150), (cx + 70, 150), (cx + 10 * side, 440), (cx - 4 * side, 448)], fill=255)
    d.polygon([(226, 150), (286, 150), (256, 250)], fill=255)
    return im


def g_anchor():
    im = canvas(); d = ImageDraw.Draw(im)
    d.ellipse((206, 30, 306, 130), outline=255, width=26)
    d.rectangle((234, 120, 278, 440), fill=255)
    d.rounded_rectangle((150, 160, 362, 196), 16, fill=255)
    d.arc((86, 200, 426, 460), 20, 160, fill=255, width=40)
    arrow_head(d, (440, 300), -70, 80, 90)
    arrow_head(d, (72, 300), -110, 80, 90)
    return im


def g_spyglass():
    """A telescope at an angle with a four-point glint at its lens."""
    im = canvas(); d = ImageDraw.Draw(im)
    # three nested tube sections, thinning toward the eyepiece
    sections = [((96, 404), (214, 286), 58), ((214, 286), (306, 194), 46), ((306, 194), (366, 134), 34)]
    for a, b, w in sections:
        thick_line(d, [a, b], w * 2)
    for (x, y), w in (((214, 286), 70), ((306, 194), 56)):
        d.line([rot([(x - w, y)], 45, x, y)[0], rot([(x + w, y)], 45, x, y)[0]], fill=0, width=12)
    # glint
    gx, gy = 420, 92
    d.polygon([(gx, gy - 78), (gx + 16, gy - 16), (gx + 78, gy), (gx + 16, gy + 16),
               (gx, gy + 78), (gx - 16, gy + 16), (gx - 78, gy), (gx - 16, gy - 16)], fill=255)
    return im


def g_mooring():
    """An anchor inside a rope ring."""
    im = g_anchor().resize((330, 330), Image.LANCZOS)
    out = canvas(); out.paste(im, (91, 70))
    d = ImageDraw.Draw(out)
    d.ellipse((30, 300, 482, 470), outline=255, width=24)
    for a in (200, 340):
        x = 256 + 226 * math.cos(math.radians(a)); y = 385 + 85 * math.sin(math.radians(a))
        d.rounded_rectangle((x - 18, y - 40, x + 18, y + 10), 8, fill=255)
    return out


def g_kite():
    """A diamond kite with spars and a ribbon tail on a curving string."""
    im = canvas(); d = ImageDraw.Draw(im)
    top, right, bottom, left = (330, 40), (452, 170), (330, 330), (208, 170)
    d.polygon([top, right, bottom, left], fill=255)
    d.line([top, bottom], fill=0, width=14)
    d.line([left, right], fill=0, width=14)
    pts = [(330, 330), (300, 380), (250, 400), (200, 430), (140, 470)]
    thick_line(d, pts, 16)
    for (x, y) in ((292, 386), (214, 420)):
        d.polygon([(x - 34, y - 26), (x, y), (x - 34, y + 26)], fill=255)
        d.polygon([(x + 34, y - 26), (x, y), (x + 34, y + 26)], fill=255)
    return im


def g_gem():
    im = canvas(); d = ImageDraw.Draw(im)
    d.polygon([(160, 100), (352, 100), (452, 210), (256, 450), (60, 210)], fill=255)
    d.line([(60, 210), (452, 210)], fill=0, width=14)
    d.line([(190, 210), (256, 440)], fill=0, width=12)
    d.line([(322, 210), (256, 440)], fill=0, width=12)
    d.line([(190, 210), (230, 104)], fill=0, width=12)
    d.line([(322, 210), (282, 104)], fill=0, width=12)
    return im


def g_spark():
    im = canvas(); d = ImageDraw.Draw(im)
    pts = []
    for k in range(8):
        a = math.radians(k * 45 - 90)
        r = 230 if k % 2 == 0 else 62
        pts.append((256 + r * math.cos(a), 256 + r * math.sin(a)))
    d.polygon(pts, fill=255)
    for (x, y) in ((400, 110), (120, 400)):
        circle(d, x, y, 30)
    return im


def g_comet():
    im = canvas(); d = ImageDraw.Draw(im)
    circle(d, 340, 172, 104)
    d.polygon([(260, 110), (60, 440), (410, 250)], fill=255)
    circle(d, 340, 172, 112, 255)
    d.line([(140, 330), (250, 230)], fill=0, width=16)
    d.line([(200, 380), (300, 270)], fill=0, width=14)
    return im


def g_moon():
    im = canvas(); d = ImageDraw.Draw(im)
    circle(d, 256, 256, 200)
    circle(d, 340, 200, 170, 0)
    for (x, y, r) in ((380, 380, 24), (430, 290, 16)):
        circle(d, x, y, r)
    return im


def g_medkit():
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle((60, 120, 452, 440), 50, fill=255)
    d.rounded_rectangle((186, 70, 326, 140), 20, outline=255, width=26)
    d.rectangle((226, 190, 286, 370), fill=0)
    d.rectangle((166, 250, 346, 310), fill=0)
    return im


def g_bubble():
    im = canvas(); d = ImageDraw.Draw(im)
    d.polygon([(256, 40), (440, 130), (440, 300), (256, 470), (72, 300), (72, 130)], fill=255)
    d.polygon([(256, 96), (390, 162), (390, 290), (256, 410), (122, 290), (122, 162)], fill=0)
    d.polygon([(256, 140), (348, 186), (348, 280), (256, 362), (164, 280), (164, 186)], fill=255)
    return im


def g_buoy():
    im = canvas(); d = ImageDraw.Draw(im)
    circle(d, 256, 256, 210)
    circle(d, 256, 256, 110, 0)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        thick_line(d, [(256 + 112 * math.cos(a), 256 + 112 * math.sin(a)), (256 + 208 * math.cos(a), 256 + 208 * math.sin(a))], 22, 0)
    return im


def g_tome():
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle((96, 50, 420, 470), 34, fill=255)
    d.rectangle((140, 50, 154, 470), fill=0)              # spine crease
    d.rectangle((154, 420, 420, 432), fill=0)             # page edge
    d.rounded_rectangle((330, 26, 368, 120), 8, fill=255)  # ribbon
    return im



# ---- map challenges, Toggler, slots and the cute shrines

def g_scales():
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle((236, 110, 276, 420), 16, fill=255)        # post
    d.rounded_rectangle((150, 410, 362, 456), 20, fill=255)        # foot
    circle(d, 256, 96, 34)                                          # knob
    thick_line(d, [(92, 150), (420, 150)], 30)                      # beam
    for cx in (110, 402):
        thick_line(d, [(cx, 150), (cx - 62, 290)], 10)
        thick_line(d, [(cx, 150), (cx + 62, 290)], 10)
        d.pieslice((cx - 92, 220, cx + 92, 350), 0, 180, fill=255)   # pan
    return im


def g_well():
    im = canvas(); d = ImageDraw.Draw(im)
    d.polygon([(70, 190), (256, 50), (442, 190)], fill=255)          # roof
    d.rectangle((110, 180, 140, 330), fill=255)                      # posts
    d.rectangle((372, 180, 402, 330), fill=255)
    thick_line(d, [(256, 190), (256, 270)], 10)                      # rope
    d.rounded_rectangle((222, 262, 290, 318), 10, fill=255)          # bucket
    d.rounded_rectangle((80, 320, 432, 470), 40, fill=255)           # stone ring
    for x in (170, 256, 342):
        d.rectangle((x - 4, 330, x + 4, 460), fill=0)                # stone joints
    d.rectangle((80, 392, 432, 400), fill=0)
    return im


def g_toggle():
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle((40, 150, 472, 362), 106, fill=255)
    circle(d, 366, 256, 80, 0)
    circle(d, 366, 256, 62, 255)
    return im


def g_flag():
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle((110, 50, 146, 470), 16, fill=255)           # pole
    circle(d, 128, 52, 28)
    pts = [(146, 80)]
    for x in range(146, 450, 8):
        pts.append((x, 90 + 26 * math.sin((x - 146) / 304 * 2 * math.pi)))
    for x in range(450, 145, -8):
        pts.append((x, 280 + 26 * math.sin((x - 146) / 304 * 2 * math.pi)))
    d.polygon(pts, fill=255)
    return im


def g_slot():
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle((60, 60, 452, 452), 70, fill=255)
    d.rounded_rectangle((110, 110, 402, 402), 44, fill=0)
    d.rounded_rectangle((232, 150, 280, 362), 16, fill=255)         # plus
    d.rounded_rectangle((150, 232, 362, 280), 16, fill=255)
    return im


PROCEDURAL = {
    "boot": g_boot, "dash": g_dash, "magnet": g_magnet, "snowflake": g_snowflake,
    "bolt": g_bolt, "drop": g_drop, "spring": g_spring, "feather": g_feather, "coin": g_coin,
    "dart": g_dart, "multi": g_multi, "homing": g_homing, "aura": g_aura, "bomb": g_bomb,
    "orbit": g_orbit, "chakram": g_chakram, "beam": g_beam, "hammer": g_hammer,
    "chain": g_chain, "wave": g_wave, "bell": g_bell, "vortex": g_vortex, "cloud": g_cloud,
    "storm": g_storm, "mine": g_mine, "spikes": g_spikes, "horn": g_horn,
    "needles": g_needles, "sling": g_sling, "cluster": g_cluster, "mushroom": g_mushroom,
    "lance": g_lance, "lantern": g_lantern, "regen": g_regen, "swords": g_swords,
    "fang": g_fang, "anchor": g_anchor, "gem": g_gem, "spark": g_spark, "comet": g_comet,
    "moon": g_moon, "medkit": g_medkit, "bubble": g_bubble, "buoy": g_buoy, "tome": g_tome,
    "scales": g_scales, "well": g_well, "toggle": g_toggle, "flag": g_flag, "slot": g_slot,
    "spyglass": g_spyglass, "mooring": g_mooring, "kite": g_kite,
}


# -------------------------------------------------------------- pipeline

ROOT = ""


def load_kenney(name):
    folder, file = KENNEY[name]
    src = Image.open(os.path.join(ROOT, folder, file)).convert("RGBA")
    # Kenney glyphs are white on transparent: the alpha is the shape.
    a = src.getchannel("A")
    return a.resize((S, S), Image.LANCZOS)


def normalise(mask):
    """Crop to the shape and scale it into the standard glyph box."""
    box = mask.point(lambda v: 255 if v > 40 else 0).getbbox()
    if not box:
        return mask
    shape = mask.crop(box)
    w, h = shape.size
    k = FIT / max(w, h)
    shape = shape.resize((max(1, round(w * k)), max(1, round(h * k))), Image.LANCZOS)
    out = canvas()
    out.paste(shape, ((S - shape.width) // 2, (S - shape.height) // 2))
    return out


def render(mask):
    mask = normalise(mask)
    outline = mask.filter(ImageFilter.MaxFilter(2 * OUTLINE + 1)).filter(ImageFilter.GaussianBlur(1.5))
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    dark = Image.new("RGBA", (S, S), (14, 16, 24, 255))
    dark.putalpha(outline.point(lambda v: int(v * 0.9)))
    img.alpha_composite(dark)
    white = Image.new("RGBA", (S, S), (255, 255, 255, 255))
    white.putalpha(mask)
    img.alpha_composite(white)
    return img.resize((OUT, OUT), Image.LANCZOS)


def main():
    global ROOT
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    out_dir_default = os.path.join(os.path.dirname(__file__), "..", "assets", "icons")
    if sys.argv[1] == "--procedural":
        # Redraw only the original glyphs (all, or the names given); the
        # Kenney-derived icons already in the folder are left alone.
        wanted = sys.argv[2:] or list(PROCEDURAL)
        os.makedirs(out_dir_default, exist_ok=True)
        for name in wanted:
            render(PROCEDURAL[name]()).save(os.path.join(out_dir_default, name + ".png"))
        print(f"{len(wanted)} procedural icons -> {os.path.abspath(out_dir_default)}")
        return
    ROOT = sys.argv[1]
    out_dir = sys.argv[2] if len(sys.argv) > 2 else out_dir_default
    os.makedirs(out_dir, exist_ok=True)
    names = []
    for name in KENNEY:
        render(load_kenney(name)).save(os.path.join(out_dir, name + ".png"))
        names.append(name)
    for name, fn in PROCEDURAL.items():
        render(fn()).save(os.path.join(out_dir, name + ".png"))
        names.append(name)
    print(f"{len(names)} icons -> {os.path.abspath(out_dir)}")


if __name__ == "__main__":
    main()
