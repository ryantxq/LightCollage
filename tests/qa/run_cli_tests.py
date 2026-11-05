# -*- coding: utf-8 -*-
"""轻拼 LightCollage v1.1.0 CLI 验收测试（QA 独立实现，不复用产品源码）。

覆盖：[v1.0 回归] --version/--help、--compose 横/纵布局尺寸、gap/margin/base、
透明背景 alpha、图片自身半透明像素保留、半透明背景叠加、JPG 白底无 alpha、
WebP 真实解码、GIF 首帧、EXIF 方向、尺寸保护、损坏/不支持文件非零退出、中文路径、颜色格式；
[v1.1 新增] --compose --layout g 网格：行列/行间距/列间距/外边距几何、行优先顺序像素、
合并单元格（含跨间距大格）、容量不足截断（多余图不输出）、末尾空格、gap=0 无缝、
--gap 回落、--merge 1 基格式与非法项容错、指定宽/高（--out-width/--out-height）整体缩放、
PNG 透明/自定背景、JPG 白底、--base 在网格模式被忽略、行列越界 clamp。
像素校验用 Pillow 独立解码/比对；布局期望值由本脚本按 docs/需求确认-网格布局拼图-v1.1.md
第 5 节与 docs/interfaces.md §10.3 独立实现（不复用产品源码）。
环境变量 QA_EXE 可覆盖被测 exe（默认 dist/轻拼/LightCollage.exe；仓库内正式路径为 dist\\轻拼\\LightCollage.exe）。
"""
import hashlib
import json
import os
import subprocess
import sys

from PIL import Image, ImageOps

# 项目根目录：由本文件位置（tests/qa/run_cli_tests.py）向上三级推导，任意目录运行均可。
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
EXE = os.environ.get("QA_EXE") or os.path.join(ROOT, "dist", "轻拼", "LightCollage.exe")
FIX = os.path.join(ROOT, "tests", "core", "fixtures")
SAMPLES = r"C:\Temp\collage-samples"
OUTDIR = r"C:\Temp\qa-lightcollage\out"
GRIDDIR = r"C:\Temp\qa-lightcollage\grid"
RESULT_JSON = os.path.join(OUTDIR, "cli-results.json")
RESULT_LOG = os.path.join(OUTDIR, "cli-results.log")
os.makedirs(OUTDIR, exist_ok=True)
os.makedirs(GRIDDIR, exist_ok=True)

results = []
log_lines = []


def log(s):
    print(s)
    log_lines.append(s)


def run(args, timeout=180):
    """运行 exe，返回 (exit, stdout, stderr)；中文系统控制台输出按 GBK 解码。"""
    p = subprocess.run([EXE] + args, capture_output=True, timeout=timeout)
    out = p.stdout.decode("gbk", "replace")
    err = p.stderr.decode("gbk", "replace")
    return p.returncode, out, err


def check(name, ok, expected, actual, evidence=""):
    results.append({"name": name, "pass": bool(ok), "expected": str(expected), "actual": str(actual), "evidence": evidence})
    log(("[PASS] " if ok else "[FAIL] ") + name + " | expected=" + str(expected) + " | actual=" + str(actual) + ((" | " + evidence) if evidence else ""))


