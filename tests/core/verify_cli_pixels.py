# -*- coding: utf-8 -*-
"""用 Pillow 校验 `LightCollage.exe --compose` 的 CLI 输出像素（端到端集成测试辅助）。

用法:
    python verify_cli_pixels.py <输出目录>

要求输出目录里已有 `red40x20.png`、`semi40x20.png`（可直接从 tests/core/fixtures 复制），
以及用以下命令生成的四张输出图：

    LightCollage.exe --compose --layout h --gap 4 --margin 2 --bg #00000000 --base auto --format png ^
        --out out_h.png red40x20.png red40x20.png
    LightCollage.exe --compose --layout h --gap 4 --margin 2 --bg #00000000 --base auto --format png ^
        --out out_h_semi.png red40x20.png semi40x20.png
    LightCollage.exe --compose --layout v --gap 4 --margin 2 --bg #00000000 --base auto --format png ^
        --out out_v.png red40x20.png red40x20.png
    LightCollage.exe --compose --layout h --gap 4 --margin 2 --bg #00000000 --base auto --format jpg ^
        --out out_h.jpg red40x20.png red40x20.png
    :: 第 2 轮 gap=0 无缝（需要 green40x20.png，从 fixtures 复制）
    LightCollage.exe --compose --layout h --gap 0 --margin 0 --bg #00000000 --base auto --format png ^
        --out out_gap0.png red40x20.png green40x20.png
    LightCollage.exe --compose --layout h --gap 0 --margin 0 --bg #00000000 --base 41 --format png ^
        --out out_gap0_scaled.png red40x20.png green40x20.png

退出码 0 表示全部像素符合预期。
"""

import sys

from PIL import Image

failures = []


def check(path, size, points):
    image = Image.open(path).convert("RGBA")
    if image.size != size:
        failures.append("%s size %s != %s" % (path, image.size, size))
        return
    for (x, y), expected, tolerance, label in points:
        actual = image.getpixel((x, y))
        for index, name in enumerate("RGBA"):
            if abs(actual[index] - expected[index]) > tolerance:
                failures.append("%s (%d,%d) %s %s != %s (tolerance %d, %s)"
                                % (path, x, y, name, actual, expected, tolerance, label))
                break
    print("checked %s size=%s" % (path, image.size))


def check_seamless(path, size, boundary_x):
    """gap=0 双色图：全部像素必须不透明且非红即绿；边界两侧为纯色。"""
    image = Image.open(path).convert("RGBA")
    if image.size != size:
        failures.append("%s size %s != %s" % (path, image.size, size))
        return
    width, height = image.size
    translucent = 0
    wrong_color = 0
    for y in range(height):
        for x in range(width):
            pixel = image.getpixel((x, y))
            if pixel[3] < 255:
                translucent += 1
            is_red = pixel[0] > 240 and pixel[1] < 20 and pixel[2] < 20
            is_green = pixel[1] > 240 and pixel[0] < 20 and pixel[2] < 20
            if not is_red and not is_green:
                wrong_color += 1
    left = image.getpixel((boundary_x - 1, height // 2))
    right = image.getpixel((boundary_x, height // 2))
    print("checked %s size=%s translucent=%d nonRedGreen=%d boundary=%s|%s"
          % (path, image.size, translucent, wrong_color, left, right))
    if translucent != 0:
        failures.append("%s 存在 %d 个半透明像素（gap=0 透明缝）" % (path, translucent))
    if wrong_color != 0:
        failures.append("%s 存在 %d 个非红非绿像素（背景色列）" % (path, wrong_color))
    if not (left[0] > 240 and left[1] < 20):
        failures.append("%s 边界左列不是红色：%s" % (path, left))
    if not (right[1] > 240 and right[0] < 20):
        failures.append("%s 边界右列不是绿色：%s" % (path, right))


def main():
    if len(sys.argv) < 2:
        print("usage: python verify_cli_pixels.py <output-dir>")
        return 2
    out_dir = sys.argv[1].rstrip("\\/")

    # 横向：2 张 40x20，gap=4 margin=2 => 88x24
    check(out_dir + "/out_h.png", (88, 24), [
        ((20, 12), (255, 0, 0, 255), 2, "第 1 张图红色不透明"),
        ((43, 12), (0, 0, 0, 0), 0, "间距应透明"),
        ((66, 12), (255, 0, 0, 255), 2, "第 2 张图应画在右侧同一水平带"),
        ((1, 12), (0, 0, 0, 0), 0, "左边距透明"),
        ((86, 12), (0, 0, 0, 0), 0, "右边距透明"),
        ((20, 23), (0, 0, 0, 0), 0, "下边距透明（不能有图错位到下一行）"),
    ])

    # 横向 + 第二张半透明：alpha 必须保留
    check(out_dir + "/out_h_semi.png", (88, 24), [
        ((20, 12), (255, 0, 0, 255), 2, "第 1 张图红色"),
        ((56, 12), (0, 255, 0, 128), 3, "第 2 张半透明图保留自身 alpha=128"),
    ])

    # 纵向：gap=4 margin=2 => 44x48
    check(out_dir + "/out_v.png", (44, 48), [
        ((22, 12), (255, 0, 0, 255), 2, "第 1 张图在上方"),
        ((22, 25), (0, 0, 0, 0), 0, "纵向间距透明"),
        ((22, 36), (255, 0, 0, 255), 2, "第 2 张图在下方"),
        ((1, 12), (0, 0, 0, 0), 0, "左边距透明"),
    ])

    # JPG：透明背景压白，不透明
    check(out_dir + "/out_h.jpg", (88, 24), [
        ((20, 12), (255, 0, 0, 255), 4, "JPG 第 1 张图红色"),
        ((43, 12), (255, 255, 255, 255), 6, "JPG 间距压白"),
        ((66, 12), (255, 0, 0, 255), 4, "JPG 第 2 张图红色"),
    ])

    # 第 2 轮：gap=0 无缝（全分辨率）
    # 两张 40x20 红/绿，无缩放 => 80x20，边界在 x=40
    check_seamless(out_dir + "/out_gap0.png", (80, 20), 40)
    # 自定义 base=41 放大 => 164x41，边界在 x=82（GDI+ 双三次边缘采样路径）
    check_seamless(out_dir + "/out_gap0_scaled.png", (164, 41), 82)

    if failures:
        print("FAILURES:")
        for item in failures:
            print("  " + item)
        return 1
    print("CLI PIXEL CHECK OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
