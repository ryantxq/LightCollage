# 参与贡献 · 轻拼 LightCollage

感谢你关注轻拼（LightCollage）！本文档说明如何搭建开发环境、构建、测试，以及提交改动的约定和流程。

如果你只是想使用这个工具，请看 [README.md](README.md)；想下载发布包，请到
[Releases](https://github.com/ryantxq/LightCollage/releases)。

## 可以贡献什么

- **Bug 修复**：附复现步骤与截图，先开 Issue 描述问题。
- **新功能**：请先开 Issue 讨论范围与做法，避免大改动做完后因方向不符被拒。
- **文档改进**：README、`scripts/使用说明.txt`、FAQ、注释中的错别字与不准确描述。
- **测试补充**：`tests/core/` 单元测试、`tests/qa/` 端到端用例。
- **兼容性验证**：不同 Windows 版本、DPI 缩放、多显示器、不同图片格式/扩展组合的实测反馈。

## 环境要求

| 项目 | 要求 |
|---|---|
| 操作系统 | Windows 10（1903+）/ Windows 11 |
| 编译器 | 系统自带 .NET Framework 4.8 的 csc：`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` |
| PowerShell | Windows 10 / 11 自带的 Windows PowerShell 5.1 或更高版本 |
| 构建工具链 | **不需要** Visual Studio、dotnet SDK、NuGet，也不需要额外安装 .NET 运行包 |
| 可选：Python | Python 3 + Pillow（生成/校验测试图片、生成图标） |
| 可选：交互桌面 | `tests/qa/` 的 UI 自动化需要在真实的交互式桌面会话中运行 |

## 获取代码与构建

```powershell
git clone https://github.com/ryantxq/LightCollage.git
cd LightCollage

# 一键构建（无需任何参数）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1
```

构建产物：

- `build\LightCollage.exe`：单文件主程序
- `dist\轻拼\`：发布文件夹（exe + 安装/卸载脚本 + `使用说明.txt`）
- `dist\轻拼-右键拼图-v1.0.0.zip`：发布压缩包

`build/`、`dist/` 都是生成物，已被 `.gitignore` 忽略，**不要提交**。

> 请始终使用 `scripts\build.ps1` 构建：它负责用通配符收集 `src/**/*.cs`、拼装编译引用、
> 嵌入图标与清单，并生成发布目录和 ZIP。手工拼一条 csc 命令行很容易漏掉这些参数。

## 测试

### 核心单元测试（必须通过）

```powershell
# 生成测试图 -> csc 编译 -> 运行；退出码 0 = 全部通过，1 = 有失败，2 = 环境/编译问题
powershell -NoProfile -ExecutionPolicy Bypass -File tests\core\run-core-tests.ps1
```

- 覆盖布局与输出尺寸、像素/透明度、EXIF 方向、GIF/WebP 解码、设置读写、尺寸保护等。
- 测试图由 `tests/core/make_test_images.py` 用 Pillow 生成（`-SkipFixtures` 可跳过重新生成）。
- 完整输出保存在 `tests/core/last-run.log`（本地运行产物，不提交）。
- 详细说明见 [tests/core/README.md](tests/core/README.md)。

### 端到端 CLI 测试

```powershell
# 需要 Python 3 + Pillow
python tests\qa\run_cli_tests.py
```

也可以手动用 `--compose` 验证一次合成：

```powershell
build\LightCollage.exe --compose --layout h --gap 12 --margin 16 --bg "#00000000" `
    --base auto --format png --out out.png a.png b.png
# 成功时 stdout 输出：OK <W>x<H> -> <path>
```

### UI 自动化（需要交互桌面）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests\qa\run_ui_tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests\qa\run_ui_supplement.ps1
```

UI 自动化会启动/结束 `LightCollage.exe`、操作真实窗口与注册表菜单，**只能在有交互桌面的 Windows
会话中运行，CI 不会执行**。运行前请关闭已打开的工具实例。

## 代码约定

### 语言与框架

- **C# 5**（目标 .NET Framework 4.8）。禁止 C# 6 及更高版本语法，包括但不限于：
  字符串插值 `$"{x}"`、`?.`、`??=`、表达式体成员（`=>` 方法/属性）、`nameof`、
  自动属性初始化器（`{ get; set; } = x`）、元组、模式匹配、`using static`、`out var`。
- 允许 `async` / `await`、`Task`、`IProgress<T>` 等 C# 5 已支持的特性。
- 只能引用系统程序集（`System`、`System.Core`、`System.Drawing`、`System.Windows.Forms`、
  `System.Xaml`、WPF `WindowsBase` / `PresentationCore`），**不引入 NuGet、第三方 DLL 或外部 exe**。
- 所有代码文件放在 `src/` 下；`scripts/build.ps1` 用通配符收集 `src/**/*.cs`，不需要维护文件清单。

### 编码与文案

- 源码统一 UTF-8；构建命令已包含 `/codepage:65001`。
- 界面中文文案**直接写在代码里**，不引入资源文件或多语言框架。
- 用户可见的错误提示要可读，并给出下一步动作（例如提示安装哪个 WIC 解码扩展）。

### 稳定性与安全

- 不联网、无遥测、无自动更新；不请求管理员权限。
- 只写 HKCU 和 `%APPDATA%` / `%LOCALAPPDATA%`，不写 HKLM、不写系统目录、不改文件关联。
- 不锁定用户图片文件；解码/合成失败应给出中文提示而不是崩溃。
- 下载、安装、注册表等路径处理要考虑中文路径与空格。

### 共享契约（冻结）

- `src/Common/Models.cs` 的公开模型（`CollageSettings`、`CollageLayout`、`AppInfo` 等）已冻结，
  不要修改公开签名；确需变更请先开 Issue 与维护者确认。
- 各模块 API 契约见 [docs/interfaces.md](docs/interfaces.md)。
- `src/Shell/MenuInstaller.cs` 的注册表路径、动词名（`LightCollage`）、菜单文字（`拼图`）等
  属于对外行为，不要随意改动。
- 版本号唯一来源是 `src/Common/Models.cs` 中的 `AppInfo.Version`。

### 提交前检查

- `scripts\build.ps1` 构建通过（无 csc 错误）。
- `tests\core\run-core-tests.ps1` 全部通过。
- 涉及 Core / CLI 行为的改动，跑一下 `tests\qa\run_cli_tests.py`。
- 涉及 UI / 菜单的改动，附上前后截图。
- 不提交 `build/`、`dist/`、`tests/core/bin/`、`tests/core/last-run.log`、`docs/qa/` 下的本机证据等生成物。

## 提交 Pull Request

1. **Fork** 本仓库并创建分支，分支名建议 `feat/xxx`、`fix/xxx`、`docs/xxx`、`test/xxx`。
2. **一个 PR 只做一件事**，避免混入无关的格式化、重排或大范围改名。
3. **Commit message 说明「为什么」**，例如：
   - `fix: 修复 gap=0 时相邻图片出现 1px 透明缝`
   - `docs: 补充 Win11 右键菜单显示更多选项的说明`
4. **PR 描述**包含：改动内容、动机、测试方式（贴出命令与结果）、关联 Issue（如 `Fixes #123`）；
   UI 改动请附截图。
5. **确认 CI 通过**：`.github/workflows/build.yml` 会在 `windows-latest` 上执行构建与核心单元测试；
   CI 上传的 ZIP 产物可在 Actions 页面下载复核。
6. 维护者会进行 Review，请根据意见修订；合并后你的贡献会随下一个版本发布。

## 问题反馈

- 先查 [README 常见问题 FAQ](README.md#常见问题-faq) 和已有的
  [Issues](https://github.com/ryantxq/LightCollage/issues)，避免重复。
- 新建 Issue：<https://github.com/ryantxq/LightCollage/issues/new/choose>，优先使用 Bug 报告模板。
- 请附上：
  - Windows 版本（`winver` 查看）；
  - 工具版本（「关于」窗口，或 `LightCollage.exe --version`）；
  - 复现步骤、期望结果、实际结果；
  - 截图或报错文本；涉及解码问题请说明图片格式与来源。
- 程序异常会把错误详情写入 `%TEMP%\LightCollage-error.log`，反馈时可一并提供。

## 文档与发布（维护者）

- 用户文档：`README.md`、`scripts/使用说明.txt`（随发布包分发）、[CONTRIBUTING.md](CONTRIBUTING.md)。
- 本地构建的发布 ZIP 由 `scripts/build.ps1` 生成，命名 `轻拼-右键拼图-v<版本>.zip`；
  发布到 GitHub Release 时会重命名为 ASCII 的 `LightCollage-v<版本>-windows.zip`，方便链接与下载。
- 打 tag（如 `v1.0.0`）会触发 `.github/workflows/release.yml` 自动构建并创建 GitHub Release。

感谢你的贡献！
