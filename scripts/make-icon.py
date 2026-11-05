# -*- coding: utf-8 -*-
"""生成轻拼 LightCollage 的应用图标（开发期工具，构建时不需要运行）。

依赖：Pillow（本机已装）。运行：
    python scripts/make-icon.py

输出：
    assets/app.ico   多尺寸（16/20/24/32/40/48/64/128/256）透明图标，供 csc /win32icon 嵌入
    assets/app.png   256x256 PNG，供 README/预览使用
    build/_icon_preview.png  设计预览拼图（黑/白/棋盘格背景 + 小尺寸放大，便于目检）
"""

import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS_DIR = os.path.join(ROOT, "assets")
BUILD_DIR = os.path.join(ROOT, "build")
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SUPERSAMPLE = 4

FONT_BOLD = r"C:\Windows\Fonts\msyhbd.ttc"

# 黑白灰品牌色（与主界面主题一致）
BLUE_TOP = (246, 246, 246, 255)
VIOLET_BOTTOM = (176, 176, 176, 255)
TILE_TOP = (34, 34, 34, 255)
TILE_BOTTOM = (78, 78, 78, 255)
SUN_COLOR = (255, 255, 255, 255)
MOUNTAIN_COLOR = (196, 196, 196, 255)
SHADOW_COLOR = (0, 0, 0, 90)


def _rounded_mask(size, radius):
    mask = Image.new("L", size, 0)
    draw = ImageDraw.Draw(mask)
    draw.rounded_rectangle([0, 0, size[0] - 1, size[1] - 1], radius=radius, fill=255)
    return mask


def _vertical_gradient(size, top, bottom):
    width, height = size
    gradient = Image.new("RGBA", (1, height))
    for y in range(height):
        t = y / float(max(1, height - 1))
        gradient.putpixel(
            (0, y),
            (
                int(round(top[0] + (bottom[0] - top[0]) * t)),
                int(round(top[1] + (bottom[1] - top[1]) * t)),
                int(round(top[2] + (bottom[2] - top[2]) * t)),
                255,
            ),
        )
    return gradient.resize(size, Image.Resampling.BILINEAR)


def _rounded_square(size, top, bottom, radius_ratio=0.215):
    """圆角方块背景：垂直渐变 + 顶部柔光。"""
    radius = int(round(size[0] * radius_ratio))
    mask = _rounded_mask(size, radius)
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    layer.paste(_vertical_gradient(size, top, bottom), (0, 0))
    # 顶部柔光，增加一点质感
    shine = Image.new("RGBA", size, (0, 0, 0, 0))
    shine_draw = ImageDraw.Draw(shine)
    shine_draw.ellipse(
        [-size[0] * 0.30, -size[1] * 0.72, size[0] * 1.30, size[1] * 0.42],
        fill=(255, 255, 255, 26),
    )
    layer.alpha_composite(shine)
    layer.putalpha(mask)
    return layer


def _clip_to(layer, mask):
    """用 mask 裁剪 RGBA 图层的 alpha。"""
    alpha = layer.split()[3]
    clipped = Image.new("L", layer.size, 0)
    clipped.paste(mask, (0, 0))
    from PIL import ImageChops

    layer.putalpha(ImageChops.multiply(alpha, clipped))
    return layer


def draw_tiles_icon(pixel_size):
    """设计 A：渐变圆角方块上并排两张白色「照片卡」，直观表达“拼接”。"""
    scale = SUPERSAMPLE
    s = pixel_size * scale
    canvas = _rounded_square((s, s), BLUE_TOP, VIOLET_BOTTOM)

    tile_w = s * 0.295
    tile_h = s * 0.475
    gap = s * 0.040
    radius = max(1.0, s * 0.055)
    total = tile_w * 2 + gap
    x0 = (s - total) / 2.0
    y0 = (s - tile_h) / 2.0

    # 卡片投影（裁剪在圆角方块内）
    shadow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    shadow_draw = ImageDraw.Draw(shadow)
    offset_x = s * 0.010
    offset_y = s * 0.016
    for i in range(2):
        x = x0 + i * (tile_w + gap)
        shadow_draw.rounded_rectangle(
            [x + offset_x, y0 + offset_y, x + tile_w + offset_x, y0 + tile_h + offset_y],
            radius=radius,
            fill=SHADOW_COLOR,
        )
    shadow = shadow.filter(ImageFilter.GaussianBlur(s * 0.016))
    shadow = _clip_to(shadow, _rounded_mask((s, s), int(round(s * 0.215))))
    canvas.alpha_composite(shadow)

    # 两张白色卡片
    for i in range(2):
        x = x0 + i * (tile_w + gap)
        tile_mask = Image.new("L", (s, s), 0)
        ImageDraw.Draw(tile_mask).rounded_rectangle(
            [x, y0, x + tile_w, y0 + tile_h], radius=radius, fill=255
        )
        tile = Image.new("RGBA", (s, s), (0, 0, 0, 0))
        tile.paste(_vertical_gradient((s, s), TILE_TOP, TILE_BOTTOM), (0, 0))
        tile.putalpha(tile_mask)

        # 小尺寸不画内部细节，避免糊成一团
        if pixel_size >= 28:
            motif = Image.new("RGBA", (s, s), (0, 0, 0, 0))
            motif_draw = ImageDraw.Draw(motif)
            sun_r = tile_w * 0.115
            sun_cx = x + tile_w * 0.685
            sun_cy = y0 + tile_h * 0.285
            motif_draw.ellipse(
                [sun_cx - sun_r, sun_cy - sun_r, sun_cx + sun_r, sun_cy + sun_r],
                fill=SUN_COLOR,
            )
            motif_draw.polygon(
                [
                    (x + tile_w * 0.075, y0 + tile_h * 0.880),
                    (x + tile_w * 0.380, y0 + tile_h * 0.500),
                    (x + tile_w * 0.620, y0 + tile_h * 0.880),
                ],
                fill=MOUNTAIN_COLOR,
            )
            motif_draw.polygon(
                [
                    (x + tile_w * 0.430, y0 + tile_h * 0.880),
                    (x + tile_w * 0.660, y0 + tile_h * 0.620),
                    (x + tile_w * 0.900, y0 + tile_h * 0.880),
                ],
                fill=(140, 140, 140, 255),
            )
            motif = _clip_to(motif, tile_mask)
            tile.alpha_composite(motif)

        canvas.alpha_composite(tile)

    return canvas.resize((pixel_size, pixel_size), Image.Resampling.LANCZOS)


