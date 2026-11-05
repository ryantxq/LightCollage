# tests/qa · 轻拼独立验收测试（QA 编写）

本目录为独立 QA 验收使用，不参与产品构建；`src/`、`scripts/`、`assets/` 未被修改。

## 内容

| 文件 | 说明 |
|---|---|
| `run_cli_tests.py` | CLI 验收：**v1.0 回归**（77 断言：布局尺寸、透明 PNG alpha、半透明源图、JPG 白底、EXIF 方向、GIF 首帧、WebP、尺寸保护、损坏/不支持文件退出码、中文路径等）+ **v1.1 网格**（113 断言：`--layout g/--rows/--cols/--row-gap/--col-gap/--merge/--out-width/--out-height`，独立期望公式 + Pillow 像素：行优先、合并跨间距、容量截断/未放入、末尾空格、gap=0 无缝、行列间距、JPG 白底、`--gap` 回落、`--merge` 1 基容错、中位数取整、`--base` 忽略、行列 clamp）。需要 Python 3 + Pillow。 |
| `run_ui_grid.ps1` | **v1.1 新增（144 断言）**：网格 UI 链路验收（三段切换、网格设置区显隐、行列/间距、合并弹窗矩阵拖选/合并/拆分/清空/取消、容量不足「未放入」、JPG 白底、网格/横向/纵向预览拖动对调、无效目标/Esc、设置记忆重启、辅助线仅预览、旧/损坏配置、1×1/20×20 边界、单实例追加、横向/纵向 v1.0 零回归、最小窗口/200% DPI、性能原始数据）。依赖 QaTool（v1.1 新命令 `matrixdrag`/`previewdrag`/`segwait segmentCount`）。截图写入 `docs/qa/screenshots/v11-*.png`，日志 `docs/qa/logs/ui-grid.*`。 |
| `run_ui_tests.ps1` | UI 主套件（v1.0 链路回归，111 断言）：默认值/保存/设置交互/编辑/排序/单图/损坏图/60 图/最小窗口/性能。**v1.1 已适配三段控件与 v1.0 第二轮现行 UI（输出尺寸模式/无 hex 色值/灰度预设）；另存为改为打开+取消（自定义路径为已知环境限制）。** |
| `run_ui_supplement.ps1` | 补充验证套件（54 断言）：保存按钮/键盘保存/另存为对话框/三段切换/间距步进/输出尺寸模式/JPG/60 图/性能；已修正旧脚本的 Expected-Size 逗号优先级 bug、模态对话框未关闭级联、60 图实例隔离等。 |
| `imgcheck.py` | 像素校验辅助：`info`/`pixel`/`alpha`/`diff`/`center`/**`present`**（检查指定 RGBA 是否出现在图中，用于「未放入」断言）。 |
| `bin/QaTool.cs` | QA 自动化小工具源码（截图/UIA/鼠标键盘/菜单端到端/计时；v1.1 新增 `segcount`/`matrixdrag`/`previewdrag`）。 |
| `bin/QaTool.exe` | 本机编译的 QaTool 可执行文件，**未纳入版本库**；首次使用请用系统 csc 从 `bin/QaTool.cs` 编译（命令见下方「运行」）。 |

## 运行

```powershell
# 首次运行前：编译 QaTool（QaTool.exe 未入库；用系统 .NET Framework 4.8 的 csc）
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
& "$fw\csc.exe" /nologo /target:exe /platform:anycpu /codepage:65001 /utf8output `
    /out:tests\qa\bin\QaTool.exe tests\qa\bin\QaTool.cs `
    "/r:$fw\WPF\UIAutomationClient.dll" "/r:$fw\WPF\UIAutomationTypes.dll" "/r:$fw\WPF\WindowsBase.dll"

# CLI（默认被测 dist\轻拼\LightCollage.exe；可用 QA_EXE 覆盖，便于集成前用临时编译 exe 预跑）
python tests\qa\run_cli_tests.py
$env:QA_EXE = 'C:\Temp\prebuild\LightCollage.exe'; python tests\qa\run_cli_tests.py

# 网格 UI 套件（v1.1；会启动/结束 LightCollage 进程，**需桌面独占**）
powershell -NoProfile -ExecutionPolicy Bypass -File tests\qa\run_ui_grid.ps1

# UI 主套件 / 补充套件（v1.0 回归，建议无其它实例运行时执行）
powershell -NoProfile -ExecutionPolicy Bypass -File tests\qa\run_ui_tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests\qa\run_ui_supplement.ps1
```

结果与截图默认写入 `docs/qa/logs/`、`docs/qa/screenshots/` 及 `C:\Temp\qa-lightcollage\`；其中前两者为**仅本机生成、未纳入版本库**（已在 `.gitignore` 中忽略），仓库中仅保留 `docs/qa/验收报告*.md`。

UI 脚本自动探测 Python：优先环境变量 `QA_PYTHON`，其次 `PATH` 中的 `python`（需 Python 3 + Pillow）；均找不到时给出中文提示并退出。

## v1.1 网格测试要点（便于复测/复核）

1. **独立期望公式**：`run_cli_tests.py` 的 `expected_grid()` 与 `run_ui_grid.ps1` 的 `Expected-Grid` 按 `docs/需求确认-网格布局拼图-v1.1.md` 第 5 节与 `docs/interfaces.md` §10.3 **独立实现**（中位数取整、行优先区域顺序、合并容量、几何、指定宽/高边界取整），不复用产品源码。
2. **三段控件**：拼接方式由 2 段变 3 段（index 0/1/2 = 横向/纵向/网格）。QaTool `segwait` 第 6 个参数为 segmentCount，**「拼接方式」必须传 3**（旧脚本已更新）；输出格式选择等 2 段控件仍传 2 或不传。
3. **合并弹窗**：矩阵控件 AccessibleName=`网格矩阵`，QaTool `matrixdrag <dialogTitle> <rows> <cols> <r1> <c1> <r2> <c2>` 直接从 UIA rect 计算单元格中心拖选；弹窗标题 `编辑网格合并`。
4. **预览拖动**：`PreviewPanel.AccessibleName=预览区`，QaTool `previewdrag <title> <canvasW> <canvasH> <fromOutX> <fromOutY> <toOutX> <toOutY> [ms] [esc]` 把**输出像素坐标**按预览等比缩放换算成屏幕坐标后拖拽；`esc` 模式用于验证 Esc 取消。
5. **桌面独占**：UI 套件会 `App-Kill` 所有 `LightCollage*` 进程并使用前台窗口/SendKeys；**与其它成员并行跑 UI 自动化会互相干扰**（单实例窗口同名、抢占前台）。跑前确认无其它 UI 测试/人工操作。
6. **容量不足证据**：计数行 `· 未放入 X 张`（X=N−K，仅 N>K 时出现）+ 缩略图角标截图 + 保存输出中「未放入」图颜色不出现（`imgcheck.py present`）。

## 已知自动化注意事项（便于复测）

1. **UIA 对自绘控件只暴露整体 Name**（如 `拼接方式`、`保存图片  Ctrl+S`），无法枚举分段控件内部文字；方向/模式请以内容标签（`共 N 张图片 · 横向/纵向拼接`、`网格拼接 R×C`）和截图判断。
2. **Stepper 的 +/− 点击热区固定 28 物理像素**（源码硬编码，不随 DPI 放大）：200% DPI 下减号中心约为控件矩形左边 +18 物理像素，不要按逻辑像素乘 DPI 计算，否则会点到热区外（曾导致“减号无效”误报）。
3. **SendKeys/QaTool 需要 STA 线程与前台窗口**：QaTool 已加 `[STAThread]`；跨进程点击前先 `bring`，必要时在单进程内连点（`clickseq`）。
4. **原生对话框**（OpenFileDialog/SaveFileDialog/ColorDialog）UIA 结构：打开对话框文件名 `Edit` id=`1148`；保存对话框 `Edit` id=`1001`、名“文件名:”；颜色对话框 `Edit` 名“红(R):/绿(G):/蓝(U):”。UIA `SetValue`/`SendKeys` 写入保存路径在部分环境不生效，建议人工复核“另存为自定义路径”。
5. **UIA 轮询计时开销大**：不要用 `.FindAll` 轮询测性能；QaTool `uistart` 已改为缓存元素轮询，但仍不等价于产品内计时。
6. **菜单截图**：Win11 新菜单项加载有抖动（第三方 shell 扩展），需用 UIA 动态定位「显示更多选项」，不要写死坐标。
7. **关闭主窗**：加载图片后若有未保存改动（`_dirty`），`WM_CLOSE` 会弹「还有未保存的拼图，确定关闭吗？」；脚本需等待 `轻拼` 对话框并点「是」才能正常保存窗口/设置（`run_ui_grid.ps1` 的 `Stop-AppGraceful` 已处理）。
