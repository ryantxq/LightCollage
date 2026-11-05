# -*- coding: utf-8 -*-
"""
轻拼 LightCollage — 网格布局拼图 UI mockups
============================================

Generates two design-confirmation mockups (not screenshots):

    docs/ui-mockup-grid-main.png    main window, 1440 x 900 logical
    docs/ui-mockup-grid-merge.png   merge editor dialog, 860 x 620 logical

Run from the project root:

    python tools\\make_mockup_grid.py

Everything is drawn at 2x and downsampled with LANCZOS.  Palette is sampled
from docs/screenshots/main-window.png (neutral dark UI: #0A0A0A window,
#161616 panel, #101010 preview, #262626 / #1B1B1B checker, #242424 controls,
#F2F2F2 / #BDBDBD / #8A8A8A / #616161 text) plus the teal accent called for by
the grid-layout feature design (#1F8A7A fill, #6ED8C5 highlight).
"""

from PIL import Image, ImageDraw, ImageFont, ImageFilter
import math
import os

# --------------------------------------------------------------------------- #
#  Config
# --------------------------------------------------------------------------- #

S = 2                                    # supersampling factor
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "docs")
FONT_R = r"C:\Windows\Fonts\msyh.ttc"    # Microsoft YaHei
FONT_B = r"C:\Windows\Fonts\msyhbd.ttc"  # Microsoft YaHei Bold

MAIN_W, MAIN_H = 1440, 900
WIN_X, WIN_Y, WIN_W, WIN_H = 130, 60, 1180, 780

MERGE_W, MERGE_H = 860, 620


def C(h, a=255):
    """hex string -> RGBA tuple."""
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), int(a))


# neutral ramp sampled from the shipping app
BG0          = C("#07080B")
WINDOW       = C("#0B0C10")
TITLEBAR     = C("#191B21")
SIDEBAR      = C("#171A20")
PREVIEW      = C("#0F1116")
DIALOG       = C("#1A1E26")
CONTROL      = C("#242830")
CONTROL_DEEP = C("#14171C")
BORDER       = C("#2E333C")
BORDER_SOFT  = C("#252A32")
BORDER_LINE  = C("#3A404B")
CHECK_L      = C("#262A31")
CHECK_D      = C("#1B1E24")
TEXT         = C("#F2F4F8")
TEXT2        = C("#BDC2CB")
MUTED        = C("#8A909B")
DIM          = C("#626873")
ACCENT       = C("#1F8A7A")
ACCENT_DEEP  = C("#16766A")
ACCENT_HI    = C("#6ED8C5")
ACCENT_SOFT  = C("#2FBFA6")
TEAL_TINT    = C("#12312C")
WHITE        = C("#FFFFFF")

# --------------------------------------------------------------------------- #
#  Primitive helpers
# --------------------------------------------------------------------------- #


def sc(v):
    return int(round(v * S))


def pbox(b):
    return [sc(b[0]), sc(b[1]), sc(b[2]), sc(b[3])]


def new_draw(im):
    """Draw object that alpha-blends 4-tuple fills on RGB canvases."""
    return ImageDraw.Draw(im, "RGBA") if im.mode == "RGB" else ImageDraw.Draw(im)


def rrect(d, b, r, fill=None, outline=None, width=1):
    d.rounded_rectangle(pbox(b), radius=sc(r), fill=fill, outline=outline,
                        width=max(1, sc(width)))


def rect(d, b, fill):
    d.rectangle(pbox(b), fill=fill)


def line(d, x1, y1, x2, y2, fill, width=1):
    d.line([sc(x1), sc(y1), sc(x2), sc(y2)], fill=fill,
           width=max(1, sc(width)))


_FONTS = {}


def F(size, bold=False):
    key = (max(1, int(round(size * S))), bool(bold))
    if key not in _FONTS:
        _FONTS[key] = ImageFont.truetype(FONT_B if bold else FONT_R, key[0])
    return _FONTS[key]


def text(d, xy, s, size=11, fill=TEXT, bold=False, anchor="la"):
    if not s:
        return
    d.text((sc(xy[0]), sc(xy[1])), s, font=F(size, bold), fill=fill, anchor=anchor)


def tw(s, size=11, bold=False):
    return F(size, bold).getlength(s) / float(S)


def rich(d, x, y, segs, size=11, anchor="la"):
    """Draw a sequence of differently coloured runs, return total width."""
    cx = x
    for s, color, bold in segs:
        text(d, (cx, y), s, size, color, bold, anchor=anchor)
        cx += tw(s, size, bold)
    return cx - x


def rich_w(segs, size=11):
    return sum(tw(s, size, b) for s, _c, b in segs)


def chip_width(label, size=11.5, padx=11, bold=False, dot=False):
    return tw(label, size, bold) + padx * 2 + (9 if dot else 0)


def chip(d, x, y, label, size=11.5, fg=TEXT, bg=(6, 8, 11, 200),
         border=(255, 255, 255, 32), padx=11, h=30, r=8, bold=False,
         dot=None):
    w = chip_width(label, size, padx, bold, dot is not None)
    rrect(d, (x, y, x + w, y + h), r, fill=bg, outline=border, width=1)
    tx = x + padx
    if dot:
        cy = y + h / 2
        d.ellipse([sc(tx - 2), sc(cy - 2.5), sc(tx + 3), sc(cy + 2.5)], fill=dot)
        tx += 9
    text(d, (tx, y + h / 2), label, size, fg, bold, anchor="lm")
    return w


def dashed_line(d, x1, y1, x2, y2, fill, width=1, dash=5, gap=5):
    dx, dy = x2 - x1, y2 - y1
    length = math.hypot(dx, dy)
    if length <= 0.01:
        return
    ux, uy = dx / length, dy / length
    pos = 0.0
    while pos < length:
        end = min(pos + dash, length)
        d.line([sc(x1 + ux * pos), sc(y1 + uy * pos),
                sc(x1 + ux * end), sc(y1 + uy * end)],
               fill=fill, width=max(1, sc(width)))
        pos = end + gap


def _round_rect_points(x, y, w, h, r, step=1.0):
    """Dense clockwise perimeter points of a rounded rectangle (logical px)."""
    r = max(0.0, min(r, w / 2.0, h / 2.0))
    pts = []

    def edge(x1, y1, x2, y2):
        n = max(2, int(math.hypot(x2 - x1, y2 - y1) / step) + 1)
        for i in range(n + 1):
            pts.append((x1 + (x2 - x1) * i / n, y1 + (y2 - y1) * i / n))

    def arc(cx, cy, a0, a1):
        n = max(4, int(abs(math.radians(a1 - a0)) * r / step) + 1)
        for i in range(n + 1):
            a = math.radians(a0 + (a1 - a0) * i / n)
            pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))

    edge(x + r, y, x + w - r, y)
    arc(x + w - r, y + r, -90, 0)
    edge(x + w, y + r, x + w, y + h - r)
    arc(x + w - r, y + h - r, 0, 90)
    edge(x + w - r, y + h, x + r, y + h)
    arc(x + r, y + h - r, 90, 180)
    edge(x, y + h - r, x, y + r)
    arc(x + r, y + r, 180, 270)
    return pts


def dashed_rrect(d, b, r, fill, width=1, dash=5, gap=5):
    pts = _round_rect_points(b[0], b[1], b[2] - b[0], b[3] - b[1], r)
    period = dash + gap
    acc = 0.0
    for p0, p1 in zip(pts, pts[1:]):
        seg = math.hypot(p1[0] - p0[0], p1[1] - p0[1])
        if seg <= 0:
            continue
        steps = max(1, int(seg / 1.2))
        for i in range(steps):
            t0 = i / steps
            t1 = (i + 1) / steps
            if (acc + seg * t0) % period < dash:
                d.line([sc(p0[0] + (p1[0] - p0[0]) * t0),
                        sc(p0[1] + (p1[1] - p0[1]) * t0),
                        sc(p0[0] + (p1[0] - p0[0]) * t1),
                        sc(p0[1] + (p1[1] - p0[1]) * t1)],
                       fill=fill, width=max(1, sc(width)))
        acc += seg


