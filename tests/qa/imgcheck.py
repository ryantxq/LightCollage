# -*- coding: utf-8 -*-
"""轻拼 QA 图像校验辅助（Pillow）。用法：
  python imgcheck.py info <file>         -> JSON {ok,size,mode,extrema}
  python imgcheck.py pixel <file> <x> <y> -> JSON {ok,rgba}
  python imgcheck.py diff <a> <b>        -> JSON {ok,same,diff,total,maxdiff}
  python imgcheck.py alpha <file>        -> JSON {ok,min,max}
  python imgcheck.py center <file> <n>   -> JSON {ok,colors:[...]}  # 横向 n 等分中心色
"""
import json
import sys

from PIL import Image, ImageChops


def out(obj):
    print(json.dumps(obj, ensure_ascii=True))


def main():
    try:
        cmd = sys.argv[1]
        if cmd == "info":
            im = Image.open(sys.argv[2])
            im.load()
            info = {"ok": True, "size": list(im.size), "mode": im.mode}
            if im.mode in ("RGBA", "LA", "P"):
                info["extrema"] = im.convert("RGBA").getextrema()
            else:
                info["extrema"] = im.getextrema()
            out(info)
        elif cmd == "pixel":
            im = Image.open(sys.argv[2]).convert("RGBA")
            rgba = im.getpixel((int(sys.argv[3]), int(sys.argv[4])))
            out({"ok": True, "rgba": list(rgba)})
        elif cmd == "alpha":
            im = Image.open(sys.argv[2]).convert("RGBA")
            ex = im.getextrema()
            out({"ok": True, "min": ex[3][0], "max": ex[3][1]})
        elif cmd == "diff":
            a = Image.open(sys.argv[2]).convert("RGBA")
            b = Image.open(sys.argv[3]).convert("RGBA")
            if a.size != b.size:
                out({"ok": False, "error": "size mismatch %s vs %s" % (a.size, b.size)})
                return
            diff = ImageChops.difference(a, b)
            ex = diff.getextrema()
            maxdiff = max(x[1] for x in ex)
            # 统计非零像素
            import numpy as np
            arr = np.asarray(diff)
            nonzero = int((arr != 0).any(axis=2).sum()) if arr.ndim == 3 else int((arr != 0).sum())
            out({"ok": True, "same": nonzero == 0, "diff": nonzero, "total": a.size[0] * a.size[1], "maxdiff": int(maxdiff)})
        elif cmd == "center":
            im = Image.open(sys.argv[2]).convert("RGBA")
            n = int(sys.argv[3])
            w, h = im.size
            colors = []
            for i in range(n):
                x = int((i + 0.5) * w / n)
                colors.append(list(im.getpixel((x, h // 2))))
            out({"ok": True, "colors": colors})
        else:
            out({"ok": False, "error": "unknown command " + cmd})
    except Exception as e:  # noqa
        out({"ok": False, "error": "%s: %s" % (type(e).__name__, e)})


if __name__ == "__main__":
    main()
