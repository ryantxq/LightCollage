# 轻拼 LightCollage · Core 单元测试（tests/core）

> Owner：Core 引擎开发。自包含控制台测试，不依赖 dotnet SDK / NuGet / 测试框架；
> 用系统自带 `csc.exe`（C# 5）编译，用 Python + Pillow 生成测试图并校验像素。

## 1. 运行方式

```powershell
# 在工程根目录执行（自动探测 Pillow Python，生成 fixtures → 编译 → 运行 → 写 last-run.log）
powershell -ExecutionPolicy Bypass -File tests\core\run-core-tests.ps1

# 可选参数
#   -Fixtures <dir>   指定测试图目录（默认 tests\core\fixtures）
#   -Python <exe>     指定 Python（默认先取环境变量 QA_PYTHON，其次 PATH 中的 python；需 Python 3 + Pillow）
#   -SkipFixtures     跳过重新生成测试图
```

脚本退出码：`0` 全部通过，`1` 有失败，`2` 环境/编译问题。
每次运行的完整输出保存在 `tests/core/last-run.log`。

## 2. 文件说明

| 文件 | 说明 |
|---|---|
| `CoreTests.cs` | 自包含单元测试（布局/输出尺寸/解码/EXIF/合成/保存/设置） |
| `make_test_images.py` | 用 Pillow 生成测试图（纯色/半透明 PNG、大图、四色块、EXIF 1-8 JPEG、动图 GIF、WebP、损坏文件） |
| `run-core-tests.ps1` | 一键：生成测试图 → csc 编译 → 运行 → 记录日志 |
| `verify_cli_pixels.py` | 用 Pillow 校验 `LightCollage.exe --compose` 输出像素（端到端辅助，见第 6 节） |
| `fixtures/` | 生成的测试图（可随时删除，脚本会重建） |
| `bin/CoreTests.exe` | 测试可执行文件（编译产物） |
| `last-run.log` | 最近一次运行结果（UTF-8） |

## 3. 最新测试结果（第 2 轮迭代后）

- 时间：2026-11-04（Windows 10.0.26300，.NET Framework 4.8，csc 14.8.10001 for C# 5，Pillow 12.3.0 / Python 3.12.14）
- 命令：`powershell -ExecutionPolicy Bypass -File tests\core\run-core-tests.ps1`
- 结果：**PASSED: 69，FAILED: 0，SKIPPED: 0，ALL TESTS PASSED**（详见 `last-run.log`）
- 第 1 轮 51 项全部继续通过；第 2 轮新增 18 项（输出尺寸模式 10、gap=0 无缝 5、设置 3）。
- WebP 解码通过（本机已安装 WebP WIC 扩展；HEIC 实测限制见第 7 节）。

覆盖范围（69 项）：

| 分类 | 项数 | 覆盖点 |
|---|---|---|
| 布局 Layout | 26 | 空列表、单图不缩放、横/纵自动中位数、横/纵坐标与间距（含 88×24 回归）、自定义基准、非法基准回退、gap/margin clamp、单边 32000/8000 万像素上限自动缩小并标记 WasClamped、极端 200 图输入不抛异常、非法尺寸/枚举；**固定宽度/固定高度（横/纵）尺寸精确与另一维比例、gap=0 边界取整严格相接、单图固定宽放大、超单边/超像素自动缩目标、目标<1/非法模式安全回退** |
| 解码 Loader | 11 | 扩展名/过滤器、LoadForEditing 成功路径（预览/缩略图尺寸、32bppArgb）、永不抛异常（null/空/不存在/目录/损坏/假 HEIC/非图片）、假 HEIC 中文提示、不锁文件（可独占打开并删除源文件）、GIF 首帧、预览/缩略图缩放与自身 alpha 保留、EXIF Orientation 1-8 转正（尺寸 + 四色块像素）、编辑态转正尺寸、LoadFull 异常、WebP（WIC）解码 + EXIF 方向 |
| 合成/保存 Compose | 24 | ComposePreview 透明/不透明/半透明背景、JPG 预览压白、预览缩放、空/全失败返回 null、Compose ARGB 像素、横/纵两图实际像素落位、88×24 CLI 回归、JPG 24bpp 白底、PNG 保存重载（半透明背景 alpha=128 精确）、JPG 保存重载（透明填白/半透明背景压白）、进度、保存错误提示、参数校验、预览与保存逐点一致；**gap=0 全分辨率（PNG 放大 2.5×、纵向放大 3×）零半透明像素/无背景色列、预览边界取整无缝、大图缩略图边缘不透明、JPG 白底无背景列** |
| 设置 Settings | 8 | 默认值（含新字段 Auto/2000）、保存/读取往返、损坏文件容错与 clamp、默认路径（%APPDATA%\LightCollage\settings.ini）、写入失败静默；**OutputSizeMode/OutputTarget 往返、旧版 INI 缺失新字段默认 Auto/2000、非法模式回 Auto / target 夹到 1..32000** |