def expected_layout(sizes, orientation, gap, margin, base):
    """独立实现需求文档 4 节规则。sizes: [(w,h)...]"""
    if not sizes:
        return (0, 0, 0)
    if orientation == "h":
        auto = sorted(s[1] for s in sizes)
        b = auto[len(auto) // 2] if len(auto) % 2 == 1 else round((auto[len(auto) // 2 - 1] + auto[len(auto) // 2]) / 2.0)
        H = b if base == "auto" else base
        ws = [max(1, int(round(w * H / float(h)))) for (w, h) in sizes]
        W = 2 * margin + sum(ws) + gap * (len(sizes) - 1)
        return (W, 2 * margin + H, H)
    else:
        auto = sorted(s[0] for s in sizes)
        b = auto[len(auto) // 2] if len(auto) % 2 == 1 else round((auto[len(auto) // 2 - 1] + auto[len(auto) // 2]) / 2.0)
        W = b if base == "auto" else base
        hs = [max(1, int(round(h * W / float(w)))) for (w, h) in sizes]
        H = 2 * margin + sum(hs) + gap * (len(sizes) - 1)
        return (2 * margin + W, H, W)


def round_away(x):
    """四舍五入、.5 远离 0（与产品 Math.Round(AwayFromZero) 一致；本脚本只用正数）。"""
    return int(x + 0.5) if x >= 0 else -int(-x + 0.5)


def median_away(values):
    """独立实现中位数：奇数取中间；偶数取中间两个平均后四舍五入（.5 远离 0），至少 1。"""
    if not values:
        return 0
    ordered = sorted(values)
    n = len(ordered)
    if n % 2 == 1:
        value = ordered[n // 2]
    else:
        value = (ordered[n // 2 - 1] + ordered[n // 2]) / 2.0
    return max(1, round_away(value))


# ---------------------------------------------------------------- 网格（v1.1）独立期望实现
# 依据 docs/需求确认-网格布局拼图-v1.1.md 5.2/5.3/5.5 与 docs/interfaces.md §10.3；
# 参数：merges 为 0 基闭区间元组列表 [(r0,c0,r1,c1), ...]（与 CLI 1 基字符串解耦）。

def clamp(value, low, high):
    return max(low, min(high, value))


def grid_normalize_merges(rows, cols, merges):
    """去非法/重叠，按行优先排序（先出现者优先），语义同契约：面积<2、越界、重叠丢弃。"""
    accepted = []
    occupied = [[False] * cols for _ in range(rows)]
    for region in merges or []:
        r0, c0, r1, c1 = region
        rs = r1 - r0 + 1
        cs = c1 - c0 + 1
        if rs < 1 or cs < 1 or rs * cs < 2:
            continue
        if r0 < 0 or c0 < 0 or r1 >= rows or c1 >= cols:
            continue
        overlap = False
        for r in range(r0, r1 + 1):
            for c in range(c0, c1 + 1):
                if occupied[r][c]:
                    overlap = True
                    break
            if overlap:
                break
        if overlap:
            continue
        for r in range(r0, r1 + 1):
            for c in range(c0, c1 + 1):
                occupied[r][c] = True
        accepted.append((r0, c0, r1, c1))
    accepted.sort(key=lambda m: (m[0], m[1]))
    return accepted


def grid_regions(rows, cols, merges):
    """行优先填图区域列表（每个元素 = (r0,c0,r1,c1)），容量 = len(regions)。"""
    accepted = grid_normalize_merges(rows, cols, merges)
    covered = [[False] * cols for _ in range(rows)]
    top_left = {}
    for (r0, c0, r1, c1) in accepted:
        for r in range(r0, r1 + 1):
            for c in range(c0, c1 + 1):
                covered[r][c] = True
        top_left[(r0, c0)] = (r0, c0, r1, c1)
    regions = []
    for r in range(rows):
        for c in range(cols):
            if covered[r][c]:
                if (r, c) in top_left:
                    regions.append(top_left[(r, c)])
                continue
            regions.append((r, c, r, c))
    return regions


def grid_region_rect(region, cell_w, cell_h, row_gap, col_gap, margin):
    r0, c0, r1, c1 = region
    x = margin + c0 * (cell_w + col_gap)
    y = margin + r0 * (cell_h + row_gap)
    w = (c1 - c0 + 1) * cell_w + (c1 - c0) * col_gap
    h = (r1 - r0 + 1) * cell_h + (r1 - r0) * row_gap
    return (x, y, w, h)


def expected_grid(sizes, rows, cols, merges, row_gap, col_gap, margin,
                  mode="auto", target=None):
    """独立实现网格期望：返回 dict(cw, ch, capacity, placed, cell_w, cell_h,
    row_gap, col_gap, margin, items, all_regions)。items 为前 placed 个区域矩形（输出像素）。"""
    rows = clamp(rows, 1, 20)
    cols = clamp(cols, 1, 20)
    row_gap = clamp(row_gap, 0, 200)
    col_gap = clamp(col_gap, 0, 200)
    margin = clamp(margin, 0, 200)
    regions = grid_regions(rows, cols, merges)
    capacity = len(regions)
    placed = min(len(sizes), capacity)
    if not sizes:
        return {"cw": 0, "ch": 0, "capacity": 0, "placed": 0, "cell_w": 0, "cell_h": 0,
                "row_gap": row_gap, "col_gap": col_gap, "margin": margin,
                "items": [], "all_regions": []}
    cell_w = median_away([w for (w, h) in sizes])
    cell_h = median_away([h for (w, h) in sizes])
    cw = 2 * margin + cols * cell_w + (cols - 1) * col_gap
    ch = 2 * margin + rows * cell_h + (rows - 1) * row_gap
    all_rects = [grid_region_rect(rg, cell_w, cell_h, row_gap, col_gap, margin) for rg in regions]
    items = list(all_rects[:placed])

    def scale_rect(rect, fx):
        x, y, w, h = rect
        x1 = round_away(x * fx)
        x2 = round_away((x + w) * fx)
        y1 = round_away(y * fx)
        y2 = round_away((y + h) * fx)
        if x2 <= x1:
            x2 = x1 + 1
        if y2 <= y1:
            y2 = y1 + 1
        if x1 > cw:
            x1 = cw
        if y1 > ch:
            y1 = ch
        if x2 > cw:
            x2 = cw
        if y2 > ch:
            y2 = ch
        return (x1, y1, max(1, x2 - x1), max(1, y2 - y1))

    if mode in ("width", "height") and target:
        if mode == "width":
            factor = target / float(cw)
            ch = max(1, round_away(ch * factor))
            cw = target
        else:
            factor = target / float(ch)
            cw = max(1, round_away(cw * factor))
            ch = target
        items = [scale_rect(r, factor) for r in all_rects[:placed]]
        all_rects = [scale_rect(r, factor) for r in all_rects]
    return {"cw": cw, "ch": ch, "capacity": capacity, "placed": placed,
            "cell_w": cell_w, "cell_h": cell_h, "row_gap": row_gap, "col_gap": col_gap,
            "margin": margin, "items": items, "all_regions": all_rects}


def grid_fixture(name, size, color):
    """生成纯色网格测试图（RGBA），返回路径。"""
    path = os.path.join(GRIDDIR, name)
    Image.new("RGBA", size, color).save(path)
    return path


def grid_center(im, rect):
    x, y, w, h = rect
    return px(im, int(x + w // 2), int(y + h // 2))


def check_grid_centers(prefix, out_path, exp, colors, tol=1):
    """逐区域中心像素校验行优先顺序（透明/背景不参与，只校验已放入区域）。"""
    im = Image.open(out_path).convert("RGBA")
    bad = []
    for i, rect in enumerate(exp["items"]):
        got = grid_center(im, rect)
        if not close_rgba(got, colors[i], tol):
            bad.append((i, got, colors[i]))
    check(prefix + ".rowmajor_centers", not bad,
          "前 %d 个区域中心按行优先为对应纯色" % exp["placed"],
          ("mismatch=" + repr(bad)) if bad else ("%d 个区域中心颜色正确" % exp["placed"]))
    return im


def check_grid_slot_bg(prefix, im, exp, slot_indices, bg=None):
    """校验指定区域索引为空位：PNG 透明（bg=None）或等于自定背景色。"""
    bad = []
    for idx in slot_indices:
        if idx >= len(exp["all_regions"]):
            continue
        got = grid_center(im, exp["all_regions"][idx])
        if bg is None:
            if got[3] != 0:
                bad.append((idx, got, "alpha=0"))
        elif not close_rgba(got, bg, 1):
            bad.append((idx, got, bg))
    check(prefix + ".empty_slots_bg", not bad,
          "空区域中心为 %s" % ("透明" if bg is None else str(bg)),
          ("mismatch=" + repr(bad)) if bad else ("%d 个空位正确" % len(slot_indices)))


def px(im, x, y):
    return im.getpixel((x, y))


def close_rgba(a, b, tol=2):
    return all(abs(int(x) - int(y)) <= tol for x, y in zip(a, b))


# ---------------------------------------------------------------- 环境信息
exe_size = os.path.getsize(EXE)
with open(EXE, "rb") as f:
    exe_sha = hashlib.sha256(f.read()).hexdigest()
log("EXE: %s" % EXE)
log("EXE size=%d sha256=%s" % (exe_size, exe_sha))
log("Python: %s Pillow %s" % (sys.version.split()[0], Image.__version__))

# ---------------------------------------------------------------- 1. --version / --help
rc, out, err = run(["--version"])
check("version.exit0", rc == 0, "exit 0", "exit %d" % rc)
check("version.text", "轻拼" in out and "1.1.0" in out, "含 '轻拼' 与 '1.1.0'", repr(out.strip()))

rc, out, err = run(["--help"])
check("help.exit0", rc == 0, "exit 0", "exit %d" % rc)
has_usage = ("--compose" in out) and ("--register" in out) and ("--version" in out)
check("help.compose_usage", has_usage, "帮助含 --compose/--register/--version", "exit %d, 长度 %d" % (rc, len(out)))
has_grid_usage = ("--rows" in out) and ("--merge" in out) and ("--row-gap" in out)
check("help.grid_usage", has_grid_usage, "帮助含网格参数 --rows/--row-gap/--merge", repr(out[:400]))

rc, out, err = run(["--compose"])
check("compose_no_args.exit2", rc == 2, "exit 2", "exit %d" % rc)
check("compose_no_args.usage", "用法" in err or "用法" in out, "输出用法", repr((err or out)[:120]))

# ---------------------------------------------------------------- 2. 横向 PNG 透明背景（3 张样例）
out_png = os.path.join(OUTDIR, "h_transparent.png")
rc, stdout, stderr = run(["--compose", "--layout", "h", "--gap", "12", "--margin", "16",
                          "--bg", "#00000000", "--base", "auto", "--format", "png", "--out", out_png,
                          os.path.join(SAMPLES, "photo1.png"), os.path.join(SAMPLES, "photo2.png"),
                          os.path.join(SAMPLES, "photo3.png")])
sizes = [(1200, 800), (800, 1200), (1600, 900)]
ew, eh, ebase = expected_layout(sizes, "h", 12, 16, "auto")
check("h_png.exit0", rc == 0, "exit 0", "exit %d stderr=%r" % (rc, stderr[:120]))
check("h_png.okline", stdout.strip().startswith("OK %dx%d" % (ew, eh)), "stdout starts OK %dx%d" % (ew, eh), repr(stdout.strip()))
im = Image.open(out_png)
check("h_png.size", im.size == (ew, eh), (ew, eh), im.size)
check("h_png.mode", im.mode == "RGBA", "RGBA", im.mode)
# 边距角/间隙中点完全透明
check("h_png.margin_corner_alpha0", px(im, 0, 0)[3] == 0, "alpha=0", px(im, 0, 0))
check("h_png.margin_inside_alpha0", px(im, 15, eh // 2)[3] == 0, "alpha=0", px(im, 15, eh // 2))
ws = [max(1, int(round(w * ebase / float(h)))) for (w, h) in sizes]
gap_x = 16 + ws[0]
check("h_png.gap_alpha0", px(im, gap_x + 6, eh // 2)[3] == 0, "alpha=0 at gap x=%d" % (gap_x + 6), px(im, gap_x + 6, eh // 2))
check("h_png.photo_alpha255", px(im, 16 + ws[0] // 2, eh // 2)[3] == 255, "alpha=255", px(im, 16 + ws[0] // 2, eh // 2))
# 三张图中心均不透明
cx = 16
centers = []
for w in ws:
    centers.append(cx + w // 2)
    cx += w + 12
okc = all(px(im, c, eh // 2)[3] == 255 for c in centers)
check("h_png_all3_alpha255", okc, "3 张图中心 alpha=255", [px(im, c, eh // 2) for c in centers])
check("h_png.base900", ebase == 900, 900, ebase)

# ---------------------------------------------------------------- 3. 图片自身半透明像素 alpha 保留（透明背景）
out_semi = os.path.join(OUTDIR, "semi.png")
rc, stdout, stderr = run(["--compose", "--layout", "h", "--gap", "0", "--margin", "0",
                          "--bg", "#00000000", "--base", "auto", "--format", "png", "--out", out_semi,
                          os.path.join(FIX, "semi40x20.png"), os.path.join(FIX, "red40x20.png")])
im = Image.open(out_semi)
check("semi.exit0", rc == 0, "exit 0", "exit %d" % rc)
check("semi.size", im.size == (80, 20), (80, 20), im.size)
check("semi.alpha128_preserved", px(im, 20, 10) == (0, 255, 0, 128), "(0,255,0,128)", px(im, 20, 10))
check("semi.opaque_neighbor", px(im, 60, 10) == (255, 0, 0, 255), "(255,0,0,255)", px(im, 60, 10))

# 透明源 + 透明背景
out_tr = os.path.join(OUTDIR, "transparent.png")
rc, _, _ = run(["--compose", "--layout", "h", "--gap", "0", "--margin", "0", "--bg", "#00000000",
                "--base", "auto", "--format", "png", "--out", out_tr, os.path.join(FIX, "transparent40x20.png")])
im = Image.open(out_tr)
check("transparent_src.size", im.size == (40, 20), (40, 20), im.size)
check("transparent_src.alpha0", im.getextrema()[3] == (0, 0), "alpha 全 0", im.getextrema()[3])

# ---------------------------------------------------------------- 4. 半透明背景 + 不透明图片不受全局透明度影响
out_bg = os.path.join(OUTDIR, "bg_semi.png")
rc, _, _ = run(["--compose", "--layout", "h", "--gap", "0", "--margin", "4", "--bg", "#80FF0000",
                "--base", "auto", "--format", "png", "--out", out_bg,
                os.path.join(FIX, "red40x20.png"), os.path.join(FIX, "red40x20.png")])
im = Image.open(out_bg)
check("bg_semi.exit0", rc == 0, "exit 0", "exit %d" % rc)
check("bg_semi.size", im.size == (4 + 40 + 40 + 4, 4 + 20 + 4), (88, 28), im.size)
check("bg_semi.margin_argb", px(im, 1, 1) == (255, 0, 0, 128), "(255,0,0,128)", px(im, 1, 1))
check("bg_semi.image_opaque", px(im, 20, 14) == (255, 0, 0, 255), "(255,0,0,255)", px(im, 20, 14))

# ---------------------------------------------------------------- 5. JPG：无 alpha、透明区域压白
out_jpg = os.path.join(OUTDIR, "h_transparent.jpg")
rc, stdout, stderr = run(["--compose", "--layout", "h", "--gap", "10", "--margin", "10",
                          "--bg", "#00000000", "--base", "auto", "--format", "jpg", "--out", out_jpg,
                          os.path.join(FIX, "transparent40x20.png"), os.path.join(FIX, "red40x20.png")])
im = Image.open(out_jpg)
check("jpg.exit0", rc == 0, "exit 0", "exit %d" % rc)
check("jpg.mode_no_alpha", im.mode in ("RGB", "L"), "RGB", im.mode)
check("jpg.size", im.size == (10 + 40 + 10 + 40 + 10, 10 + 20 + 10), (110, 40), im.size)
check("jpg.transparent_to_white", all(c >= 250 for c in px(im, 2, 2)[:3]), "≈(255,255,255)", px(im, 2, 2))

# ---------------------------------------------------------------- 6. 纵向布局
out_v = os.path.join(OUTDIR, "v.png")
rc, stdout, stderr = run(["--compose", "--layout", "v", "--gap", "12", "--margin", "16", "--bg", "#00000000",
                          "--base", "auto", "--format", "png", "--out", out_v,
                          os.path.join(SAMPLES, "photo1.png"), os.path.join(SAMPLES, "photo2.png"),
                          os.path.join(SAMPLES, "photo3.png")])
ew, eh, ebase = expected_layout(sizes, "v", 12, 16, "auto")
im = Image.open(out_v)
check("v.exit0", rc == 0, "exit 0", "exit %d %r" % (rc, stderr[:80]))
check("v.size", im.size == (ew, eh), (ew, eh), im.size)
check("v.base", ebase, 1200, ebase)
check("v.okline", stdout.strip().startswith("OK %dx%d" % (ew, eh)), "OK %dx%d" % (ew, eh), repr(stdout.strip()))

# ---------------------------------------------------------------- 7. gap/margin/手动 base 精确尺寸
out_gm = os.path.join(OUTDIR, "gapmargin.png")
rc, _, _ = run(["--compose", "--layout", "h", "--gap", "7", "--margin", "3", "--bg", "#FF112233",
                "--base", "10", "--format", "png", "--out", out_gm,
                os.path.join(FIX, "pattern40x20.png"), os.path.join(FIX, "pattern40x20.png"),
                os.path.join(FIX, "pattern40x20.png")])
im = Image.open(out_gm)
# H=10 → W_i=20 each → W=6+60+14=80, H=6+10=16
check("gapmargin.size", im.size == (80, 16), (80, 16), im.size)
check("gapmargin.bg_solid", px(im, 1, 8) == (0x11, 0x22, 0x33, 255), "(17,34,51,255)", px(im, 1, 8))
check("gapmargin.gap_px", px(im, 3 + 20 + 3, 8) == (0x11, 0x22, 0x33, 255), "间隙为背景色", px(im, 3 + 20 + 3, 8))

# 0 间距/边距相邻检查
out_g0 = os.path.join(OUTDIR, "gap0.png")
rc, _, _ = run(["--compose", "--layout", "h", "--gap", "0", "--margin", "0", "--bg", "#00000000",
                "--base", "20", "--format", "png", "--out", out_g0,
                os.path.join(FIX, "red40x20.png"), os.path.join(FIX, "transparent40x20.png"),
                os.path.join(FIX, "red40x20.png")])
im = Image.open(out_g0)
check("gap0.size", im.size == (120, 20), (120, 20), im.size)
check("gap0.adjacent_pixels", px(im, 39, 10) == (255, 0, 0, 255) and px(im, 40, 10)[3] == 0,
      "x39 红不透明, x40 透明", (px(im, 39, 10), px(im, 40, 10)))

# ---------------------------------------------------------------- 8. 单图不缩放 + 边距
out_one = os.path.join(OUTDIR, "single.png")
rc, _, _ = run(["--compose", "--layout", "h", "--gap", "0", "--margin", "5", "--bg", "#FFFFFFFF",
                "--base", "auto", "--format", "png", "--out", out_one, os.path.join(FIX, "quadrants24x16.png")])
im = Image.open(out_one)
check("single.size", im.size == (24 + 10, 16 + 10), (34, 26), im.size)
check("single.quadrants", px(im, 5, 5) == (255, 0, 0, 255) and px(im, 5 + 23, 5) == (0, 255, 0, 255)
      and px(im, 5, 5 + 15) == (0, 0, 255, 255) and px(im, 5 + 23, 5 + 15) == (255, 255, 0, 255),
      "四象限颜色正确(TL红/TR绿/BL蓝/BR黄)", (px(im, 5, 5), px(im, 28, 5), px(im, 5, 20), px(im, 28, 20)))

# ---------------------------------------------------------------- 9. WebP 真实解码
out_webp = os.path.join(OUTDIR, "webp.png")
rc, stdout, stderr = run(["--compose", "--layout", "h", "--gap", "0", "--margin", "0", "--bg", "#00000000",
                          "--base", "auto", "--format", "png", "--out", out_webp,
                          os.path.join(FIX, "pattern40x20.webp"), os.path.join(FIX, "semi_webp.webp")])
im = Image.open(out_webp)
src = Image.open(os.path.join(FIX, "pattern40x20.webp")).convert("RGBA")
check("webp.exit0", rc == 0, "exit 0 (真实 WebP 解码)", "exit %d stderr=%r" % (rc, stderr[:150]))
check("webp.size", im.size == (80, 20), (80, 20), im.size)
ok_webp = True
maxdiff = 0
for (x, y) in [(2, 2), (20, 10), (37, 17)]:
    a = px(im, x, y)
    b = px(src, x, y)
    d = max(abs(int(i) - int(j)) for i, j in zip(a, b))
    maxdiff = max(maxdiff, d)
    if d > 12:
        ok_webp = False
check("webp.pixels", ok_webp, "与 Pillow 解码原图一致(tol=12)", "maxdiff=%d samples=%s" % (maxdiff, [px(im, x, y) for (x, y) in [(2, 2), (20, 10)]]))

# ---------------------------------------------------------------- 10. GIF 首帧
out_gif = os.path.join(OUTDIR, "gif.png")
rc, _, stderr = run(["--compose", "--layout", "h", "--gap", "0", "--margin", "0", "--bg", "#00000000",
                     "--base", "auto", "--format", "png", "--out", out_gif, os.path.join(FIX, "anim.gif")])
im = Image.open(out_gif)
gif = Image.open(os.path.join(FIX, "anim.gif"))
gif.seek(0)
first = gif.convert("RGBA")
check("gif.exit0", rc == 0, "exit 0 (GIF 首帧)", "exit %d stderr=%r" % (rc, stderr[:100]))
check("gif.size", im.size == first.size, first.size, im.size)
same = sum(1 for y in range(first.height) for x in range(first.width)
           if close_rgba(px(im, x, y), px(first, x, y), 8))
total = first.width * first.height
check("gif.first_frame_pixels", same >= total * 0.98, "≥98%% 像素一致", "%d/%d" % (same, total))

# ---------------------------------------------------------------- 11. EXIF 方向
exif_cases = []
for n in range(1, 9):
    p = os.path.join(FIX, "exif_o%d.jpg" % n)
    src = Image.open(p)
    oriented = ImageOps.exif_transpose(src)
    exif_cases.append((p, oriented))
p = os.path.join(FIX, "exif_o6.webp")
try:
    src = Image.open(p)
    exif_cases.append((p, ImageOps.exif_transpose(src)))
except Exception as e:
    log("webp exif sample skip: %r" % (e,))
for idx, (p, oriented) in enumerate(exif_cases):
    outp = os.path.join(OUTDIR, "exif_%d.png" % idx)
    rc, stdout, stderr = run(["--compose", "--layout", "h", "--gap", "0", "--margin", "0", "--bg", "#00000000",
                              "--base", "auto", "--format", "png", "--out", outp, p])
    ok = rc == 0 and os.path.exists(outp)
    if ok:
        got = Image.open(outp).convert("RGB")
        exp = oriented.convert("RGB")
        dim_ok = got.size == exp.size
        # 尺寸一致 + 中心颜色近似
        center_ok = False
        if dim_ok:
            c = (exp.width // 2, exp.height // 2)
            center_ok = close_rgba(px(got, *c), px(exp, *c), 12)
        check("exif_%s.size" % os.path.basename(p), dim_ok, exp.size, got.size)
        check("exif_%s.center" % os.path.basename(p), center_ok, "中心色近似", (px(got, exp.width // 2, exp.height // 2) if dim_ok else "size mismatch", px(exp, exp.width // 2, exp.height // 2)))
    else:
        check("exif_%s.exit0" % os.path.basename(p), False, "exit 0", "exit %d %r" % (rc, stderr[:100]))

# ---------------------------------------------------------------- 12. 尺寸保护（超限自动缩小）
out_big = os.path.join(OUTDIR, "clamp.png")
rc, stdout, stderr = run(["--compose", "--layout", "h", "--gap", "200", "--margin", "200",
                          "--bg", "#00000000", "--base", "30000", "--format", "png", "--out", out_big,
                          os.path.join(SAMPLES, "photo1.png"), os.path.join(SAMPLES, "photo2.png")])
ok = rc == 0 and os.path.exists(out_big)
if ok:
    im = Image.open(out_big)
    w, h = im.size
    px_count = w * h
    within = px_count <= 80000000 and w <= 32000 and h <= 32000
    check("clamp.exit0", True, "exit 0", "exit 0")
    check("clamp.limits", within, "总像素≤8000万且单边≤32000", "%dx%d=%d px" % (w, h, px_count))
    check("clamp.was_reduced", (w, h) != (None, None), "实际尺寸被自动缩小", "%dx%d" % (w, h))
else:
    check("clamp.exit0", False, "exit 0", "exit %d stderr=%r" % (rc, stderr[:200]))

# ---------------------------------------------------------------- 13. 损坏/不支持文件非零退出
corrupt = os.path.join(FIX, "corrupted.png")
out_bad = os.path.join(OUTDIR, "bad.png")
rc, stdout, stderr = run(["--compose", "--layout", "h", "--gap", "0", "--margin", "0", "--bg", "#00000000",
                          "--base", "auto", "--format", "png", "--out", out_bad, corrupt])
check("corrupt.exit_nonzero", rc != 0, "exit != 0", "exit %d stderr=%r" % (rc, stderr[:120]))
check("corrupt.error_message", "ERROR" in stderr, "stderr 以 ERROR 开头", repr(stderr[:120]))
check("corrupt.no_output", not os.path.exists(out_bad), "未生成输出文件", os.path.exists(out_bad))

fake_heic = os.path.join(FIX, "fake.heic")
out_heic = os.path.join(OUTDIR, "fake_heic.png")
rc, stdout, stderr = run(["--compose", "--layout", "h", "--gap", "0", "--margin", "0", "--bg", "#00000000",
                          "--base", "auto", "--format", "png", "--out", out_heic, fake_heic])
check("fake_heic.exit_nonzero", rc != 0, "exit != 0（无真实 HEIC 样本，仅测错误处理）", "exit %d stderr=%r" % (rc, stderr[:160]))

# 不支持的扩展名
tx = os.path.join(OUTDIR, "notimage.txt")
open(tx, "w").write("hello")
out_tx = os.path.join(OUTDIR, "tx.png")
rc, _, stderr = run(["--compose", "--layout", "h", "--gap", "0", "--margin", "0", "--bg", "#00000000",
                     "--base", "auto", "--format", "png", "--out", out_tx, tx])
check("unsupported.exit_nonzero", rc != 0, "exit != 0", "exit %d stderr=%r" % (rc, stderr[:120]))

# ---------------------------------------------------------------- 14. 中文输出路径 + #RRGGBB
cn_dir = os.path.join(OUTDIR, "中文目录")
os.makedirs(cn_dir, exist_ok=True)
cn_out = os.path.join(cn_dir, "拼图_中文路径.png")
rc, stdout, stderr = run(["--compose", "--layout", "h", "--gap", "0", "--margin", "4", "--bg", "#112233",
                          "--base", "auto", "--format", "png", "--out", cn_out,
                          os.path.join(FIX, "red40x20.png"), os.path.join(FIX, "red40x20.png")])
check("cnpath.exit0", rc == 0 and os.path.exists(cn_out), "中文路径输出成功", "exit %d exists=%s" % (rc, os.path.exists(cn_out)))
if os.path.exists(cn_out):
    im = Image.open(cn_out)
    check("cnpath.rgb6hex", px(im, 0, 0) == (0x11, 0x22, 0x33, 255), "(17,34,51,255)（#RRGGBB 补 255 alpha）", px(im, 0, 0))

# 非法颜色
out_badc = os.path.join(OUTDIR, "badcolor.png")
rc, _, stderr = run(["--compose", "--layout", "h", "--gap", "0", "--margin", "0", "--bg", "#GGGGGG",
                     "--base", "auto", "--format", "png", "--out", out_badc, os.path.join(FIX, "red40x20.png")])
check("badcolor.exit_nonzero", rc != 0, "exit != 0", "exit %d stderr=%r" % (rc, stderr[:120]))

# ---------------------------------------------------------------- 15. JPEG 质量参数
out_q1 = os.path.join(OUTDIR, "q30.jpg")
out_q2 = os.path.join(OUTDIR, "q95.jpg")
rc1, _, _ = run(["--compose", "--layout", "h", "--gap", "0", "--margin", "0", "--bg", "#00000000",
                 "--base", "auto", "--format", "jpg", "--quality", "30", "--out", out_q1,
                 os.path.join(SAMPLES, "photo3.png")])
rc2, _, _ = run(["--compose", "--layout", "h", "--gap", "0", "--margin", "0", "--bg", "#00000000",
                 "--base", "auto", "--format", "jpg", "--quality", "95", "--out", out_q2,
                 os.path.join(SAMPLES, "photo3.png")])
check("jpeg_quality.effect", rc1 == 0 and rc2 == 0 and os.path.getsize(out_q1) < os.path.getsize(out_q2),
      "quality30 文件小于 quality95", "%d vs %d bytes" % (os.path.getsize(out_q1), os.path.getsize(out_q2)))

# ---------------------------------------------------------------- 16. 网格 CLI（v1.1 新增）
log("")
log("---------------------------------------------------------------- 16. 网格 CLI 用例")

GRID_COLORS = [
    (255, 0, 0, 255), (0, 255, 0, 255), (0, 0, 255, 255),
    (255, 255, 0, 255), (255, 0, 255, 255), (0, 255, 255, 255),
    (255, 128, 0, 255), (128, 0, 255, 255), (0, 128, 128, 255),
]
# 3×3 用尺寸：宽度中位数 300、高度中位数 160（奇数个，median 语义直接覆盖）；
# 宽高各不相同，确保 contain 后区域中心仍是纯色。
GRID_SIZES_9 = [(200, 120), (300, 180), (400, 240), (200, 160), (300, 240),
                (400, 320), (200, 80), (300, 120), (400, 160)]
GRID_FILES_9 = [grid_fixture("g9-%d-%dx%d.png" % (i, s[0], s[1]), s, GRID_COLORS[i])
                for i, s in enumerate(GRID_SIZES_9)]
GRID_SMALL = (40, 20)
GRID_FILES_4 = [grid_fixture("g4-%d.png" % i, GRID_SMALL, GRID_COLORS[i]) for i in range(4)]


def grid_compose(name, options, images, exp, filename):
    """执行一次网格 --compose，断言 exit0 / OK WxH 行 / 输出文件与尺寸；返回 (path, image)。"""
    out_path = os.path.join(OUTDIR, filename)
    if os.path.exists(out_path):
        os.remove(out_path)
    rc, stdout, stderr = run(["--compose"] + options + ["--out", out_path] + images)
    check(name + ".exit0", rc == 0, "exit 0", "exit %d stderr=%r" % (rc, stderr[:200]))
    expected_ok = "OK %dx%d -> " % (exp["cw"], exp["ch"])
    check(name + ".okline", stdout.strip().startswith(expected_ok),
          "stdout 以 %r 开头" % expected_ok, repr(stdout.strip()))
    im = None
    if os.path.exists(out_path):
        im = Image.open(out_path)
        im.load()
    check(name + ".size", im is not None and im.size == (exp["cw"], exp["ch"]),
          (exp["cw"], exp["ch"]), im.size if im is not None else "missing file")
    return out_path, im


# ---- G1：3×3 无合并（自动单元格中位数 + 行优先 + 透明间距/边距）
exp = expected_grid(GRID_SIZES_9, 3, 3, [], 12, 12, 16)
log("G1 expected: %dx%d capacity=%d cell=%dx%d" % (exp["cw"], exp["ch"], exp["capacity"],
                                                   exp["cell_w"], exp["cell_h"]))
opts = ["--layout", "g", "--rows", "3", "--cols", "3", "--row-gap", "12", "--col-gap", "12",
        "--margin", "16", "--bg", "#00000000", "--format", "png"]
out_g1, im_g1 = grid_compose("g1_3x3", opts, GRID_FILES_9, exp, "grid_3x3.png")
check("g1_3x3.capacity9", exp["capacity"] == 9 and exp["placed"] == 9, "容量 9 / 放入 9", exp)
if im_g1 is not None:
    check("g1_3x3.mode", im_g1.mode == "RGBA", "RGBA", im_g1.mode)
    check_grid_centers("g1_3x3", out_g1, exp, GRID_COLORS)
    margin_px = px(im_g1, 0, 0)
    check("g1_3x3.margin_transparent", margin_px[3] == 0, "角像素 alpha=0", margin_px)
    gap_x = exp["margin"] + exp["cell_w"] + exp["col_gap"] // 2
    gap_y = exp["margin"] + exp["cell_h"] // 2
    gap_px = px(im_g1, gap_x, gap_y)
    check("g1_3x3.col_gap_transparent", gap_px[3] == 0, "列间距中点 alpha=0", gap_px)
    row_gap_y = exp["margin"] + exp["cell_h"] + exp["row_gap"] // 2
    row_px = px(im_g1, exp["margin"] + exp["cell_w"] // 2, row_gap_y)
    check("g1_3x3.row_gap_transparent", row_px[3] == 0, "行间距中点 alpha=0", row_px)

# ---- G2：3×3 + 左上 2×2 合并 → 容量 6；9 张图只放前 6 张（溢出图不输出）
merge_2x2 = [(0, 0, 1, 1)]
exp = expected_grid(GRID_SIZES_9, 3, 3, merge_2x2, 12, 12, 16)
log("G2 expected: %dx%d capacity=%d placed=%d big=%s" % (
    exp["cw"], exp["ch"], exp["capacity"], exp["placed"], exp["items"][0]))
opts = ["--layout", "g", "--rows", "3", "--cols", "3", "--row-gap", "12", "--col-gap", "12",
        "--margin", "16", "--merge", "1,1,2,2", "--bg", "#00000000", "--format", "png"]
out_g2, im_g2 = grid_compose("g2_merge_overflow", opts, GRID_FILES_9, exp, "grid_merge_2x2.png")
check("g2_merge_overflow.capacity6", exp["capacity"] == 6, "容量 6（9-3）", exp["capacity"])
check("g2_merge_overflow.placed6", exp["placed"] == 6, "只放入 6 张", exp["placed"])
if im_g2 is not None:
    check_grid_centers("g2_merge_overflow", out_g2, exp, GRID_COLORS)
    # 合并大格跨内部间距：大格右边界与 (0,2) 单元格之间仍有 12px 间距
    big = exp["items"][0]
    gap_sample = (big[0] + big[2] + 6, big[1] + big[3] // 2)
    check("g2_merge_overflow.big_rect_cross_gap", gap_sample[0] == exp["margin"] + big[2] + 6,
          "大格宽 = 2*cell + col_gap", big)
    present = set(im_g2.getdata())
    absent = [c for c in GRID_COLORS[6:9] if c in present]
    check("g2_merge_overflow.overflow_images_not_drawn", not absent,
          "第 7–9 张颜色不出现在输出中（未放入）", ("found=" + repr(absent)) if absent else "absent")
    check("g2_merge_overflow.big_gap_px", px(im_g2, gap_sample[0], gap_sample[1])[3] == 0,
          "大格右侧列间距 alpha=0", px(im_g2, gap_sample[0], gap_sample[1]))

# ---- G3：3×3 + 2×2 合并 + 仅 5 张图 → 最后一个区域 (2,2) 留空（背景/透明）
sizes_5 = GRID_SIZES_9[:5]
files_5 = GRID_FILES_9[:5]
exp = expected_grid(sizes_5, 3, 3, merge_2x2, 12, 12, 16)
log("G3 expected: %dx%d capacity=%d placed=%d" % (exp["cw"], exp["ch"], exp["capacity"], exp["placed"]))
opts = ["--layout", "g", "--rows", "3", "--cols", "3", "--row-gap", "12", "--col-gap", "12",
        "--margin", "16", "--merge", "1,1,2,2", "--bg", "#00000000", "--format", "png"]
out_g3, im_g3 = grid_compose("g3_last_empty", opts, files_5, exp, "grid_merge_last_empty.png")
if im_g3 is not None:
    check_grid_centers("g3_last_empty", out_g3, exp, GRID_COLORS[:5])
    check_grid_slot_bg("g3_last_empty", im_g3, exp, list(range(exp["placed"], exp["capacity"])))

# ---- G4：2×2、gap=0、margin=0 → 相邻区域严丝合缝（无 1px 缝隙/半透明）
exp = expected_grid([GRID_SMALL] * 4, 2, 2, [], 0, 0, 0)
log("G4 expected: %dx%d cell=%dx%d" % (exp["cw"], exp["ch"], exp["cell_w"], exp["cell_h"]))
opts = ["--layout", "g", "--rows", "2", "--cols", "2", "--row-gap", "0", "--col-gap", "0",
        "--margin", "0", "--bg", "#00000000", "--format", "png"]
out_g4, im_g4 = grid_compose("g4_gap0", opts, GRID_FILES_4, exp, "grid_gap0.png")
if im_g4 is not None:
    colors4 = GRID_COLORS[:4]
    pixels = set(im_g4.getdata())
    check("g4_gap0.no_translucent", all(p[3] == 255 for p in pixels), "无半透明像素",
          "unique=%d" % len(pixels))
    check("g4_gap0.only_4_colors", pixels <= set(colors4), "输出像素仅为 4 个纯色（无缝/无背景列）",
          sorted(pixels)[:6])
    # 行列边界两侧均为实色
    check("g4_gap0.col_boundary", px(im_g4, 39, 10) == colors4[0] and px(im_g4, 40, 10) == colors4[1],
          "x=39 红 / x=40 绿", (px(im_g4, 39, 10), px(im_g4, 40, 10)))
    check("g4_gap0.row_boundary", px(im_g4, 10, 19) == colors4[0] and px(im_g4, 10, 20) == colors4[2],
          "y=19 红 / y=20 蓝", (px(im_g4, 10, 19), px(im_g4, 10, 20)))
    check_grid_centers("g4_gap0", out_g4, exp, colors4)

# ---- G5：行/列间距分别生效（row-gap=8, col-gap=20, margin=10）
exp = expected_grid([GRID_SMALL] * 4, 2, 2, [], 8, 20, 10)
log("G5 expected: %dx%d" % (exp["cw"], exp["ch"]))
opts = ["--layout", "g", "--rows", "2", "--cols", "2", "--row-gap", "8", "--col-gap", "20",
        "--margin", "10", "--bg", "#00000000", "--format", "png"]
out_g5, im_g5 = grid_compose("g5_gaps", opts, GRID_FILES_4, exp, "grid_gaps.png")
if im_g5 is not None:
    check_grid_centers("g5_gaps", out_g5, exp, GRID_COLORS[:4])
    # 列间距 20px 中点与行间距 8px 中点均透明；边距 10px 内也透明
    col_gap_px = px(im_g5, 10 + 40 + 10, 10 + 20 + 4 + 10)
    check("g5_gaps.col_gap20", col_gap_px[3] == 0, "列间距 20px 中点 alpha=0", col_gap_px)
    row_gap_px = px(im_g5, 10 + 20, 10 + 20 + 4)
    check("g5_gaps.row_gap8", row_gap_px[3] == 0, "行间距 8px 中点 alpha=0", row_gap_px)
    check("g5_gaps.margin10", px(im_g5, 1, 1)[3] == 0, "外边距 alpha=0", px(im_g5, 1, 1))
    check("g5_gaps.image_corner", px(im_g5, 10, 10) == GRID_COLORS[0], "首图左上角落在 (10,10)",
          px(im_g5, 10, 10))

# ---- G6：网格 JPG 白底（无 alpha、透明间隙/边距压白）
exp = expected_grid([GRID_SMALL] * 4, 2, 2, [], 12, 12, 16)
opts = ["--layout", "g", "--rows", "2", "--cols", "2", "--row-gap", "12", "--col-gap", "12",
        "--margin", "16", "--bg", "#00000000", "--format", "jpg"]
out_g6, im_g6 = grid_compose("g6_jpg_white", opts, GRID_FILES_4, exp, "grid_jpg_white.jpg")
if im_g6 is not None:
    rgb6 = im_g6.convert("RGB")
    check("g6_jpg_white.mode_no_alpha", im_g6.mode in ("RGB", "L"), "RGB 无 Alpha", im_g6.mode)
    gap_rgb = px(rgb6, 16 + 40 + 6, 16 + 20 + 6)
    check("g6_jpg_white.gap_white", all(c >= 248 for c in gap_rgb), "间隙压白 ≈(255,255,255)", gap_rgb)
    corner_rgb = px(rgb6, 0, 0)
    check("g6_jpg_white.margin_white", all(c >= 248 for c in corner_rgb), "边距压白", corner_rgb)
    bad = []
    for i, rect in enumerate(exp["items"]):
        got = px(rgb6, int(rect[0] + rect[2] // 2), int(rect[1] + rect[3] // 2))
        if not close_rgba(got, GRID_COLORS[i][:3], 12):
            bad.append((i, got))
    check("g6_jpg_white.centers", not bad, "4 个区域中心颜色近似（JPEG 容差 12）", bad or "ok")

# ---- G7：行/列间距缺省回落 --gap、非法值 clamp/fallback
small4 = GRID_FILES_4
base_opts = ["--layout", "g", "--rows", "2", "--cols", "2", "--margin", "16",
             "--bg", "#00000000", "--format", "png"]
case = expected_grid([GRID_SMALL] * 4, 2, 2, [], 30, 30, 16)
_, im7a = grid_compose("g7a_gap_fallback30", base_opts + ["--gap", "30"], small4, case,
                       "grid_gap_fallback30.png")
check("g7a_gap_fallback30.both30", (case["cw"], case["ch"]) == (2 * 16 + 2 * 40 + 30, 2 * 16 + 2 * 20 + 30),
      "row/col gap 均回落 30 → %dx%d" % (case["cw"], case["ch"]), (case["cw"], case["ch"]))
case = expected_grid([GRID_SMALL] * 4, 2, 2, [], 0, 30, 16)
grid_compose("g7b_row_override0", base_opts + ["--gap", "30", "--row-gap", "0"], small4, case,
             "grid_row_gap_override0.png")
case = expected_grid([GRID_SMALL] * 4, 2, 2, [], 0, 12, 16)
grid_compose("g7c_row_gap_negative", base_opts + ["--row-gap", "-5"], small4, case,
             "grid_row_gap_negative.png")
check("g7c_row_gap_negative.clamp0", case["row_gap"] == 0, "row-gap -5 clamp 到 0", case["row_gap"])
case = expected_grid([GRID_SMALL] * 4, 2, 2, [], 12, 200, 16)
grid_compose("g7d_col_gap_clamp", base_opts + ["--col-gap", "999"], small4, case,
             "grid_col_gap_clamp.png")
check("g7d_col_gap_clamp.clamp200", case["col_gap"] == 200, "col-gap 999 clamp 到 200", case["col_gap"])
case = expected_grid([GRID_SMALL] * 4, 2, 2, [], 12, 12, 16)
grid_compose("g7e_row_gap_invalid", base_opts + ["--row-gap", "abc"], small4, case,
             "grid_row_gap_invalid.png")
check("g7e_row_gap_invalid.fallback12", case["row_gap"] == 12, "row-gap 非法回落 12", case["row_gap"])

# ---- G8：--merge 1 基闭区间 + 两个合法合并（3×3：左上 2×2 + 右侧竖 1×2）
merge_text_valid = "1,1,2,2;2,3,3,3"
merges_valid = [(0, 0, 1, 1), (1, 2, 2, 2)]
exp = expected_grid(sizes_5, 3, 3, merges_valid, 12, 12, 16)
log("G8 expected: %dx%d capacity=%d placed=%d regions=%s" % (
    exp["cw"], exp["ch"], exp["capacity"], exp["placed"], exp["all_regions"]))
opts = ["--layout", "g", "--rows", "3", "--cols", "3", "--row-gap", "12", "--col-gap", "12",
        "--margin", "16", "--merge", merge_text_valid, "--bg", "#00000000", "--format", "png"]
out_g8, im_g8 = grid_compose("g8_merge_two", opts, files_5, exp, "grid_merge_two.png")
check("g8_merge_two.capacity5", exp["capacity"] == 5, "容量 5（9-3-1）", exp["capacity"])
if im_g8 is not None:
    check_grid_centers("g8_merge_two", out_g8, exp, GRID_COLORS[:5])
    vmerge = exp["items"][2]
    check("g8_merge_two.vertical_merge_rect", vmerge[3] == 2 * exp["cell_h"] + exp["row_gap"],
          "竖合并高 = 2*cell_h + row_gap", vmerge)

# ---- G9：--merge 非法项容错（仅第一个合法 2×2 生效，其余越界/重叠/单格/非数字全部丢弃）
merge_text_tolerant = ("1,1,2,2;4,4,1,1;0,0,1,1;abc;20,1,21,2;2,1,2,2;1,2,2,3;1,1,2,2")
sizes_6 = GRID_SIZES_9[:6]
files_6 = GRID_FILES_9[:6]
exp = expected_grid(sizes_6, 3, 3, [(0, 0, 1, 1)], 12, 12, 16)
opts = ["--layout", "g", "--rows", "3", "--cols", "3", "--row-gap", "12", "--col-gap", "12",
        "--margin", "16", "--merge", merge_text_tolerant, "--bg", "#00000000", "--format", "png"]
out_g9, im_g9 = grid_compose("g9_merge_tolerant", opts, files_6, exp, "grid_merge_tolerant.png")
check("g9_merge_tolerant.exit0_valid", exp["capacity"] == 6 and exp["placed"] == 6,
      "非法项被丢弃，容量 6 / 放入 6（退出码 0）", (exp["capacity"], exp["placed"]))
if im_g9 is not None:
    check_grid_centers("g9_merge_tolerant", out_g9, exp, GRID_COLORS[:6])

# ---- G10：指定宽度（--out-width）整体缩放：主尺寸精确 500，另一维按比例
exp = expected_grid(GRID_SIZES_9, 3, 3, [], 12, 12, 16, mode="width", target=500)
log("G10 expected: %dx%d (natural 956x536)" % (exp["cw"], exp["ch"]))
opts = ["--layout", "g", "--rows", "3", "--cols", "3", "--row-gap", "12", "--col-gap", "12",
        "--margin", "16", "--out-width", "500", "--bg", "#00000000", "--format", "png"]
out_g10, im_g10 = grid_compose("g10_out_width", opts, GRID_FILES_9, exp, "grid_out_width.png")
check("g10_out_width.exact_width", exp["cw"] == 500, "主尺寸宽精确 500", exp["cw"])
if im_g10 is not None:
    check_grid_centers("g10_out_width", out_g10, exp, GRID_COLORS)

# ---- G11：指定高度（--out-height）整体缩放
exp = expected_grid(GRID_SIZES_9, 3, 3, [], 12, 12, 16, mode="height", target=400)
log("G11 expected: %dx%d" % (exp["cw"], exp["ch"]))
opts = ["--layout", "g", "--rows", "3", "--cols", "3", "--row-gap", "12", "--col-gap", "12",
        "--margin", "16", "--out-height", "400", "--bg", "#00000000", "--format", "png"]
out_g11, im_g11 = grid_compose("g11_out_height", opts, GRID_FILES_9, exp, "grid_out_height.png")
check("g11_out_height.exact_height", exp["ch"] == 400, "主尺寸高精确 400", exp["ch"])
if im_g11 is not None:
    check_grid_centers("g11_out_height", out_g11, exp, GRID_COLORS)

# ---- G12：自动单元格 = 宽/高分别取中位数（偶数个取平均后 .5 远离 0 四舍五入）
sizes_even = [(100, 50), (200, 100), (301, 151), (400, 200)]
files_even = [grid_fixture("ge-%d-%dx%d.png" % (i, s[0], s[1]), s, GRID_COLORS[i])
              for i, s in enumerate(sizes_even)]
exp = expected_grid(sizes_even, 2, 2, [], 12, 12, 16)
opts = ["--layout", "g", "--rows", "2", "--cols", "2", "--row-gap", "12", "--col-gap", "12",
        "--margin", "16", "--bg", "#00000000", "--format", "png"]
out_g12, im_g12 = grid_compose("g12_median_even", opts, files_even, exp, "grid_median_even.png")
check("g12_median_even.cell", (exp["cell_w"], exp["cell_h"]) == (251, 126),
      "cellW=median(100,200,301,400)=250.5→251；cellH=125.5→126", (exp["cell_w"], exp["cell_h"]))
if im_g12 is not None:
    check_grid_centers("g12_median_even", out_g12, exp, GRID_COLORS[:4])
    gap_mid = px(im_g12, 16 + 251 + 6, 16 + 63)
    check("g12_median_even.col_gap_position", gap_mid[3] == 0,
          "列间距位于 x=16+cellW 之后（cellW=251）", gap_mid)

# ---- G13：网格模式 --base 不生效（仅横/纵使用自定义基准）
exp = expected_grid([GRID_SMALL] * 4, 2, 2, [], 0, 0, 0)
opts = ["--layout", "g", "--rows", "2", "--cols", "2", "--row-gap", "0", "--col-gap", "0",
        "--margin", "0", "--base", "100", "--bg", "#00000000", "--format", "png"]
out_g13, im_g13 = grid_compose("g13_base_ignored", opts, GRID_FILES_4, exp, "grid_base_ignored.png")
check("g13_base_ignored.base_ignored", (exp["cw"], exp["ch"]) == (80, 40),
      "--base 100 被忽略，输出仍 80×40", (exp["cw"], exp["ch"]))
if im_g13 is not None:
    check_grid_centers("g13_base_ignored", out_g13, exp, GRID_COLORS[:4])

# ---- G14：行/列越界 clamp（--rows 0 → 1，--cols 99 → 20），9 张图全部放入第 1 行
exp = expected_grid(GRID_SIZES_9, 0, 99, [], 12, 12, 16)
log("G14 expected: %dx%d rows=1 cols=20 capacity=%d" % (exp["cw"], exp["ch"], exp["capacity"]))
opts = ["--layout", "g", "--rows", "0", "--cols", "99", "--row-gap", "12", "--col-gap", "12",
        "--margin", "16", "--bg", "#00000000", "--format", "png"]
out_g14, im_g14 = grid_compose("g14_rows_cols_clamp", opts, GRID_FILES_9, exp, "grid_rows_cols_clamp.png")
check("g14_rows_cols_clamp.clamped", (exp["cw"], exp["ch"]) == (6260, 192),
      "rows clamp 1 / cols clamp 20 → 6260×192", (exp["cw"], exp["ch"]))
if im_g14 is not None:
    check_grid_centers("g14_rows_cols_clamp", out_g14, exp, GRID_COLORS)

# ---- G15：自定不透明背景色 + 图片少于容量（空位 = 背景色）
exp = expected_grid([GRID_SMALL, GRID_SMALL], 2, 2, [], 0, 0, 4)
opts = ["--layout", "g", "--rows", "2", "--cols", "2", "--row-gap", "0", "--col-gap", "0",
        "--margin", "4", "--bg", "#FF112233", "--format", "png"]
out_g15, im_g15 = grid_compose("g15_bg_empty", opts, GRID_FILES_4[:2], exp, "grid_bg_empty.png")
check("g15_bg_empty.expected_size", (exp["cw"], exp["ch"]) == (88, 48), "88×48", (exp["cw"], exp["ch"]))
if im_g15 is not None:
    check_grid_centers("g15_bg_empty", out_g15, exp, GRID_COLORS[:2])
    check_grid_slot_bg("g15_bg_empty", im_g15, exp, [2, 3], bg=(0x11, 0x22, 0x33, 255))
    corner = px(im_g15, 0, 0)
    check("g15_bg_empty.margin_bg", corner == (0x11, 0x22, 0x33, 255), "边距 = 自定背景色", corner)

# ---- G16：--layout grid 长写法 + --gap 用于行/列间距（默认 12 回落验证）
exp = expected_grid([GRID_SMALL] * 4, 2, 2, [], 0, 0, 0)
opts = ["--layout", "grid", "--rows", "2", "--cols", "2", "--gap", "0", "--margin", "0",
        "--bg", "#00000000", "--format", "png"]
out_g16, im_g16 = grid_compose("g16_layout_grid", opts, GRID_FILES_4, exp, "grid_layout_long_form.png")
if im_g16 is not None:
    check_grid_centers("g16_layout_grid", out_g16, exp, GRID_COLORS[:4])

# ---------------------------------------------------------------- 汇总
summary = {"exe": EXE, "exe_size": exe_size, "exe_sha256": exe_sha,
           "total": len(results), "pass": sum(1 for r in results if r["pass"]),
           "fail": sum(1 for r in results if not r["pass"]), "results": results}
os.makedirs(OUTDIR, exist_ok=True)
log("")
log("SUMMARY total=%d pass=%d fail=%d" % (summary["total"], summary["pass"], summary["fail"]))
for r in results:
    if not r["pass"]:
        log("FAILED: %s | expected=%s actual=%s" % (r["name"], r["expected"], r["actual"]))
with open(RESULT_JSON, "w", encoding="utf-8") as f:
    json.dump(summary, f, ensure_ascii=False, indent=2)
with open(RESULT_LOG, "w", encoding="utf-8") as f:
    f.write("\n".join(log_lines) + "\n")
# 同时归档一份到 docs/qa/logs（验收报告引用的证据路径；仅本机生成、未入库）
try:
    import shutil
    archive_dir = os.path.join(ROOT, "docs", "qa", "logs")
    os.makedirs(archive_dir, exist_ok=True)
    shutil.copyfile(RESULT_JSON, os.path.join(archive_dir, "cli-results.json"))
    shutil.copyfile(RESULT_LOG, os.path.join(archive_dir, "cli-results.log"))
    print("archived -> docs/qa/logs/cli-results.{json,log}")
except Exception as e:  # noqa
    print("archive failed: %r" % (e,))
sys.exit(0 if summary["fail"] == 0 else 1)
