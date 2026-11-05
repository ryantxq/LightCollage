# tests/qa · 轻拼独立验收测试（QA 编写）

本目录为独立 QA 验收使用，不参与产品构建；`src/`、`scripts/`、`assets/` 未被修改。

## 内容

| 文件 | 说明 |
|---|---|
| `run_cli_tests.py` | CLI 验收（77 断言）：布局尺寸、透明 PNG alpha、半透明源图、JPG 白底、EXIF 方向、GIF 首帧、WebP、尺寸保护、损坏/不支持文件退出码、中文路径等。需要 Python 3 + Pillow。 |
| `run_ui_tests.ps1` | UI 主套件：默认值/保存/设置交互/编辑/排序/单图/损坏图/60 图/最小窗口/性能。 |
| `run_ui_supplement.ps1` | 针对主套件误报的修正版补充用例（部分用例含测试脚本缺陷，最终结论以 `docs/qa/验收报告.md` 为准；`docs/qa/logs/` 为本机证据、未纳入版本库）。 |
| `imgcheck.py` | 像素校验辅助：`info`/`pixel`/`alpha`/`diff`/`center`。 |
| `bin/QaTool.cs` | QA 自动化小工具源码（截图/UIA/鼠标键盘/菜单端到端/计时）。 |
| `bin/QaTool.exe` | 本机编译的 QaTool 可执行文件，**未纳入版本库**；首次使用请用系统 csc 从 `bin/QaTool.cs` 编译（命令见下方「运行」）。 |

## 运行

```powershell
# 首次运行前：编译 QaTool（QaTool.exe 未入库；用系统 .NET Framework 4.8 的 csc）
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
& "$fw\csc.exe" /nologo /target:exe /platform:anycpu /codepage:65001 /utf8output `
    /out:tests\qa\bin\QaTool.exe tests\qa\bin\QaTool.cs `
    "/r:$fw\WPF\UIAutomationClient.dll" "/r:$fw\WPF\UIAutomationTypes.dll" "/r:$fw\WPF\WindowsBase.dll"

# CLI
python tests\qa\run_cli_tests.py

# UI 主套件（会启动/结束 LightCollage 进程；建议无其它实例运行时执行）
powershell -NoProfile -ExecutionPolicy Bypass -File tests\qa\run_ui_tests.ps1
```

结果与截图默认写入 `docs/qa/logs/`、`docs/qa/screenshots/` 及 `C:\Temp\qa-lightcollage\`；其中前两者为**仅本机生成、未纳入版本库**（已在 `.gitignore` 中忽略），仓库中仅保留 `docs/qa/验收报告.md`。

UI 脚本自动探测 Python：优先环境变量 `QA_PYTHON`，其次 `PATH` 中的 `python`（需 Python 3 + Pillow）；均找不到时给出中文提示并退出。

## 已知自动化注意事项（便于复测）

1. **UIA 对自绘控件只暴露整体 Name**（如 `拼接方式选项`、`保存图片  Ctrl+S`），无法枚举分段控件内部“横向/纵向”文字；方向请以内容标签“共 N 张图片 · 横向/纵向拼接”和截图判断。
2. **Stepper 的 +/− 点击热区固定 28 物理像素**（源码硬编码，不随 DPI 放大）：200% DPI 下减号中心约为控件矩形左边 +18 物理像素，不要按逻辑像素乘 DPI 计算，否则会点到热区外（曾导致“减号无效”误报）。
3. **SendKeys/QaTool 需要 STA 线程与前台窗口**：QaTool 已加 `[STAThread]`；跨进程点击前先 `bring`，必要时在单进程内连点（`clickseq`）。
4. **原生对话框**（OpenFileDialog/SaveFileDialog/ColorDialog）UIA 结构：打开对话框文件名 `Edit` id=`1148`；保存对话框 `Edit` id=`1001`、名“文件名:”；颜色对话框 `Edit` 名“红(R):/绿(G):/蓝(U):”。UIA `SetValue`/`SendKeys` 写入保存路径在部分环境不生效，建议人工复核“另存为自定义路径”。
5. **UIA 轮询计时开销大**：不要用 `.FindAll` 轮询测性能；QaTool `uistart` 已改为缓存元素轮询，但仍不等价于产品内计时。
6. **菜单截图**：Win11 新菜单项加载有抖动（第三方 shell 扩展），需用 UIA 动态定位「显示更多选项」，不要写死坐标。