## 4. 第 1 轮关键回归（Lead 集成发现）

`LayoutEngine` 图片坐标错误，已修复并补像素级测试：

1. **方向坐标写反**：横向时曾把 `Y=margin+crossTotal`（纵向逻辑）用于横向；现为
   横向 `X=margin+crossTotal, Y=margin`，纵向 `X=margin, Y=margin+crossTotal`。
2. **间距没计入坐标**：`crossTotal` 只累加图片尺寸，漏加 `gap`，导致第 2 张起坐标偏小 12px（默认）。
   现为 `offset = crossTotal + gap * index`。

复现用例（Lead 提供）：`--layout h --gap 4 --margin 2` 两张 40×20 → 修复后 88×24，两张图分别在 x=2 / x=46，y 均为 2。
对应测试：`Compose/HorizontalTwoImagesPixels`、`Compose/VerticalTwoImagesPixels`、`Compose/CliCase88x24`、
`Layout/HorizontalItemCoordinates`、`Layout/VerticalItemCoordinates`。

## 5. 第 2 轮迭代：输出尺寸模式 + gap=0 无缝

### 5.1 LayoutEngine（`OutputSizeMode` / `OutputTarget`）

- `Auto`：保持原有中位数/自定义基准逻辑不变（旧 51 项测试全部原样通过）。
- `Width`：先按 Auto 逻辑生成自然布局，再整体等比缩放到 `CanvasWidth == OutputTarget`；
  `CanvasHeight` 按比例四舍五入；`Height` 对称（`CanvasHeight == OutputTarget`）。
- 缩放用**边界取整**：`x1=round(X*f)`、`x2=round((X+W)*f)`、`宽=x2-x1`；纵向同理。
  gap=0 时相邻图片边界值完全相同，布局层面坐标严格相接（测试断言 `X[i+1] == X[i]+W[i]`）。
- `BaseSize` 同步缩放；最终比例与所有图片相对位置保留、不裁切。
- 超 80MP / 单边 32000 时自动缩小目标并 `WasClamped=true`；目标 <1、空列表、非法枚举均安全处理。

实测目标尺寸（`FixedSizeCheck` 临时验证程序 + Pillow 保存后逐像素复核）：

| 场景 | 自然布局 | 目标 | 实际输出 | BaseSize | WasClamped |
|---|---|---|---|---|---|
| 两张 40×20 横向，gap=0 margin=0 | 80×20 | Width 200 | **200×50** | 50 | false |
| 两张 40×20 纵向，gap=0 margin=0 | 40×40 | Height 120 | **120×120** | 120 | false |
| 两张 40×20 横向，gap=12 margin=16 | 124×52 | Width 500 | **500×210** | 81 | false |
| 两张 400×300 / 300×600 横向 | 869×482 | Width 500 | **500×277**（比例误差 <0.002） | 259 | false |
| 同上 | 869×482 | Height 300 | **541×300** | 280 | false |
| 3×（100×4000）纵向 | 132×12056 | Width 30000 | 自动缩小到 350×31967（≤32000/≤80MP） | — | **true** |
| 10×（1000×1000）横向 | 10140×1032 | Height 32000 | 自动缩小（像素 ≤80MP） | — | **true** |