def draw_glyph_icon(pixel_size):
    """设计 B：渐变圆角方块 + 白色「拼」字（备选方案）。"""
    scale = SUPERSAMPLE
    s = pixel_size * scale
    canvas = _rounded_square((s, s), (238, 238, 238, 255), (168, 168, 168, 255))

    if not os.path.exists(FONT_BOLD):
        return canvas.resize((pixel_size, pixel_size), Image.Resampling.LANCZOS)

    font_size = int(round(s * 0.66))
    font = ImageFont.truetype(FONT_BOLD, font_size, index=0)
    text = "拼"

    # 文字投影
    shadow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    shadow_draw = ImageDraw.Draw(shadow)
    bbox = shadow_draw.textbbox((0, 0), text, font=font)
    text_w = bbox[2] - bbox[0]
    text_h = bbox[3] - bbox[1]
    tx = (s - text_w) / 2.0 - bbox[0]
    ty = (s - text_h) / 2.0 - bbox[1]
    shadow_draw.text((tx, ty + s * 0.018), text, font=font, fill=(0, 0, 0, 110))
    shadow = shadow.filter(ImageFilter.GaussianBlur(s * 0.012))
    canvas.alpha_composite(shadow)

    text_layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    ImageDraw.Draw(text_layer).text((tx, ty), text, font=font, fill=(255, 255, 255, 255))
    canvas.alpha_composite(text_layer)
    return canvas.resize((pixel_size, pixel_size), Image.Resampling.LANCZOS)


def save_ico(path, render_fn):
    frames = []
    for size in ICO_SIZES:
        frames.append(render_fn(size))
    base = frames[-1]  # 256
    others = frames[:-1]
    base.save(path, format="ICO", sizes=[(s, s) for s in ICO_SIZES], append_images=others)


def _checkerboard(size, cell=8):
    board = Image.new("RGBA", size, (255, 255, 255, 255))
    draw = ImageDraw.Draw(board)
    for y in range(0, size[1], cell):
        for x in range(0, size[0], cell):
            if ((x // cell) + (y // cell)) % 2 == 0:
                draw.rectangle([x, y, x + cell - 1, y + cell - 1], fill=(204, 204, 204, 255))
    return board


def build_preview():
    """生成设计对比图，便于人工目检小尺寸可读性。"""
    os.makedirs(BUILD_DIR, exist_ok=True)
    renderers = [("A 双卡片", draw_tiles_icon), ("B 拼字", draw_glyph_icon)]
    row_h = 300
    sheet_w = 1000
    sheet = Image.new("RGBA", (sheet_w, row_h * len(renderers) + 10), (245, 246, 250, 255))
    draw = ImageDraw.Draw(sheet)
    label_font = ImageFont.truetype(FONT_BOLD, 18, index=0) if os.path.exists(FONT_BOLD) else None

    for row, (label, render_fn) in enumerate(renderers):
        top = row * row_h + 10
        draw.text((12, top + 4), label, font=label_font, fill=(30, 34, 60, 255))
        x = 12
        # 256 大图放在棋盘格上
        big = render_fn(256)
        board = _checkerboard((264, 264), 11)
        board.alpha_composite(big, (4, 4))
        sheet.alpha_composite(board, (x, top + 30))
        draw.text((x, top + 296), "256", font=label_font, fill=(60, 60, 80, 255))
        x += 290
        # 真实小尺寸
        for size in [64, 48, 32, 24, 20, 16]:
            icon = render_fn(size)
            board = _checkerboard((size + 8, size + 8), 4)
            board.alpha_composite(icon, (4, 4))
            sheet.alpha_composite(board, (x, top + 30))
            draw.text((x, top + 296), str(size), font=label_font, fill=(60, 60, 80, 255))
            x += size + 26
        # 16/32 放大 5 倍，检查形状（最近邻）
        for size in [16, 32]:
            icon = render_fn(size)
            enlarged = icon.resize((size * 5, size * 5), Image.Resampling.NEAREST)
            board = _checkerboard((size * 5 + 8, size * 5 + 8), 5)
            board.alpha_composite(enlarged, (4, 4))
            sheet.alpha_composite(board, (x, top + 30))
            draw.text((x, top + 296), "%dx5" % size, font=label_font, fill=(60, 60, 80, 255))
            x += size * 5 + 20

    preview_path = os.path.join(BUILD_DIR, "_icon_preview.png")
    sheet.save(preview_path, format="PNG")
    print("preview ->", preview_path)


def main():
    os.makedirs(ASSETS_DIR, exist_ok=True)
    build_preview()
    save_ico(os.path.join(ASSETS_DIR, "app.ico"), draw_tiles_icon)
    draw_tiles_icon(256).save(os.path.join(ASSETS_DIR, "app.png"), format="PNG")
    print("ico     ->", os.path.join(ASSETS_DIR, "app.ico"))
    print("png     ->", os.path.join(ASSETS_DIR, "app.png"))


if __name__ == "__main__":
    main()