def rounded_mask(w, h, r):
    m = Image.new("L", (sc(w), sc(h)), 0)
    ImageDraw.Draw(m).rounded_rectangle(
        [0, 0, sc(w) - 1, sc(h) - 1], radius=sc(r), fill=255)
    return m


def paste_rounded(base, im, x, y, w, h, r):
    target = (sc(w), sc(h))
    if im.size != target:
        im = im.resize(target, Image.LANCZOS)
    im = im.convert("RGBA")
    im.putalpha(rounded_mask(w, h, r))
    base.paste(im, (sc(x), sc(y)), im)


def checker(w, h, cell=14, c1=CHECK_L, c2=CHECK_D):
    im = Image.new("RGBA", (sc(w), sc(h)), c1)
    dd = ImageDraw.Draw(im)
    cs = max(2, sc(cell))
    for yy in range(0, sc(h), cs):
        for xx in range(0, sc(w), cs):
            if ((xx // cs) + (yy // cs)) & 1:
                dd.rectangle([xx, yy, min(xx + cs - 1, sc(w) - 1),
                              min(yy + cs - 1, sc(h) - 1)], fill=c2)
    return im


def lerp_col(c1, c2, t):
    t = max(0.0, min(1.0, t))
    return tuple(int(round(c1[i] + (c2[i] - c1[i]) * t)) for i in range(4))


def grad_col(stops, t):
    if t <= stops[0][0]:
        return stops[0][1]
    for i in range(1, len(stops)):
        if t <= stops[i][0]:
            a, b = stops[i - 1], stops[i]
            f = (t - a[0]) / max(1e-6, (b[0] - a[0]))
            return lerp_col(a[1], b[1], f)
    return stops[-1][1]


def vgrad(w, h, stops):
    """stops: [(0.0, RGBA), ...]"""
    W, H = sc(w), sc(h)
    im = Image.new("RGBA", (W, H), stops[0][1])
    dd = ImageDraw.Draw(im)
    for yy in range(H):
        t = yy / float(max(1, H - 1))
        dd.line([(0, yy), (W, yy)], fill=grad_col(stops, t))
    return im


def vignette(im, strength=90):
    W, H = im.size
    mask = Image.radial_gradient("L").resize((W, H), Image.LANCZOS)
    mask = mask.point(lambda p: int(p * strength / 255.0))
    dark = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dark.putalpha(mask)
    im.paste(dark, (0, 0), dark)


def bottom_scrim(im, w, h, start=0.58, alpha=130):
    W, H = im.size
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dd = ImageDraw.Draw(layer)
    y0 = int(H * start)
    for yy in range(y0, H):
        t = (yy - y0) / float(max(1, H - y0))
        a = int(alpha * (t ** 1.6))
        dd.line([(0, yy), (W, yy)], fill=(3, 6, 8, a))
    im.paste(layer, (0, 0), layer)


# --------------------------------------------------------------------------- #
#  Abstract "photos" (generated gradients, deliberately not real images)
# --------------------------------------------------------------------------- #


def _glow(im, cx, cy, rx, ry, color, blur):
    layer = Image.new("RGBA", im.size, (0, 0, 0, 0))
    dd = ImageDraw.Draw(layer)
    dd.ellipse([sc(cx - rx), sc(cy - ry), sc(cx + rx), sc(cy + ry)], fill=color)
    layer = layer.filter(ImageFilter.GaussianBlur(sc(blur)))
    im.paste(layer, (0, 0), layer)


def _wave(d, w, y, amp, period, color, width=1.0, phase=0.0):
    pts = []
    for i in range(0, 161):
        x = w * i / 160.0
        yy = y + amp * math.sin((x / period) * math.pi * 2 + phase)
        pts.append((sc(x), sc(yy)))
    d.line(pts, fill=color, width=max(1, sc(width)), joint="curve")


def art_beach(w, h):
    im = vgrad(w, h, [
        (0.00, C("#052B41")), (0.32, C("#0D627B")), (0.53, C("#2797A4")),
        (0.70, C("#74D2C9")), (0.86, C("#2E8E92")), (1.00, C("#09323C")),
    ]).convert("RGB")
    _glow(im, w * .70, h * .30, w * .34, h * .30, C("#FFE2AE", 125), w * .09)
    d = new_draw(im)
    # sun
    sx, sy, sr = w * .70, h * .295, w * .070
    d.ellipse([sc(sx - sr), sc(sy - sr), sc(sx + sr), sc(sy + sr)],
              fill=C("#FFEFC9", 238))
    d.ellipse([sc(sx - sr * .62), sc(sy - sr * .62), sc(sx + sr * .62), sc(sy + sr * .62)],
              fill=C("#FFF9E8", 250))
    # horizon
    line(d, 0, h * .565, w, h * .565, C("#E9FFFA", 85), 1)
    # distant island
    island = [(0, h * .565), (w * .08, h * .545), (w * .15, h * .552),
              (w * .235, h * .535), (w * .31, h * .556), (w * .37, h * .565)]
    d.polygon([(sc(x), sc(y)) for x, y in island], fill=C("#082A33", 210))
    # sea sparkle / waves
    for yy, amp, col in [
            (h * .615, h * .010, C("#E8FFFA", 55)),
            (h * .675, h * .014, C("#E8FFFA", 48)),
            (h * .745, h * .018, C("#D7FFF7", 38)),
            (h * .825, h * .020, C("#C9F7ED", 30))]:
        _wave(d, w, yy, amp, w * .42, col, 1.1, phase=yy * .03)
    # wet foreground
    d.polygon([(0, h * .94), (w * .20, h * .895), (w * .46, h * .925),
               (w * .72, h * .885), (w, h * .925), (w, h), (0, h)],
              fill=C("#062B34", 120))
    _glow(im, w * .28, h * .66, w * .22, h * .05, C("#BFF7EE", 45), w * .04)
    bottom_scrim(im, w, h, .66, 105)
    vignette(im, 78)
    return im


def art_coffee(w, h):
    im = vgrad(w, h, [
        (0.00, C("#1B100C")), (0.38, C("#4A281B")), (0.72, C("#9A5F36")),
        (1.00, C("#D89A5C")),
    ]).convert("RGB")
    _glow(im, w * .78, h * .20, w * .34, h * .28, C("#FFD9A0", 95), w * .08)
    d = new_draw(im)
    # table
    d.ellipse([sc(-w * .18), sc(h * .72), sc(w * 1.12), sc(h * 1.12)],
              fill=C("#2A1710", 115))
    # saucer
    cx, cy = w * .52, h * .655
    d.ellipse([sc(cx - w * .34), sc(cy - h * .10), sc(cx + w * .34), sc(cy + h * .10)],
              fill=C("#1A0E0A", 180))
    d.ellipse([sc(cx - w * .34), sc(cy - h * .10), sc(cx + w * .34), sc(cy + h * .10)],
              outline=C("#E4BE8A", 60), width=max(1, sc(1)))
    # cup body + rim
    cr = w * .245
    d.ellipse([sc(cx - cr), sc(cy - cr), sc(cx + cr), sc(cy + cr)],
              fill=C("#24130E", 255))
    d.ellipse([sc(cx - cr * .94), sc(cy - cr * .94), sc(cx + cr * .94), sc(cy + cr * .94)],
              outline=C("#E8C49A", 150), width=max(2, sc(2)))
    # handle
    d.arc([sc(cx + cr * .62), sc(cy - cr * .58), sc(cx + cr * 1.35), sc(cy + cr * .58)],
          start=-78, end=78, fill=C("#E8C49A", 130), width=max(2, sc(2.2)))
    # coffee
    d.ellipse([sc(cx - cr * .78), sc(cy - cr * .78), sc(cx + cr * .78), sc(cy + cr * .78)],
              fill=C("#3A2115", 255))
    d.ellipse([sc(cx - cr * .66), sc(cy - cr * .66), sc(cx + cr * .66), sc(cy + cr * .66)],
              outline=C("#B97A45", 105), width=max(1, sc(1.4)))
    # crema swirl
    d.arc([sc(cx - cr * .55), sc(cy - cr * .55), sc(cx + cr * .55), sc(cy + cr * .55)],
          start=200, end=330, fill=C("#F0C894", 95), width=max(1, sc(1.6)))
    # steam
    steam = Image.new("RGBA", im.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(steam)
    for i, xx in enumerate([cx - cr * .45, cx - cr * .05, cx + cr * .38]):
        pts = []
        for j in range(0, 41):
            t = j / 40.0
            yy = cy - cr * (1.02 + t * 1.05)
            x = xx + math.sin(t * math.pi * 2.4 + i) * (w * .025) * t
            pts.append((sc(x), sc(yy)))
        sd.line(pts, fill=C("#FFF4DE", 65 - i * 8), width=max(2, sc(2.2)),
                joint="curve")
    steam = steam.filter(ImageFilter.GaussianBlur(sc(1.4)))
    im.paste(steam, (0, 0), steam)
    # warm bokeh
    for bx, by, br, al in [(.19, .25, .055, 55), (.27, .13, .030, 45),
                          (.86, .40, .040, 40), (.10, .55, .022, 35)]:
        d.ellipse([sc(w * (bx - br)), sc(h * (by - br)),
                   sc(w * (bx + br)), sc(h * (by + br))],
                  outline=C("#FFE2B6", al), width=max(1, sc(1.2)))
    bottom_scrim(im, w, h, .62, 115)
    vignette(im, 88)
    return im


def art_night(w, h):
    im = vgrad(w, h, [
        (0.00, C("#080A1E")), (0.34, C("#211A45")), (0.62, C("#4C2D5E")),
        (0.82, C("#7B4268")), (1.00, C("#20132C")),
    ]).convert("RGB")
    _glow(im, w * .76, h * .24, w * .24, h * .20, C("#E6D5FF", 120), w * .06)
    d = new_draw(im)
    # moon
    mx, my, mr = w * .76, h * .235, w * .060
    d.ellipse([sc(mx - mr), sc(my - mr), sc(mx + mr), sc(my + mr)],
              fill=C("#F4ECFF", 235))
    d.ellipse([sc(mx - mr * .78), sc(my - mr * .78),
               sc(mx + mr * .50), sc(my + mr * .50)],
              fill=C("#E8DCFA", 70))
    # bokeh
    for bx, by, br, al in [(.18, .20, .050, 40), (.28, .30, .026, 32),
                          (.55, .16, .036, 28), (.88, .50, .022, 32),
                          (.12, .47, .018, 26)]:
        d.ellipse([sc(w * (bx - br)), sc(h * (by - br)),
                   sc(w * (bx + br)), sc(h * (by + br))],
                  fill=C("#FFE2B8", al))
    # skyline (deterministic)
    buildings = [(0.00, .19, .17), (0.17, .13, .25), (0.28, .16, .14),
                 (0.42, .11, .22), (0.52, .18, .12), (0.68, .14, .27),
                 (0.80, .20, .16)]
    base_y = h * .895
    for bx, bw, bh in buildings:
        x0, x1 = w * bx, w * (bx + bw * .62)
        y0 = base_y - h * bh
        d.rectangle([sc(x0), sc(y0), sc(x1), sc(base_y)], fill=C("#070812", 238))
        # roof antenna
        if bh > .2:
            line(d, (x0 + x1) / 2, y0, (x0 + x1) / 2, y0 - h * .03,
                 C("#070812", 238), 1)
    # tiny windows
    win_cols = [C("#FFD58A", 195), C("#FFE8B8", 150), C("#BFE9FF", 120)]
    seed = 11
    for bx, bw, bh in buildings:
        x0, x1 = w * bx, w * (bx + bw * .62)
        y0 = base_y - h * bh
        cols = max(2, int((x1 - x0) / (w * .030)))
        rows = max(3, int((base_y - y0) / (h * .045)))
        for cy in range(rows):
            for cxx in range(cols):
                seed = (seed * 1103515245 + 12345) & 0x7fffffff
                if seed % 5 > 2:
                    continue
                px = x0 + (cxx + .5) * (x1 - x0) / cols
                py = y0 + (cy + .55) * (base_y - y0) / rows
                d.rectangle([sc(px - .9), sc(py - .9), sc(px + .9), sc(py + .9)],
                            fill=win_cols[seed % 3])
    # ground haze
    d.rectangle([0, sc(base_y), sc(w), sc(h)], fill=C("#0A0710", 245))
    _glow(im, w * .52, h * .84, w * .55, h * .10, C("#FFB46B", 32), w * .08)
    # light streaks
    for yy, al in [(.905, 35), (.94, 26)]:
        line(d, 0, h * yy, w, h * yy, C("#FFD9A0", al), 1)
    bottom_scrim(im, w, h, .60, 100)
    vignette(im, 92)
    return im


def art_mountain(w, h):
    im = vgrad(w, h, [
        (0.00, C("#0C1E20")), (0.30, C("#27473F")), (0.55, C("#5E7A61")),
        (0.72, C("#B9B58C")), (1.00, C("#17281F")),
    ]).convert("RGB")
    _glow(im, w * .22, h * .28, w * .34, h * .24, C("#FFE6B8", 105), w * .07)
    d = new_draw(im)
    # sun
    sx, sy, sr = w * .22, h * .275, w * .045
    d.ellipse([sc(sx - sr), sc(sy - sr), sc(sx + sr), sc(sy + sr)],
              fill=C("#FFF1CD", 215))
    # far ridge
    far = [(0, .60), (.12, .47), (.22, .53), (.36, .37), (.48, .49),
           (.60, .40), (.74, .53), (.86, .44), (1, .57), (1, 1), (0, 1)]
    d.polygon([(sc(x * w), sc(y * h)) for x, y in far], fill=C("#6E846A", 215))
    # haze band
    haze = Image.new("RGBA", im.size, (0, 0, 0, 0))
    hd = ImageDraw.Draw(haze)
    hd.rectangle([0, sc(h * .48), sc(w), sc(h * .70)], fill=C("#E7E3C4", 46))
    haze = haze.filter(ImageFilter.GaussianBlur(sc(w * .03)))
    im.paste(haze, (0, 0), haze)
    d = new_draw(im)
    # middle ridge
    mid = [(0, .72), (.13, .54), (.26, .63), (.40, .46), (.52, .60),
           (.63, .51), (.78, .64), (.90, .55), (1, .65), (1, 1), (0, 1)]
    d.polygon([(sc(x * w), sc(y * h)) for x, y in mid], fill=C("#2B4A3E", 238))
    # snow / light on peaks
    for px, py, dx in [(.40, .46, .07), (.63, .51, .05), (.13, .54, .05)]:
        d.polygon([(sc(px * w), sc(py * h)),
                   (sc((px - dx) * w), sc((py + .055) * h)),
                   (sc((px - dx * .20) * w), sc((py + .040) * h))],
                  fill=C("#DDE0C8", 120))
    # front ridge
    front = [(0, .86), (.10, .72), (.22, .79), (.34, .66), (.48, .78),
             (.58, .68), (.72, .80), (.84, .70), (1, .78), (1, 1), (0, 1)]
    d.polygon([(sc(x * w), sc(y * h)) for x, y in front], fill=C("#102820", 252))
    # mist
    for yy, al, bx in [(.62, 44, .30), (.70, 36, .62), (.78, 30, .46)]:
        _glow(im, w * bx, h * yy, w * .30, h * .022, C("#E9EBD7", al), w * .025)
    d = new_draw(im)
    # foreground grass hints
    for i in range(11):
        x = w * (0.05 + i * 0.09)
        d.line([sc(x), sc(h * .97), sc(x + w * .01), sc(h * .90)],
               fill=C("#0A1C16", 130), width=max(1, sc(1)))
    bottom_scrim(im, w, h, .60, 120)
    vignette(im, 84)
    return im


def art_street(w, h):
    im = vgrad(w, h, [
        (0.00, C("#211510")), (0.30, C("#55301F")), (0.58, C("#A2653A")),
        (0.78, C("#D5A05F")), (1.00, C("#2A1911")),
    ]).convert("RGB")
    _glow(im, w * .52, h * .40, w * .28, h * .18, C("#FFD9A2", 125), w * .06)
    d = new_draw(im)
    vx, vy = w * .52, h * .415
    # buildings left / right
    d.polygon([(0, h * .13), (w * .30, h * .33), (w * .34, h * .56),
               (0, h * .80)], fill=C("#2A1812", 230))
    d.polygon([(w, h * .08), (w * .72, h * .30), (w * .68, h * .58),
               (w, h * .84)], fill=C("#24140F", 230))
    # road
    d.polygon([(w * .36, h * .50), (w * .64, h * .50), (w * .92, h),
               (w * .08, h)], fill=C("#1B120D", 220))
    # perspective edges
    for x0 in (.36, .41, .47, .53, .59, .64):
        line(d, vx, vy, w * x0, h, C("#E5B783", 32), 1)
    for yy in (.55, .63, .72, .83, .95):
        line(d, 0, h * yy, w, h * yy, C("#E5B783", 22), 1)
    # warm windows / lanterns
    for side in (0, 1):
        for i in range(4):
            t = i / 4.0
            x = w * (0.10 + t * 0.40) if side == 0 else w * (0.90 - t * 0.40)
            y = h * (0.30 + t * 0.28)
            r = w * (.025 - t * .010)
            _glow(im, x, y, r * 2.6, r * 2.6, C("#FFB86B", 48), r * 1.4)
            d = new_draw(im)
            d.ellipse([sc(x - r), sc(y - r), sc(x + r), sc(y + r)],
                      fill=C("#FFCE8A", 225))
            d.ellipse([sc(x - r * .55), sc(y - r * .55),
                       sc(x + r * .55), sc(y + r * .55)],
                      fill=C("#FFF1CE", 235))
            line(d, x, y - r * 2.4, x, y - r, C("#3A2016", 200), 1)
    # distant arch silhouette
    d.arc([sc(vx - w * .12), sc(vy - h * .11), sc(vx + w * .12), sc(vy + h * .13)],
          start=180, end=360, fill=C("#2A1812", 200), width=max(2, sc(2.5)))
    # ground reflections
    for x, al in [(.30, 30), (.44, 24), (.56, 24), (.70, 30)]:
        line(d, x * w, h * .93, vx, h * .56, C("#FFD8A0", al), 1)
    bottom_scrim(im, w, h, .55, 145)
    vignette(im, 92)
    return im


def make_photo(kind, w, h):
    if kind == "beach":
        im = art_beach(w, h)
    elif kind == "coffee":
        im = art_coffee(w, h)
    elif kind == "night":
        im = art_night(w, h)
    elif kind == "mountain":
        im = art_mountain(w, h)
    else:
        im = art_street(w, h)
    return im


def photo_label(d, x, y_bottom, num, name, size=11.0, pad=8, h=None):
    h = h or size + 10
    nw = tw(num, size, True)
    sw = tw("  " + name, size, False)
    w = pad * 2 + nw + sw
    top = y_bottom - h
    rrect(d, (x, top, x + w, y_bottom), 6, fill=(5, 7, 9, 155),
          outline=(255, 255, 255, 32), width=1)
    cy = top + h / 2
    text(d, (x + pad, cy), num, size, WHITE, True, anchor="lm")
    text(d, (x + pad + nw, cy), "  " + name, size, C("#ECEFF4"), anchor="lm")


# --------------------------------------------------------------------------- #
#  Shared chrome bits
# --------------------------------------------------------------------------- #


def draw_window_controls(d, cy=80):
    # minimize
    line(d, 1231, cy, 1245, cy, C("#8F96A2"), 1.2)
    # maximize
    rrect(d, (1259, cy - 6, 1273, cy + 6), 1.5, outline=C("#8F96A2"), width=1.2)
    # close
    line(d, 1289, cy - 6, 1301, cy + 6, C("#D7DCE4"), 1.2)
    line(d, 1301, cy - 6, 1289, cy + 6, C("#D7DCE4"), 1.2)


def draw_app_icon(d, x, y, s=26):
    rrect(d, (x, y, x + s, y + s), s * .27, fill=ACCENT, outline=C("#45C9B4"), width=1)
    # tiny top sheen
    rrect(d, (x + 1, y + 1, x + s - 1, y + s * .42), s * .24, fill=C("#2AA391"))
    fw = s * .52
    rrect(d, (x + s * .20, y + s * .24, x + s * .20 + fw, y + s * .24 + fw),
          s * .10, outline=C("#E1FBF5"), width=1.6)
    rrect(d, (x + s * .43, y + s * .47, x + s * .43 + fw, y + s * .47 + fw),
          s * .10, outline=C("#7FE6D3"), width=1.6)


def draw_grid_icon(d, x, y, s=13, color=ACCENT_HI, merged=True):
    gap = max(1.2, s * .11)
    q = (s - gap) / 2.0
    rrect(d, (x, y, x + q, y + q), 1.6, fill=color if merged else None,
          outline=color, width=1)
    rrect(d, (x + q + gap, y, x + s, y + q), 1.6, fill=None,
          outline=color, width=1)
    rrect(d, (x, y + q + gap, x + q, y + s), 1.6, fill=None,
          outline=color, width=1)
    rrect(d, (x + q + gap, y + q + gap, x + s, y + s), 1.6, fill=None,
          outline=color, width=1)


def draw_chevron(d, x, y, direction="right", color=MUTED, size=4):
    if direction == "right":
        line(d, x - size * .5, y - size, x + size * .5, y, color, 1.2)
        line(d, x + size * .5, y, x - size * .5, y + size, color, 1.2)
    else:
        line(d, x + size * .5, y - size, x - size * .5, y, color, 1.2)
        line(d, x - size * .5, y, x + size * .5, y + size, color, 1.2)


def section_label(d, x, y, right, label):
    text(d, (x, y), label, 11, C("#9AA3B0"), bold=True)
    w = tw(label, 11, True)
    line(d, x + w + 9, y + 8, right, y + 8, C("#242A33"), 1)


# --------------------------------------------------------------------------- #
#  Main window
# --------------------------------------------------------------------------- #


def build_main():
    im = Image.new("RGB", (sc(MAIN_W), sc(MAIN_H)), BG0)
    d = new_draw(im)

    # ---- presentation backdrop -------------------------------------------
    base = vgrad(MAIN_W, MAIN_H, [(0.0, C("#08090C")), (0.52, C("#0A0C11")),
                                  (1.0, C("#07080B"))])
    im.paste(base, (0, 0))

    glow = Image.new("RGBA", im.size, (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse(pbox((-320, -360, 760, 620)), fill=C("#0E3A36", 175))
    gd.ellipse(pbox((860, 420, 1690, 1210)), fill=C("#241A34", 150))
    glow = glow.filter(ImageFilter.GaussianBlur(sc(130)))
    im.paste(glow, (0, 0), glow)

    # whisper of a dot grid
    d = new_draw(im)
    for xx in range(0, MAIN_W, 26):
        for yy in range(0, MAIN_H, 26):
            d.point((sc(xx), sc(yy)), fill=(255, 255, 255, 10))

    # ---- window shadow ----------------------------------------------------
    sh = Image.new("RGBA", im.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(sh)
    sd.rounded_rectangle(pbox((WIN_X + 4, WIN_Y + 24, WIN_X + WIN_W - 4,
                               WIN_Y + WIN_H + 22)),
                         radius=sc(24), fill=(0, 0, 0, 185))
    sh = sh.filter(ImageFilter.GaussianBlur(sc(34)))
    im.paste(sh, (0, 0), sh)

    # ---- window body / titlebar ------------------------------------------
    rrect(d, (WIN_X, WIN_Y, WIN_X + WIN_W, WIN_Y + WIN_H), 18,
          fill=WINDOW, outline=C("#2B3039"), width=1)
    rrect(d, (WIN_X, WIN_Y, WIN_X + WIN_W, WIN_Y + 40), 18, fill=TITLEBAR)
    rect(d, (WIN_X, WIN_Y + 22, WIN_X + WIN_W, WIN_Y + 40), TITLEBAR)
    line(d, WIN_X, WIN_Y + 40, WIN_X + WIN_W, WIN_Y + 40, C("#2B3039"), 1)
    line(d, WIN_X + 18, WIN_Y + 1, WIN_X + WIN_W - 18, WIN_Y + 1,
         (255, 255, 255, 18), 1)

    draw_app_icon(d, 146, 67, 26)
    text(d, (182, 80), "轻拼", 14, TEXT, True, anchor="lm")
    w = tw("轻拼", 14, True)
    text(d, (182 + w + 8, 80), "· 图片拼接", 13, C("#AAB2BE"), anchor="lm")
    draw_window_controls(d, 80)

    # ======================================================================
    #  Left column — preview
    # ======================================================================
    card = (146, 116, 988, 646)
    rrect(d, card, 14, fill=PREVIEW, outline=BORDER, width=1)

    chk = checker(834, 522, 14, CHECK_L, CHECK_D)
    chk.putalpha(rounded_mask(834, 522, 10))
    im.paste(chk, (sc(150), sc(120)), chk)
    d = new_draw(im)
    rrect(d, (150, 120, 984, 642), 10, outline=(255, 255, 255, 14), width=1)

    # grid geometry: 3 x 3, 140 cells, 12 gaps, 16 outer margin -> 476 canvas
    xs = [345, 497, 649, 789]
    ys = [159, 311, 463, 603]

    # tile shadows (single blurred pass)
    shadow = Image.new("RGBA", im.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)

    def sh_box(box, r):
        sd.rounded_rectangle(pbox((box[0], box[1] + 4, box[2], box[3] + 4)),
                             radius=sc(r), fill=(0, 0, 0, 120))

    sh_box((345, 159, 637, 451), 12)
    for b in [(649, 159, 789, 299), (649, 311, 789, 451),
              (345, 463, 485, 603), (497, 463, 637, 603)]:
        sh_box(b, 9)
    shadow = shadow.filter(ImageFilter.GaussianBlur(sc(4)))
    im.paste(shadow, (0, 0), shadow)
    d = new_draw(im)

    # five generated photographs
    p1 = make_photo("beach", 292, 292)
    p2 = make_photo("coffee", 140, 140)
    p3 = make_photo("night", 140, 140)
    p4 = make_photo("mountain", 140, 140)
    p5 = make_photo("street", 140, 140)

    paste_rounded(im, p1, 345, 159, 292, 292, 12)
    paste_rounded(im, p2, 649, 159, 140, 140, 9)
    paste_rounded(im, p3, 649, 311, 140, 140, 9)
    paste_rounded(im, p4, 345, 463, 140, 140, 9)
    paste_rounded(im, p5, 497, 463, 140, 140, 9)
    d = new_draw(im)

    # faint inner keyline on each tile
    for b, r in [((345, 159, 637, 451), 12), ((649, 159, 789, 299), 9),
                 ((649, 311, 789, 451), 9), ((345, 463, 485, 603), 9),
                 ((497, 463, 637, 603), 9)]:
        rrect(d, b, r, outline=(255, 255, 255, 30), width=1)

    # ---- dashed grid guides ----------------------------------------------
    for i, x in enumerate(xs):
        col = (255, 255, 255, 105) if i in (0, 3) else (255, 255, 255, 52)
        dashed_line(d, x, ys[0], x, ys[-1], col, 1, 4.5, 6)
    for i, y in enumerate(ys):
        col = (255, 255, 255, 105) if i in (0, 3) else (255, 255, 255, 52)
        dashed_line(d, xs[0], y, xs[-1], y, col, 1, 4.5, 6)

    # ---- photo labels -----------------------------------------------------
    photo_label(d, 357, 439, "01", "海边", 12.5, 10, 28)
    photo_label(d, 657, 291, "02", "咖啡", 11.0, 7, 22)
    photo_label(d, 657, 443, "03", "夜景", 11.0, 7, 22)
    photo_label(d, 353, 595, "04", "山野", 11.0, 7, 22)
    photo_label(d, 505, 595, "05", "老街", 11.0, 7, 22)

    # ---- preview overlay chips -------------------------------------------
    chip(d, 162, 134, "网格拼接 · 5 张", 11.5, C("#E4E9F1"),
         bg=(6, 8, 11, 205), border=(255, 255, 255, 32), padx=11, h=30,
         dot=ACCENT_SOFT)
    out_label = "输出 2,756 × 2,756 px"
    ow = chip_width(out_label, 11.5, 11, False, False)
    chip(d, 972 - ow, 134, out_label, 11.5, C("#A9EDE1"),
         bg=(6, 8, 11, 205), border=(47, 191, 166, 70), padx=11, h=30)

    # bottom-left interaction hint
    hint = "拖动图片到另一张图上：两张图片对调位置"
    chip(d, 162, 609, hint, 11, C("#C9D2DE"),
         bg=(6, 8, 11, 205), border=(255, 255, 255, 26), padx=10, h=26)

    # one small annotation pointing at the guide line
    ann = "虚线 = 网格边界（仅预览）"
    aw = chip_width(ann, 11, 10, False, False)
    ax = 972 - aw
    rrect(d, (ax, 324, ax + aw, 352), 7, fill=(8, 26, 23, 220),
          outline=(47, 191, 166, 95), width=1)
    text(d, (ax + 10, 338), ann, 11, C("#9FE8DC"), anchor="lm")
    dashed_line(d, ax - 2, 338, 793, 338, (111, 226, 205, 170), 1, 3, 3)
    d.ellipse([sc(790), sc(335), sc(796), sc(341)], fill=(111, 226, 205, 220))

    # ======================================================================
    #  Caption + thumbnail strip
    # ======================================================================
    d.ellipse([sc(147), sc(660), sc(153), sc(666)], fill=ACCENT_SOFT)
    rich(d, 162, 663, [
        ("共 ", MUTED, False), ("5", C("#E7EBF1"), True), (" 张图片", MUTED, False),
        ("   ·   ", C("#3C434E"), False), ("网格拼接 ", MUTED, False),
        ("3×3", C("#E7EBF1"), True), ("   ·   ", C("#3C434E"), False),
        ("容量 ", MUTED, False), ("6", C("#E7EBF1"), True),
        ("   ·   ", C("#3C434E"), False), ("合并 ", MUTED, False),
        ("1 处", C("#8FE4D2"), True),
    ], 11, anchor="lm")

    strip_y = 678
    thumb = 92
    gap = 10
    photos = [p1, p2, p3, p4, p5]
    names = ["海边", "咖啡", "夜景", "山野", "老街"]
    for i in range(5):
        tx = 146 + i * (thumb + gap)
        rrect(d, (tx, strip_y, tx + thumb, strip_y + thumb), 10,
              fill=C("#1A1E25"), outline=BORDER_LINE, width=1)
        paste_rounded(im, photos[i], tx + 4, strip_y + 4, 84, 84, 7)
        d = new_draw(im)
        # number badge
        rrect(d, (tx + 6, strip_y + 6, tx + 26, strip_y + 23), 5,
              fill=(7, 9, 12, 225), outline=(255, 255, 255, 38), width=1)
        text(d, (tx + 16, strip_y + 14.5), str(i + 1), 10, WHITE, True, anchor="mm")

    # add tile
    add_x = 146 + 5 * (thumb + gap)
    rrect(d, (add_x, strip_y, add_x + thumb, strip_y + thumb), 10,
          fill=C("#141821"), outline=None)
    dashed_rrect(d, (add_x + .5, strip_y + .5, add_x + thumb - .5, strip_y + thumb - .5),
                 10, (92, 104, 120, 210), 1, 5.5, 5.5)
    line(d, add_x + thumb / 2 - 8, strip_y + 38, add_x + thumb / 2 + 8, strip_y + 38,
         ACCENT_HI, 1.8)
    line(d, add_x + thumb / 2, strip_y + 30, add_x + thumb / 2, strip_y + 46,
         ACCENT_HI, 1.8)
    text(d, (add_x + thumb / 2, strip_y + 64), "添加图片", 11, C("#8792A2"),
         anchor="ma")

    # right-side sort buttons
    by = strip_y + (thumb - 34) / 2
    rrect(d, (830, by, 922, by + 34), 8, fill=CONTROL, outline=BORDER_LINE, width=1)
    text(d, (876, by + 17), "按名称排序", 11, C("#AAB3C1"), anchor="mm")
    rrect(d, (930, by, 988, by + 34), 8, fill=CONTROL, outline=BORDER_LINE, width=1)
    text(d, (959, by + 17), "清空", 11, C("#AAB3C1"), anchor="mm")

    # bottom assist line
    text(d, (146, 790), "拖动缩略图可排序 · Delete 删除 · Ctrl+O 添加图片",
         11, DIM, anchor="la")
    text(d, (988, 790), "已载入 5 张图片", 11, DIM, anchor="ra")

    # ======================================================================
    #  Right side panel
    # ======================================================================
    px, py, pw, ph = 1002, 116, 292, 710
    rrect(d, (px, py, px + pw, py + ph), 14, fill=SIDEBAR, outline=C("#2A3039"),
          width=1)
    line(d, px + 18, py + 1, px + pw - 18, py + 1, (255, 255, 255, 14), 1)
    ix, ir = px + 16, px + pw - 16
    iw = ir - ix

    text(d, (ix, py + 14), "拼接设置", 17, TEXT, True)
    rrect(d, (ir - 24, py + 12, ir, py + 36), 7, fill=C("#20252D"),
          outline=C("#39414D"), width=1)
    text(d, (ir - 12, py + 24), "?", 12.5, C("#9AA4B2"), anchor="mm")
    text(d, (ix, py + 40), "参数修改会实时刷新预览", 11, C("#7E8796"))

    def seg_control(y, labels, selected, h=32, font_size=11.4):
        rrect(d, (ix, y, ir, y + h), 9, fill=CONTROL_DEEP, outline=BORDER_SOFT,
              width=1)
        n = len(labels)
        inner = 4
        seg_w = (iw - inner * 2 - (n - 1) * 2) / n
        for i, lab in enumerate(labels):
            sx = ix + inner + i * (seg_w + 2)
            if i == selected:
                rrect(d, (sx, y + 2, sx + seg_w, y + h - 2), 7, fill=ACCENT,
                      outline=C("#3CC4AE"), width=1)
                line(d, sx + 8, y + 3, sx + seg_w - 8, y + 3,
                     (255, 255, 255, 38), 1)
                text(d, (sx + seg_w / 2, y + h / 2), lab, font_size,
                     C("#EFFFFB"), True, anchor="mm")
            else:
                text(d, (sx + seg_w / 2, y + h / 2), lab, font_size,
                     C("#A6B0BF"), anchor="mm")

    def stepper(x, y, value, w=84, h=30):
        rrect(d, (x, y, x + w, y + h), 7, fill=CONTROL_DEEP,
              outline=C("#333A45"), width=1)
        cy = y + h / 2
        line(d, x + 11, cy, x + 19, cy, C("#8993A2"), 1.2)
        line(d, x + w / 2 - 4, cy, x + w / 2 + 4, cy, C("#8993A2"), 1.2)
        line(d, x + w - 19, cy, x + w - 11, cy, C("#8993A2"), 1.2)
        line(d, x + w - 15, cy - 4, x + w - 15, cy + 4, C("#8993A2"), 1.2)
        text(d, (x + w / 2, cy), str(value), 11.5, C("#EDF1F6"), anchor="mm")

    # 拼接方式
    section_label(d, ix, py + 68, ir, "拼接方式")
    seg_control(py + 87, ["横向", "纵向", "网格"], 2)

    # 网格
    section_label(d, ix, py + 134, ir, "网格")
    g1x, g2x = ix, ix + 136
    label_w = 40
    for yy, l1, v1, l2, v2 in [
            (py + 153, "行", 3, "列", 3),
            (py + 189, "行间距", 12, "列间距", 12)]:
        text(d, (g1x, yy + 15), l1, 11, C("#C3CAD5"), anchor="lm")
        text(d, (g2x, yy + 15), l2, 11, C("#C3CAD5"), anchor="lm")
        stepper(g1x + label_w, yy, v1)
        stepper(g2x + label_w, yy, v2)
    text(d, (g1x, py + 225 + 15), "外边距", 11, C("#C3CAD5"), anchor="lm")
    stepper(g1x + label_w, py + 225, 16)
    text(d, (g2x, py + 240), "四边等距", 11, C("#5F6875"), anchor="lm")

    rrect(d, (ix, py + 263, ir, py + 295), 8, fill=C("#20252D"),
          outline=C("#39414D"), width=1)
    draw_grid_icon(d, ix + 13, py + 272, 13, ACCENT_HI, True)
    text(d, (ix + 34, py + 279), "合并单元格…", 11.5, C("#C9D1DC"), anchor="lm")
    draw_chevron(d, ir - 15, py + 279, "right", C("#6E7888"), 4)

    # 背景
    section_label(d, ix, py + 309, ir, "背景")
    sw_y, sw_w, sw_h = py + 328, 57, 32
    sw_gap = (iw - 4 * sw_w) / 3.0
    labels = ["透明", "白色", "黑色", "自定义"]
    for i in range(4):
        sx = ix + i * (sw_w + sw_gap)
        if i == 0:
            ck = checker(sw_w - 4, sw_h - 4, 7, C("#D9DDE3"), C("#F7F8FA"))
            im.paste(ck, (sc(sx + 2), sc(sw_y + 2)), ck)
            d = new_draw(im)
            rrect(d, (sx, sw_y, sx + sw_w, sw_y + sw_h), 6,
                  outline=ACCENT_SOFT if i == 0 else C("#3A404B"),
                  width=2 if i == 0 else 1)
        elif i == 1:
            rrect(d, (sx, sw_y, sx + sw_w, sw_y + sw_h), 6, fill=C("#F5F7FA"),
                  outline=C("#3A404B"), width=1)
        elif i == 2:
            rrect(d, (sx, sw_y, sx + sw_w, sw_y + sw_h), 6, fill=C("#101216"),
                  outline=C("#46505E"), width=1)
        else:
            g = vgrad(sw_w, sw_h, [(0, C("#818B99")), (0.52, C("#5B6472")),
                                   (1, C("#363E4A"))])
            paste_rounded(im, g, sx, sw_y, sw_w, sw_h, 6)
            d = new_draw(im)
            rrect(d, (sx, sw_y, sx + sw_w, sw_y + sw_h), 6,
                  outline=C("#3A404B"), width=1)
        if i == 0:
            text(d, (sx + sw_w / 2, sw_y + 39), labels[i], 11,
                 C("#8FE4D2"), anchor="ma")
        else:
            text(d, (sx + sw_w / 2, sw_y + 39), labels[i], 11,
                 C("#7E8796"), anchor="ma")

    # opacity slider
    text(d, (ix, py + 389), "透明度", 11, C("#C3CAD5"))
    text(d, (ir, py + 389), "0%", 11, C("#BDC2CB"), anchor="ra")
    line(d, ix, py + 415, ir, py + 415, C("#2C323C"), 4)
    kx, ky = ix + 7, py + 415
    d.ellipse([sc(kx - 10), sc(ky - 10), sc(kx + 10), sc(ky + 10)],
              fill=(47, 191, 166, 36))
    d.ellipse([sc(kx - 6), sc(ky - 6), sc(kx + 6), sc(ky + 6)],
              fill=ACCENT_HI, outline=C("#0B0F13"), width=max(1, sc(1.5)))

    # 输出尺寸
    section_label(d, ix, py + 442, ir, "输出尺寸")
    seg_control(py + 461, ["自动", "指定宽度", "指定高度"], 0, h=32, font_size=11.0)
    text(d, (ix, py + 497), "自动：取图片中间尺寸", 11, C("#5F6875"))

    # 输出格式
    section_label(d, ix, py + 526, ir, "输出格式")
    seg_control(py + 545, ["PNG（透明）", "JPG"], 0, h=32, font_size=11.0)
    text(d, (ix, py + 581), "PNG 完整保留透明通道", 11, C("#5F6875"))

    # footer
    rrect(d, (ix, py + 618, ir, py + 658), 10, fill=ACCENT,
          outline=C("#3CC4AE"), width=1)
    line(d, ix + 9, py + 619, ir - 9, py + 619, (255, 255, 255, 42), 1)
    text(d, ((ix + ir) / 2, py + 638), "保存图片  Ctrl+S", 12.5, C("#F0FFFB"),
         True, anchor="mm")
    rrect(d, (ix, py + 666, ir, py + 698), 8, outline=C("#39414D"), width=1)
    text(d, ((ix + ir) / 2, py + 682), "另存为…  Ctrl+Shift+S", 11.2,
         C("#98A2B1"), anchor="mm")

    # final window keyline / canvas vignette
    rrect(d, (WIN_X, WIN_Y, WIN_X + WIN_W, WIN_Y + WIN_H), 18,
          outline=C("#323843"), width=1)
    vignette(im, 70)
    return im


# --------------------------------------------------------------------------- #
#  Merge editor dialog
# --------------------------------------------------------------------------- #


def build_merge(main_hi):
    W, H = MERGE_W, MERGE_H
    target = (sc(W), sc(H))
    im = Image.new("RGB", target, (5, 6, 9))

    # ---- blurred main window backdrop ------------------------------------
    back = main_hi.convert("RGB")
    scale = max(target[0] / back.width, target[1] / back.height)
    rw, rh = int(math.ceil(back.width * scale)), int(math.ceil(back.height * scale))
    back = back.resize((rw, rh), Image.LANCZOS)
    left = (rw - target[0]) // 2
    top = (rh - target[1]) // 2
    back = back.crop((left, top, left + target[0], top + target[1])).convert("RGB")
    back = back.filter(ImageFilter.GaussianBlur(sc(8.0)))
    im.paste(back, (0, 0))
    dim = Image.new("RGBA", target, (4, 5, 8, 118))
    im.paste(dim, (0, 0), dim)

    glow = Image.new("RGBA", target, (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse(pbox((160, 90, 700, 570)), fill=(31, 138, 122, 40))
    glow = glow.filter(ImageFilter.GaussianBlur(sc(90)))
    im.paste(glow, (0, 0), glow)
    vignette(im, 120)

    d = new_draw(im)

    # ---- dialog shadow / card --------------------------------------------
    sh = Image.new("RGBA", target, (0, 0, 0, 0))
    sd = ImageDraw.Draw(sh)
    sd.rounded_rectangle(pbox((30, 52, 830, 592)), radius=sc(16), fill=(0, 0, 0, 210))
    sh = sh.filter(ImageFilter.GaussianBlur(sc(22)))
    im.paste(sh, (0, 0), sh)
    d = new_draw(im)

    card = (30, 40, 830, 580)
    cg = vgrad(800, 540, [(0.0, C("#1C212A")), (0.48, C("#1A1E26")),
                          (1.0, C("#171B22"))])
    paste_rounded(im, cg, 30, 40, 800, 540, 14)
    d = new_draw(im)
    rrect(d, card, 14, outline=C("#303845"), width=1)
    line(d, 54, 41, 806, 41, (255, 255, 255, 16), 1)

    # header
    icon = (54, 62, 92, 100)
    rrect(d, icon, 10, fill=TEAL_TINT, outline=C("#2B6E62"), width=1)
    # mini grid glyph inside header icon
    rrect(d, (63, 71, 75, 83), 2.2, fill=ACCENT_SOFT, outline=None)
    rrect(d, (79, 71, 85, 77), 2.0, outline=ACCENT_HI, width=1)
    rrect(d, (63, 85, 69, 91), 2.0, outline=ACCENT_HI, width=1)
    rrect(d, (73, 81, 83, 91), 2.0, outline=ACCENT_HI, width=1)

    text(d, (104, 60), "编辑网格合并", 20, TEXT, True)
    text(d, (104, 92), "拖选相邻的矩形区域，合并后的大格只放 1 张图", 11.5,
         C("#8B95A4"))
    rrect(d, (778, 64, 806, 92), 7, fill=C("#20252D"), outline=C("#39414D"),
          width=1)
    line(d, 786, 72, 798, 84, C("#9AA4B2"), 1.2)
    line(d, 798, 72, 786, 84, C("#9AA4B2"), 1.2)
    line(d, 54, 116, 806, 116, (255, 255, 255, 26), 1)

    # ---- matrix board -----------------------------------------------------
    bx0, by0, bx1, by1 = 54, 140, 370, 456
    rrect(d, (bx0, by0, bx1, by1), 12, fill=C("#12161C"),
          outline=C("#2A323D"), width=1)
    line(d, bx0 + 16, by0 + 1, bx1 - 16, by0 + 1, (255, 255, 255, 12), 1)

    cx = [80, 172, 264]
    cy = [164, 256, 348]
    cell = 80

    # free cells
    free = {(0, 2): "02", (1, 2): "03", (2, 0): "04",
            (2, 1): "05", (2, 2): "06"}
    selected = {(2, 1), (2, 2)}
    for r in range(3):
        for c in range(3):
            if (r, c) not in free:
                continue
            x, y = cx[c], cy[r]
            sel = (r, c) in selected
            rrect(d, (x, y, x + cell, y + cell), 9,
                  fill=C("#1E4A42") if sel else C("#1A2029"),
                  outline=C("#3FBFA7") if sel else C("#313A46"), width=1)
            line(d, x + 10, y + 1, x + cell - 10, y + 1,
                 (255, 255, 255, 16), 1)
            text(d, (x + cell / 2, y + cell / 2), free[(r, c)], 14,
                 C("#A9EEE2") if sel else C("#5D6878"), True, anchor="mm")

    # existing 2x2 merged block
    mb = (cx[0], cy[0], cx[0] + 172, cy[0] + 172)
    mg = vgrad(172, 172, [(0.0, C("#1A463F")), (1.0, C("#0F2A26"))])
    paste_rounded(im, mg, mb[0], mb[1], 172, 172, 10)
    d = new_draw(im)
    rrect(d, mb, 10, outline=ACCENT_SOFT, width=1.6)
    dashed_rrect(d, (mb[0] + 4.5, mb[1] + 4.5, mb[2] - 4.5, mb[3] - 4.5),
                 7, (111, 226, 205, 95), 1, 4, 4)
    text(d, ((mb[0] + mb[2]) / 2, mb[1] + 78), "大格 1", 14,
         C("#E3FBF6"), True, anchor="mm")
    text(d, ((mb[0] + mb[2]) / 2, mb[1] + 99), "2 × 2 · 已合并", 11,
         C("#83D3C4"), anchor="mm")

    # drag-selection over the two free cells on the last row
    sel_box = (cx[1], cy[2], cx[2] + cell, cy[2] + cell)
    rrect(d, sel_box, 9, fill=(47, 191, 166, 42), outline=None)
    rrect(d, sel_box, 9, outline=ACCENT_HI, width=2)
    # corner handles
    for hx, hy in [(sel_box[0], sel_box[1]), (sel_box[2], sel_box[1]),
                   (sel_box[0], sel_box[3]), (sel_box[2], sel_box[3])]:
        rrect(d, (hx - 3.5, hy - 3.5, hx + 3.5, hy + 3.5), 1.5,
              fill=C("#DDFBF5"), outline=C("#0C3831"), width=1)
    # 2 x 1 badge
    badge_w, badge_h = 50, 21
    bxx, byy = sel_box[2] - 6 - badge_w, sel_box[1] + 6
    rrect(d, (bxx, byy, bxx + badge_w, byy + badge_h), 6,
          fill=(8, 30, 26, 235), outline=C("#3FBFA7"), width=1)
    text(d, (bxx + badge_w / 2, byy + badge_h / 2), "2 × 1", 11,
         C("#9FE8DC"), True, anchor="mm")

    # mouse cursor at the selection's bottom-right
    cur = [(330, 408), (330, 425), (334.5, 421), (338, 429),
           (341, 427.5), (337.5, 419.5), (343, 417.5)]
    d.polygon([(sc(x), sc(y)) for x, y in cur], fill=WHITE,
              outline=C("#0A1512"))
    line(d, 330, 408, 330, 425, C("#0A1512"), 1)
    line(d, 330, 408, 343, 417.5, C("#0A1512"), 1)

    # grid board caption
    text(d, (54, 464), "网格 3 × 3 · 拖动框选相邻格子", 11, C("#687383"))

    # ---- right column -----------------------------------------------------
    rx0, rx1 = 398, 806
    # instruction card
    rrect(d, (rx0, 140, rx1, 234), 12, fill=C("#141922"),
          outline=C("#29313C"), width=1)
    rrect(d, (414, 154, 446, 186), 8, fill=C("#12312C"),
          outline=C("#2B6E62"), width=1)
    # drag glyph
    dashed_rrect(d, (421, 162, 439, 177), 2, (111, 226, 205, 220), 1, 2.5, 2.5)
    line(d, 430, 166, 430, 173, C("#DDFBF5"), 1)
    line(d, 428, 171, 430, 174, C("#DDFBF5"), 1)
    line(d, 432, 171, 430, 174, C("#DDFBF5"), 1)

    text(d, (456, 152), "按住左键框选相邻矩形区域", 12.5, C("#D7DEE8"), True)
    text(d, (456, 176), "从一格拖到另一格，松开后点击「合并所选」生效。", 11,
         C("#8B96A5"))
    text(d, (456, 194), "已合并的大格需先拆分，才能参与新的框选。", 11,
         C("#8B96A5"))

    text(d, (rx0, 250), "当前拖选", 11, C("#8E98A7"), bold=True)
    # selection card
    rrect(d, (rx0, 268, rx1, 358), 12, fill=C("#10231F"),
          outline=C("#2B6E62"), width=1)
    rrect(d, (rx0, 268, rx0 + 3, 358), 1.5, fill=ACCENT_SOFT)
    text(d, (rx0 + 18, 282), "第 3 行 · 第 2–3 列", 13.5, C("#EAF8F4"), True)
    rrect(d, (rx1 - 64, 280, rx1 - 18, 302), 6, fill=ACCENT,
          outline=C("#3CC4AE"), width=1)
    text(d, ((rx1 - 64 + rx1 - 18) / 2, 291), "2 × 1", 11, C("#F0FFFB"),
         True, anchor="mm")
    text(d, (rx0 + 18, 312), "覆盖 2 个格子 · 合并后容量 6 → 5", 11,
         C("#9BB8B1"))
    text(d, (rx0 + 18, 330), "松开鼠标后生成 1 个新的大格", 11,
         C("#6F918A"))

    # legend (glyphs drawn, not unicode)
    ly = 380
    d.ellipse([sc(rx0), sc(ly - 5), sc(rx0 + 10), sc(ly + 5)],
              outline=C("#7B8596"), width=max(1, sc(1.2)))
    text(d, (rx0 + 16, ly), "单个格子", 11.2, C("#98A3B2"), anchor="lm")
    rrect(d, (rx0 + 120, ly - 5, rx0 + 131, ly + 6), 1.5, fill=ACCENT_SOFT)
    text(d, (rx0 + 137, ly), "已合并区域", 11.2, C("#98A3B2"), anchor="lm")
    rrect(d, (rx0 + 246, ly - 5, rx0 + 259, ly + 5), 1.5,
          fill=(47, 191, 166, 45), outline=ACCENT_HI, width=1.2)
    text(d, (rx0 + 265, ly), "拖选区域", 11.2, C("#98A3B2"), anchor="lm")

    # capacity
    rich(d, rx0, 406, [
        ("当前容量：", C("#8E98A7"), False), ("6", C("#8FE4D2"), True),
        (" 个格子  ·  合并 ", C("#8E98A7"), False), ("1", C("#E7EBF1"), True),
        (" 处  ·  图片 ", C("#8E98A7"), False), ("5", C("#E7EBF1"), True),
        (" 张", C("#8E98A7"), False),
    ], 11.5, anchor="lm")

    text(d, (rx0, 432), "提示：合并后的大格只占 1 个图片位，容量会减少。", 11,
         C("#646F7E"), anchor="lm")
    text(d, (rx1, 462), "Esc 取消  ·  Enter 确定", 11, C("#5D6877"),
         anchor="ra")

    # footer
    line(d, 54, 486, 806, 486, (255, 255, 255, 26), 1)
    fy = 506
    rrect(d, (54, fy, 170, fy + 38), 9, fill=ACCENT, outline=C("#3CC4AE"),
          width=1)
    line(d, 63, fy + 1, 161, fy + 1, (255, 255, 255, 42), 1)
    text(d, (112, fy + 19), "合并所选", 12.2, C("#F0FFFB"), True, anchor="mm")

    rrect(d, (180, fy, 286, fy + 38), 9, fill=C("#242A33"),
          outline=C("#3A424E"), width=1)
    text(d, (233, fy + 19), "拆分所选", 11.4, C("#C8D0DB"), anchor="mm")

    rrect(d, (296, fy, 430, fy + 38), 9, fill=None, outline=C("#2A303A"),
          width=1)
    text(d, (363, fy + 19), "清空所有合并", 11.4, C("#8F99A8"), anchor="mm")

    rrect(d, (610, fy, 698, fy + 38), 9, fill=C("#20252D"),
          outline=C("#39414D"), width=1)
    text(d, (654, fy + 19), "取消", 11.4, C("#AAB3C1"), anchor="mm")

    rrect(d, (708, fy, 806, fy + 38), 9, fill=C("#162B28"),
          outline=ACCENT_SOFT, width=1.5)
    text(d, (757, fy + 19), "确定", 12.2, C("#9FE8DC"), True, anchor="mm")

    # final border / vignette
    rrect(d, card, 14, outline=C("#323A47"), width=1)
    vignette(im, 55)
    return im


# --------------------------------------------------------------------------- #
#  Entry point
# --------------------------------------------------------------------------- #


def save_downscaled(im, w, h, path):
    out = im.convert("RGB").resize((w, h), Image.LANCZOS)
    out.save(path, "PNG", optimize=True)
    return out


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    main_hi = build_main()
    main_path = os.path.join(OUT_DIR, "ui-mockup-grid-main.png")
    main_out = save_downscaled(main_hi, MAIN_W, MAIN_H, main_path)

    merge_hi = build_merge(main_hi)
    merge_path = os.path.join(OUT_DIR, "ui-mockup-grid-merge.png")
    merge_out = save_downscaled(merge_hi, MERGE_W, MERGE_H, merge_path)

    print(main_path, main_out.size)
    print(merge_path, merge_out.size)


if __name__ == "__main__":
    main()