### 5.2 gap=0 透明缝修复（Compositor / ComposePreview / ImageLoader）

- **预览**：destination 用边界取整（`round(X*s)` ~ `round((X+W)*s)`），相邻图片预览严格相接。
- **全分辨率合成 + 预览绘制**：`Graphics.DrawImage` 传入 `ImageAttributes(WrapMode.TileFlipXY)`，
  避免 GDI+ 双三次缩放时在图片边界采样透明像素产生半透明缝/背景色列。
- **预览缩略图**：`ImageLoader.ScaleToMaxSide` 同样使用 TileFlipXY，否则大图缩略图边缘 1px 半透明，
  预览 gap=0 时仍会透出背景。
- Alpha/JPG 白底/预览与保存一致性保持第 1 轮行为（CompositingQuality.Default 标准 sRGB 混合）。

gap=0 像素证据（保存 PNG 后重新加载逐一扫描）：

```
# 全分辨率：两张 40x20 红/绿，gap=0，固定宽 200 → 200x50
fixed_width_200 saved=200x50
  translucent=0  nonRedGreen=0  boundary=A255R255G0B0|A255R0G255B0
# 纵向：固定高 120 → 120x120
fixed_height_120 saved=120x120
  translucent=0  nonRedGreen=0  boundary=A255R0G255B0（上下分界行均实色）
# CLI 端到端（全量源码临时编译 exe）：gap=0 无缩放 → 80x20
cli_gap0.png size (80, 20) translucent 0 nonRedGreen 0 boundary (255,0,0,255) (0,255,0,255)
# CLI 端到端：gap=0 + 自定义 base=41（放大，触发 GDI+ 边缘采样）→ 164x41
cli_gap0_scaled.png size (164, 41) translucent 0 nonRedGreen 0 boundary (255,0,0,255) (0,255,0,255)
```

即：**半透明像素数 = 0、非红即绿像素数 = 0、边界两侧均为纯实色**，不存在 1px 透明缝或背景色列。
预览侧同样断言：`Compose/GapZeroPreviewSeam`（旧实现会在边界漏 1px 背景）、
`Compose/GapZeroPreviewFromBigPreviews`（大图缩略图缩放后预览 0 半透明）。

### 5.3 SettingsStore

- 新增写入/读取 `OutputSizeMode=`、`OutputTarget=`（与旧字段并存）。
- 旧版 INI 缺失这两个字段时默认 `Auto` / `2000`，旧字段不受影响。
- clamp：`target` 1..32000；非法模式回 `Auto`；支持数值 0/1/2 与 W/H/A 简写。
- 测试：`Settings/OutputSizeRoundTrip`、`Settings/OldConfigDefaults`、`Settings/OutputSizeClamp`。

## 6. CLI 端到端像素校验记录（第 1 轮，回归保持）

用 `src/` 全量源码在临时目录编译出 `LightCollage.exe`（不改 build/dist），执行：

```powershell
LightCollage.exe --compose --layout h --gap 4 --margin 2 --bg #00000000 --base auto --format png --out out_h.png red40x20.png red40x20.png
LightCollage.exe --compose --layout h --gap 4 --margin 2 --bg #00000000 --base auto --format png --out out_h_semi.png red40x20.png semi40x20.png
LightCollage.exe --compose --layout h --gap 4 --margin 2 --bg #00000000 --base auto --format png --out out_v.png red40x20.png red40x20.png
LightCollage.exe --compose --layout h --gap 0 --margin 0 --bg #00000000 --base auto --format png --out out_gap0.png red40x20.png green40x20.png
LightCollage.exe --compose --layout h --gap 0 --margin 0 --bg #00000000 --base 41 --format png --out out_gap0_scaled.png red40x20.png green40x20.png
python verify_cli_pixels.py <输出目录>
```

