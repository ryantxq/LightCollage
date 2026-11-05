# -*- coding: utf-8 -*-
"""生成轻拼 Core 单测所需的测试图片（Pillow）。

用法:
    python make_test_images.py <输出目录>

生成内容包含：纯色/半透明 PNG、四色块 PNG（用于 EXIF 方向校验）、
EXIF Orientation 1-8 的 JPEG、动图 GIF（首帧红色）、带 Alpha 的 WebP、
损坏文件、大图（用于预览缩放/Alpha 保留）。
"""

import os
import sys

from PIL import Image

RED = (255, 0, 0, 255)
GREEN = (0, 255, 0, 255)
BLUE = (0, 0, 255, 255)
YELLOW = (255, 255, 0, 255)
SEMI_GREEN = (0, 255, 0, 128)


def solid(path, size, color, mode="RGBA"):
    Image.new(mode, size, color).save(path)


def quadrants(path, size=(24, 16), mode="RGBA", fmt=None):
    """四色块：左上红、右上绿、左下蓝、右下黄。"""
    width, height = size
    image = Image.new(mode, size, RED)
    half_w = width // 2
    half_h = height // 2
    image.paste(GREEN, (half_w, 0, width, half_h))
    image.paste(BLUE, (0, half_h, half_w, height))
    image.paste(YELLOW, (half_w, half_h, width, height))
    if fmt is None:
        image.save(path)
    else:
        image.save(path, fmt)
    return image


def pattern(path, size=(40, 20)):
    """左半不透明红、右半半透明绿（保留自身 alpha 用）。"""
    width, height = size
    image = Image.new("RGBA", size, (0, 0, 0, 0))
    image.paste(RED, (0, 0, width // 2, height))
    image.paste(SEMI_GREEN, (width // 2, 0, width, height))
    image.save(path)


def generate(out_dir):
    os.makedirs(out_dir, exist_ok=True)
    made = []

    def add(name):
        made.append(name)
        return os.path.join(out_dir, name)

    solid(add("red40x20.png"), (40, 20), RED)
    solid(add("green40x20.png"), (40, 20), GREEN)
    solid(add("semi40x20.png"), (40, 20), SEMI_GREEN)
    solid(add("transparent40x20.png"), (40, 20), (0, 0, 0, 0))
    pattern(add("pattern40x20.png"), (40, 20))
    pattern(add("big200x100.png"), (200, 100))
    # 大图纯色（用于 gap=0 缩放/预览缩略图边缘无缝校验）
    solid(add("big_red200x100.png"), (200, 100), RED)
    solid(add("big_green200x100.png"), (200, 100), GREEN)
    quadrants(add("quadrants24x16.png"), (24, 16))

    # JPEG + EXIF Orientation 1..8（存储像素均为四色块，方向由 EXIF 标记）
    quad_rgb = Image.open(os.path.join(out_dir, "quadrants24x16.png")).convert("RGB")
    for orientation in range(1, 9):
        exif = Image.Exif()
        exif[0x0112] = orientation
        path = add("exif_o%d.jpg" % orientation)
        quad_rgb.save(path, "JPEG", quality=95, exif=exif.tobytes())

    # 动图 GIF：首帧红、次帧蓝
    frame0 = Image.new("RGB", (32, 24), (255, 0, 0))
    frame1 = Image.new("RGB", (32, 24), (0, 0, 255))
    frame0.save(add("anim.gif"), "GIF", save_all=True, append_images=[frame1],
                duration=100, loop=0)

    # WebP（无损 + Alpha），以及带 EXIF Orientation=6 的 WebP
    semi = Image.new("RGBA", (40, 20), (0, 0, 0, 0))
    semi.paste(RED, (0, 0, 20, 20))
    semi.paste(SEMI_GREEN, (20, 0, 40, 20))
    semi.save(add("semi_webp.webp"), "WEBP", lossless=True)
    webp_out = os.path.join(out_dir, "pattern40x20.webp")
    Image.open(os.path.join(out_dir, "pattern40x20.png")).save(webp_out, "WEBP", lossless=True)
    made.append("pattern40x20.webp")

    exif = Image.Exif()
    exif[0x0112] = 6
    add("exif_o6.webp")
    quad_rgb.save(os.path.join(out_dir, "exif_o6.webp"), "WEBP", quality=95, exif=exif.tobytes())

    # 损坏 / 假扩展名文件
    with open(add("corrupted.png"), "wb") as handle:
        handle.write(b"this is definitely not a png file")
    with open(add("fake.heic"), "wb") as handle:
        handle.write(b"this is definitely not a heic file")

    # 规模自检
    checks = {
        "red40x20.png": (40, 20),
        "green40x20.png": (40, 20),
        "semi40x20.png": (40, 20),
        "transparent40x20.png": (40, 20),
        "pattern40x20.png": (40, 20),
        "big200x100.png": (200, 100),
        "big_red200x100.png": (200, 100),
        "big_green200x100.png": (200, 100),
        "quadrants24x16.png": (24, 16),
        "anim.gif": (32, 24),
        "exif_o1.jpg": (24, 16),
        "exif_o8.jpg": (24, 16),
        "pattern40x20.webp": (40, 20),
    }
    for name, expected in checks.items():
        with Image.open(os.path.join(out_dir, name)) as image:
            actual = image.size
        if actual != expected:
            raise SystemExit("fixture %s size mismatch: %s != %s" % (name, actual, expected))

    # 校验 Pillow 写出的 EXIF 方向确实可读
    for orientation in range(1, 9):
        with Image.open(os.path.join(out_dir, "exif_o%d.jpg" % orientation)) as image:
            stored = image.getexif().get(0x0112, 1)
        if stored != orientation:
            raise SystemExit("fixture exif_o%d.jpg orientation not written: %r" % (orientation, stored))

    print("generated %d fixtures into %s" % (len(made), out_dir))
    for name in made:
        print("  " + name)


if __name__ == "__main__":
    target = sys.argv[1] if len(sys.argv) > 1 else "fixtures"
    generate(target)
