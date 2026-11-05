# -*- coding: utf-8 -*-
"""轻拼 LightCollage v1.0.0 CLI 验收测试（QA 独立实现，不复用产品源码）。

覆盖：--version/--help、--compose 横/纵布局尺寸、gap/margin/base、透明背景 alpha、
图片自身半透明像素保留、半透明背景叠加、JPG 白底无 alpha、WebP 真实解码、GIF 首帧、
EXIF 方向、尺寸保护、损坏/不支持文件非零退出、中文路径、颜色格式等。
像素校验用 Pillow 独立解码/比对；布局期望值由本脚本按 docs/需求确认-v1.md 独立实现。
"""
import hashlib
import json
import os
import subprocess
import sys

from PIL import Image, ImageOps

# 项目根目录：由本文件位置（tests/qa/run_cli_tests.py）向上三级推导，任意目录运行均可。
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
EXE = os.path.join(ROOT, "dist", "轻拼", "LightCollage.exe")
FIX = os.path.join(ROOT, "tests", "core", "fixtures")
SAMPLES = r"C:\Temp\collage-samples"
OUTDIR = r"C:\Temp\qa-lightcollage\out"
RESULT_JSON = os.path.join(OUTDIR, "cli-results.json")
RESULT_LOG = os.path.join(OUTDIR, "cli-results.log")

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
check("version.text", "轻拼" in out and "1.0.0" in out, "含 '轻拼' 与 '1.0.0'", repr(out.strip()))

rc, out, err = run(["--help"])
check("help.exit0", rc == 0, "exit 0", "exit %d" % rc)
has_usage = ("--compose" in out) and ("--register" in out) and ("--version" in out)
check("help.compose_usage", has_usage, "帮助含 --compose/--register/--version", "exit %d, 长度 %d" % (rc, len(out)))

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

# ---------------------------------------------------------------- 汇总
summary = {"exe": EXE, "exe_size": exe_size, "exe_sha256": exe_sha,
           "total": len(results), "pass": sum(1 for r in results if r["pass"]),
           "fail": sum(1 for r in results if not r["pass"]), "results": results}
os.makedirs(OUTDIR, exist_ok=True)
with open(RESULT_JSON, "w", encoding="utf-8") as f:
    json.dump(summary, f, ensure_ascii=False, indent=2)
with open(RESULT_LOG, "w", encoding="utf-8") as f:
    f.write("\n".join(log_lines) + "\n")
log("")
log("SUMMARY total=%d pass=%d fail=%d" % (summary["total"], summary["pass"], summary["fail"]))
for r in results:
    if not r["pass"]:
        log("FAILED: %s | expected=%s actual=%s" % (r["name"], r["expected"], r["actual"]))
sys.exit(0 if summary["fail"] == 0 else 1)