结果：`CLI PIXEL CHECK OK`（88×24 / 44×48 / JPG 白底等均符合预期；第 2 轮 gap=0 的 CLI 证据见 5.2 节）。
注：`Program.cs --compose` 目前只有 `--base`，尚无固定宽/高 CLI 参数；第 2 轮固定尺寸验证使用 Core API
（`LayoutEngine.Compute` + `Compositor.Save`）完成，UI 已由 Lead 接入 `OutputSizeMode/OutputTarget`。

## 7. 已知限制 / 集成注意

1. **HEIC 实际解码未在本环境实测**（Pillow 无 HEIC 编码器，无法造样本）。WIC 解码路径已通过 WebP 覆盖；
   `fake.heic` 用于验证“未安装/无法解码 HEIC”的中文提示包含“HEIF 图像扩展”。上真机 HEIC 样本时建议补测。
2. **WIC 解码在独立 STA 线程执行**（部分解码器对套间敏感），单次编码超时 120s。调用方无需关心线程套间。
3. **合成质量使用 `CompositingQuality.Default`**：GDI+ 的 `HighQuality/GammaCorrected` 会做伽马校正混合，
   导致半透明像素偏离标准 sRGB SourceOver 结果。已用 Default + 高质量插值，保证 PNG 半透明像素 alpha 精确
   （128→128）、JPG 白底结果符合预期。**UI 若自绘预览请勿再套一层 GammaCorrected 合成。**
4. **gap=0 无缝依赖 WrapMode.TileFlipXY**：Compositor 已内置；若 UI 自己画预览/图片，请使用同样的
   `ImageAttributes.SetWrapMode(WrapMode.TileFlipXY)`（或直接使用 `ComposePreview`）。
5. **输出尺寸模式**：自然布局仍由“自动基准/自定义基准”决定后再整体等比缩放（因此允许放大）；
   margin/gap 也会随比例缩放；目标被 80MP/32000px 上限缩减时 `WasClamped=true`，UI 已有“已自动缩小”提示。
6. `ComposePreview` 会**跳过** `Success==false` 或 `Preview==null` 的项，布局与返回的 Layout 只对应可用图片；
   UI 只把成功加载的项传入即可。
7. `ImageLoader.LoadForEditing` 内存策略：整文件读入内存解码、立即释放句柄（不锁文件）；单文件上限 1GB、
   解码上限 2 亿像素，超限返回中文错误。
8. `SettingsStore` 支持环境变量 `LIGHTCOLLAGE_SETTINGS_DIR` 覆盖目录，**仅用于测试隔离**，正常使用不要设置。
9. 公开 API 与 `docs/interfaces.md` 第 3 节一致；额外新增 `public sealed class ImageLoadException`（LoadFull
   失败时抛出，Message 为中文），UI 可直接捕获。

## 8. 性能参考（2026-11-04 本机实测，CLI 全流程：全量解码 + 合成 + 编码）

| 场景 | 输出尺寸/像素 | 保存耗时 |
|---|---|---|
| 4 张 4000×3000 JPEG，横向 auto，PNG | 16068×3032 ≈ 4871 万像素 | **1.57 s** |
| 10 张 4000×3000 JPEG，横向 auto，PNG（触发 80MP/32000px 保护） | 31990×2421 ≈ 7745 万像素 | **5.83 s** |
| 同上，JPG（白底） | 31990×2421 | **4.85 s** |

结论：固定尺寸模式下耗时随最终输出像素数变化（与 Auto 模式同一条合成/编码路径）；
≤5000 万像素输出时满足需求中“保存 ≤3s”的目标；接近 80MP 上限时 PNG 编码是主要开销，
会超过 3s（受 GDI+ PNG 编码器限制，非 Core 可优化项）。若验收严格要求 3s，需 Lead 决策是否下调
`LayoutEngine.DefaultMaxPixels`（契约常量）或放宽该场景指标。
另外 CLI `--compose`（Program.cs）会先用 `LoadFull` 取尺寸、保存时再完整解码一次，属二次解码；
UI 主路径不受影响，如需 CLI 更快可改用 `LoadForEditing` 取尺寸。
