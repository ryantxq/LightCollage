# -*- coding: utf-8 -*-
"""Generate a UI mockup for the LightCollage design confirmation."""
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import os

S = 2  # supersampling factor
W, H = 1440, 900
img = Image.new("RGB", (W * S, H * S), "#0d0f13")
d = ImageDraw.Draw(img)

FONT_R = "C:/Windows/Fonts/msyh.ttc"
FONT_B = "C:/Windows/Fonts/msyhbd.ttc"
FONT_MONO = "C:/Windows/Fonts/consola.ttf"

def f(size, bold=False):
    return ImageFont.truetype(FONT_B if bold else FONT_R, int(size * S))

def rrect(box, radius, fill=None, outline=None, width=1):
    d.rounded_rectangle([int(v * S) for v in box], radius=int(radius * S), fill=fill,
                        outline=outline, width=int(width * S))

def text(x, y, s, size, fill="#e8ecf4", bold=False, anchor="la"):
    d.text((x * S, y * S), s, font=f(size, bold), fill=fill, anchor=anchor)

def line(x1, y1, x2, y2, fill, width=1):
    d.line([int(x1 * S), int(y1 * S), int(x2 * S), int(y2 * S)], fill=fill, width=int(width * S))

def checker(size, c1, c2, w, h):
    im = Image.new("RGB", (w * S, h * S), c1)
    dd = ImageDraw.Draw(im)
    cell = size * S
    for yy in range(0, h * S, cell):
        for xx in range(0, w * S, cell):
            if ((xx // cell) + (yy // cell)) % 2:
                dd.rectangle([xx, yy, xx + cell - 1, yy + cell - 1], fill=c2)
    return im

# background glow
glow = Image.new("RGB", (W * S, H * S), "#0d0f13")
gd = ImageDraw.Draw(glow)
gd.ellipse([-300 * S, -420 * S, 900 * S, 500 * S], fill="#153b3a")
gd.ellipse([950 * S, 300 * S, 1800 * S, 1200 * S], fill="#241a34")
glow = glow.filter(ImageFilter.GaussianBlur(120 * S))
img.paste(glow, (0, 0))
d = ImageDraw.Draw(img)

# window shadow
shadow = Image.new("RGBA", (W * S, H * S), (0, 0, 0, 0))
sd = ImageDraw.Draw(shadow)
sd.rounded_rectangle([60 * S, 50 * S, 1380 * S, 870 * S], radius=22 * S, fill=(0, 0, 0, 180))
shadow = shadow.filter(ImageFilter.GaussianBlur(30 * S))
img.paste(shadow, (0, 0), shadow)

# window
rrect((60, 42, 1380, 858), 18, fill="#171a21", outline="#2b313c", width=1)
rrect((60, 42, 1380, 96), 18, fill="#1b1f27")
d.rectangle([60 * S, 78 * S, 1380 * S, 96 * S], fill="#1b1f27")
line(60, 96, 1380, 96, "#2b313c")

# app icon
rrect((82, 58, 112, 88), 8, fill="#1f8a7a")
# overlapping frames
d.rounded_rectangle([89 * S, 66 * S, 104 * S, 81 * S], radius=3 * S, outline="#bdf5ea", width=2 * S)
d.rounded_rectangle([94 * S, 71 * S, 109 * S, 86 * S], radius=3 * S, outline="#67dcc7", width=2 * S)

text(124, 59, "轻拼", 19, "#f2f5fa", bold=True)
text(177, 63, "LightCollage", 12, "#7f8898")
text(272, 63, "图片拼接工具", 12, "#5f6878")

# window controls
for cx, glyph in [(1332, "—"), (1352, "□"), (1372, "×")]:
    text(cx, 59, glyph, 14, "#7c8595" if glyph != "×" else "#c5cbd6", anchor="ma")

# ---------------------------------------------------------------- left side
# preview card
rrect((84, 112, 1030, 618), 14, fill="#0f1218", outline="#2a303a")
check = checker(20, "#232833", "#1a1e26", 930, 490)
mask = Image.new("L", (930 * S, 490 * S), 0)
md = ImageDraw.Draw(mask)
md.rounded_rectangle([4 * S, 4 * S, 926 * S, 486 * S], radius=11 * S, fill=255)
img.paste(check, (88 * S, 116 * S), mask)
d = ImageDraw.Draw(img)

# collage inside preview: 3 sample images with transparent gaps
base_x, base_y = 170, 232
imgs = [
    (250, 330, "#1f6f8b", "#46c7e8", "01  海边.jpg"),
    (300, 330, "#8a4b2a", "#f0a35e", "02  咖啡.jpg"),
    (200, 330, "#44357a", "#a58bf2", "03  夜景.jpg"),
]
gap = 12
total_w = 235 + 285 + 185 + gap * 2
x = base_x
for i, (iw, ih, c1, c2, label) in enumerate(imgs):
    # shadow
    sh = Image.new("RGBA", (W * S, H * S), (0, 0, 0, 0))
    shd = ImageDraw.Draw(sh)
    shd.rounded_rectangle([(x + 4) * S, (base_y + 5) * S, (x + iw + 4) * S, (base_y + ih + 5) * S],
                          radius=9 * S, fill=(0, 0, 0, 130))
    sh = sh.filter(ImageFilter.GaussianBlur(8 * S))
    img.paste(sh, (0, 0), sh)
    d = ImageDraw.Draw(img)
    # gradient photo
    grad = Image.new("RGB", (iw * S, ih * S), c1)
    gd2 = ImageDraw.Draw(grad)
    for yy in range(ih * S):
        t = yy / max(1, ih * S - 1)
        col = tuple(int(a + (b - a) * t) for a, b in zip(
            Image.new("RGB", (1, 1), c1).getpixel((0, 0)),
            Image.new("RGB", (1, 1), c2).getpixel((0, 0))))
        gd2.line([(0, yy), (iw * S, yy)], fill=col)
    # decorative circles
    gd2.ellipse([-20 * S, 220 * S, 120 * S, 360 * S], fill=None, outline="#ffffff55", width=2 * S)
    gd2.ellipse([150 * S, -30 * S, 280 * S, 100 * S], fill=None, outline="#ffffff33", width=2 * S)
    m = Image.new("L", (iw * S, ih * S), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, iw * S - 1, ih * S - 1], radius=8 * S, fill=255)
    img.paste(grad, (x * S, base_y * S), m)
    d = ImageDraw.Draw(img)
    # label chip
    rrect((x + 10, base_y + ih - 34, x + iw - 10, base_y + ih - 10), 6, fill="#00000099")
    text(x + 18, base_y + ih - 29, label, 12, "#f3f6fb")
    x += iw + gap

# preview overlay chips
rrect((104, 132, 282, 164), 9, fill="#000000b0")
text(116, 139, "横向拼接 · 3 张", 12, "#dce3ee")
rrect((816, 132, 1008, 164), 9, fill="#000000b0")
text(828, 139, "输出 2,376 × 1,100 px", 12, "#9fe8dc")

# dimension helper text under preview
text(100, 632, "拖拽缩略图排序 · Delete 删除 · 支持拖入文件", 11.5, "#687183")

# thumbnails
thumb_y = 660
thumb_labels = [("01", "#1f6f8b"), ("02", "#8a4b2a"), ("03", "#44357a")]
tx = 84
for idx, (lab, col) in enumerate(thumb_labels):
    rrect((tx, thumb_y, tx + 96, thumb_y + 96), 10, fill="#1e242e", outline="#3a4352")
    rrect((tx + 8, thumb_y + 8, tx + 88, thumb_y + 78), 6, fill=col)
    text(tx + 8, thumb_y + 80, lab, 10, "#8d97a7")
    # number badge
    rrect((tx + 74, thumb_y + 6, tx + 90, thumb_y + 22), 5, fill="#0c0e12")
    text(tx + 82, thumb_y + 9, str(idx + 1), 9, "#dce3ee", anchor="ma")
    tx += 108

# add thumbnail
rrect((tx, thumb_y, tx + 96, thumb_y + 96), 10, fill="#141922", outline="#39424f")
d.dashed_rectangle = None
# dashed border manually
for xx in range(tx + 6, tx + 90, 8):
    line(xx, thumb_y + 6, xx + 4, thumb_y + 6, "#4b5666")
    line(xx, thumb_y + 90, xx + 4, thumb_y + 90, "#4b5666")
for yy in range(thumb_y + 6, thumb_y + 90, 8):
    line(tx + 6, yy, tx + 6, yy + 4, "#4b5666")
    line(tx + 90, yy, tx + 90, yy + 4, "#4b5666")
text(tx + 48, thumb_y + 30, "＋", 24, "#6ed8c5", anchor="ma")
text(tx + 48, thumb_y + 62, "添加图片", 10, "#7f8a9a", anchor="ma")

# buttons under thumbnails area
rrect((636, thumb_y + 24, 736, thumb_y + 64), 9, fill="#202631", outline="#39424f")
text(686, thumb_y + 36, "按名称排序", 11.5, "#aab3c1", anchor="ma")
rrect((748, thumb_y + 24, 824, thumb_y + 64), 9, fill="#202631", outline="#39424f")
text(786, thumb_y + 36, "清空", 11.5, "#aab3c1", anchor="ma")
text(84, 778, "提示：右键菜单可能位于 Win11 的「显示更多选项」中；工具内可一键修复。", 11, "#5f6878")

# ---------------------------------------------------------------- right panel
rrect((1048, 112, 1358, 858), 14, fill="#1a1e26", outline="#2b313c")
text(1070, 130, "拼接设置", 16, "#f1f4f9", bold=True)
text(1070, 158, "参数修改会实时刷新预览", 10.5, "#6d7787")

def section(y, title):
    text(1070, y, title, 11, "#8e98a8", bold=True)
    line(1070, y + 22, 1336, y + 22, "#2a303a")

# layout segmented
section(194, "拼接方式")
rrect((1070, 226, 1198, 266), 9, fill="#1f8a7a")
text(1134, 237, "横向排列", 12, "#eafffb", anchor="ma")
rrect((1202, 226, 1336, 266), 9, fill="#202631", outline="#39424f")
text(1269, 237, "纵向排列", 12, "#a6b0bf", anchor="ma")
text(1070, 272, "横向：统一高度，从左到右排列", 9.5, "#5f6878")

# spacing steppers
section(300, "间距与边距")
text(1070, 332, "图片间距", 11.5, "#c6ceda")
rrect((1200, 322, 1336, 358), 8, fill="#11151b", outline="#333c49")
text(1214, 331, "−", 15, "#8994a5")
text(1268, 332, "12 px", 11.5, "#eef2f8", anchor="ma")
text(1322, 331, "＋", 14, "#8994a5")
text(1070, 372, "外边距", 11.5, "#c6ceda")
rrect((1200, 362, 1336, 398), 8, fill="#11151b", outline="#333c49")
text(1214, 371, "−", 15, "#8994a5")
text(1268, 372, "16 px", 11.5, "#eef2f8", anchor="ma")
text(1322, 371, "＋", 14, "#8994a5")

# background
section(422, "背景")
# transparent swatch with checker
sw_y = 454
for i, (x0, label, col, selected) in enumerate([
        (1070, "透明", None, True),
        (1136, "白色", "#f5f7fa", False),
        (1202, "黑色", "#101216", False)]):
    box = (x0, sw_y, x0 + 52, sw_y + 34)
    if col is None:
        ck = checker(8, "#d7dbe2", "#f8f9fb", 48, 30)
        m = Image.new("L", (48 * S, 30 * S), 0)
        ImageDraw.Draw(m).rounded_rectangle([0, 0, 48 * S - 1, 30 * S - 1], radius=5 * S, fill=255)
        img.paste(ck, ((x0 + 2) * S, (sw_y + 2) * S), m)
        d = ImageDraw.Draw(img)
        rrect((x0, sw_y, x0 + 52, sw_y + 34), 6, outline="#1f8a7a" if selected else "#3a4352", width=2 if selected else 1)
        text(x0 + 26, sw_y + 39, label, 9.5, "#6ed8c5" if selected else "#7f8a9a", anchor="ma")
    else:
        rrect(box, 6, fill=col, outline="#1f8a7a" if selected else "#3a4352", width=2 if selected else 1)
        text(x0 + 26, sw_y + 39, label, 9.5, "#6ed8c5" if selected else "#7f8a9a", anchor="ma")
# custom color + hex
rrect((1268, 454, 1336, 488), 6, fill="#232a36", outline="#3a4352")
text(1302, 463, "自定义", 9.5, "#aab3c1", anchor="ma")
text(1070, 514, "#00000000", 10, "#8b95a5")
text(1320, 514, "Alpha 0", 9.5, "#65707f", anchor="ra")

# alpha slider
line(1070, 548, 1336, 548, "#39424f", 5)
line(1070, 548, 1076, 548, "#1f8a7a", 5)
d.ellipse([1066 * S, 540 * S, 1080 * S, 556 * S], fill="#6ed8c5", outline="#0e1014", width=2 * S)
text(1070, 560, "透明度 0%（完全透明）", 9.5, "#6d7787")

# base size
section(592, "基准尺寸")
rrect((1070, 622, 1104, 650), 7, fill="#1f8a7a")
text(1087, 628, "✓", 11, "#eafffb", anchor="ma")
text(1114, 625, "自动（中位数）", 10.5, "#c6ceda")
rrect((1228, 616, 1336, 652), 8, fill="#11151b", outline="#333c49")
text(1322, 625, "1110 px", 11.5, "#eef2f8", anchor="ra")

# output format
section(672, "输出格式")
rrect((1070, 702, 1198, 742), 9, fill="#1f8a7a")
text(1134, 713, "PNG（透明）", 11.5, "#eafffb", anchor="ma")
rrect((1202, 702, 1336, 742), 9, fill="#202631", outline="#39424f")
text(1269, 713, "JPG", 11.5, "#a6b0bf", anchor="ma")
text(1070, 748, "PNG 完整保留每张图的透明通道", 9.5, "#5f6878")

# action buttons
rrect((1070, 778, 1336, 818), 10, fill="#1f8a7a")
text(1203, 788, "保存图片  Ctrl+S", 12.5, "#effffb", anchor="ma", bold=True)
rrect((1070, 826, 1336, 850), 8, fill=None, outline="#39424f")
text(1203, 832, "另存为…  Ctrl+Shift+S", 9.5, "#8f99a9", anchor="ma")

img = img.resize((W, H), Image.LANCZOS)
os.makedirs("docs", exist_ok=True)
out = "docs/ui-mockup.png"
img.save(out, "PNG")
print(out, img.size)

