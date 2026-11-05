# 轻拼 LightCollage · 接口契约（供并行开发）

> 本文件由 Lead 维护。各成员只修改自己负责的目录；不要改 `src/Common/Models.cs` 的公开签名。需要改契约先告诉 Lead。

## 0. 环境与技术约束

- Windows，当前机器 **没有 dotnet SDK**；使用系统自带编译器：
  `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
- 目标框架：**.NET Framework 4.8**（系统自带，禁止引用 NuGet/第三方 DLL）。
- 语言版本：**C# 5**。禁止使用：字符串插值 `$""`、`?.`、`??=`、表达式体成员、
ameof`、自动属性初始化器、元组、模式匹配、`using static` 等 C# 6+ 语法。允许 `async/await`、`Task`、`IProgress<T>`。
- 源码统一 UTF-8（编译时加 `/codepage:65001`）；中文字符串直接写。
- 编译引用（csc 参数）：
  ```
  /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.dll
  /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Core.dll
  /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Drawing.dll
  /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Windows.Forms.dll
  /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll
  /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll
  /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll
  ```
  （WPF 两个程序集仅给 WebP/HEIC 的 WIC 解码用；`System.Xaml.dll` 是 PresentationCore 的依赖。）
- 所有代码文件放在 `src/` 下；构建脚本会用通配符收集 `src/**/*.cs`，不需要手工维护文件列表。
- 禁止写用户目录以外的系统位置；工具运行时不请求管理员权限。

## 1. 目录与写入范围

| 目录/文件 | Owner | 说明 |
|---|---|---|
| `src/Common/` | Lead | 共享模型、AppInfo；**冻结**，他人不改 |
| `src/Core/` | Core 开发 | 布局/解码/合成/设置 |
| `src/Shell/` | Shell 开发 | 右键菜单/单实例/Shell P/Invoke |
| `src/App/`、`src/Program.cs` | Lead | UI/交互/入口/CLI |
| `scripts/`、`assets/`、`src/AssemblyInfo.cs`、`README.md` | Shell 开发 | 构建/安装/图标/说明 |
| `tests/` | Core 开发 + QA | 单测与端到端测试脚本 |
| `docs/` | Lead（验收报告可 QA 写） | 需求/接口/验收 |

## 2. 共享模型（`LightCollage.Common`，Lead 已实现于 `src/Common/Models.cs`）

```csharp
public enum CollageOrientation { Horizontal = 0, Vertical = 1 }
public enum OutputFormat { Png = 0, Jpeg = 1 }

public sealed class ImageSize
{
    public ImageSize();
    public ImageSize(int width, int height);
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class CollageSettings
{
    public CollageOrientation Orientation { get; set; } // 默认 Horizontal
    public int Gap { get; set; }                        // 0..200，默认 12
    public int Margin { get; set; }                     // 0..200，默认 16
    public int BackgroundArgb { get; set; }             // ARGB int，默认 0（全透明）
    public bool AutoBaseSize { get; set; }              // 默认 true
    public int BaseSize { get; set; }                   // 自动时为 0；自定义为像素
    public OutputFormat Format { get; set; }            // 默认 Png
    public int JpegQuality { get; set; }                // 1..100，默认 92
    public OutputSizeMode OutputSizeMode { get; set; }  // 默认 Auto；Width/Height 指定最终输出尺寸
    public int OutputTarget { get; set; }               // 默认 2000；指定宽度/高度时的像素值
    public System.Drawing.Color BackgroundColor { get; set; } // 对 BackgroundArgb 的封装
    public CollageSettings Clone();
}

public sealed class LayoutItem
{
    public int Index;
    public int X, Y, Width, Height;
}

public sealed class CollageLayout
{
    public int CanvasWidth;
    public int CanvasHeight;
    public int BaseSize;         // 实际使用的统一高度/宽度
    public LayoutItem[] Items;   // 顺序与输入一致
    public bool WasClamped;      // 是否因尺寸上限被自动缩小
    public int Count { get; }    // Items.Length
}

public sealed class ImageLoadResult : IDisposable
{
    public string FilePath;
    public string FileName;
    public int PixelWidth;       // EXIF 转正后的尺寸
    public int PixelHeight;
    public Bitmap Preview;       // 长边约 previewMaxSide；失败时 null
    public Bitmap Thumbnail;     // 长边约 thumbMaxSide；失败时 null
    public string ErrorMessage;  // 失败时中文提示；成功为 null
    public bool Success { get; }
    public void Dispose();       // 释放 Preview/Thumbnail，可重复调用
}

public sealed class PreviewComposition : IDisposable
{
    public Bitmap Bitmap;        // 预览图（可能带透明，UI 负责棋盘格垫底）
    public CollageLayout Layout; // 输出布局（最终像素坐标）
    public float Scale;          // 预览尺寸 / 输出尺寸
    public void Dispose();
}

public sealed class AppSettings
{
    public CollageSettings Collage { get; set; } // 默认 new CollageSettings()
    public int WindowWidth { get; set; }         // 默认 1180
    public int WindowHeight { get; set; }        // 默认 780
    public int WindowX { get; set; }             // 默认 int.MinValue（居中）
    public int WindowY { get; set; }
    public bool WindowMaximized { get; set; }
    public bool AutoOpenAfterSave { get; set; }  // 默认 false
}

public static class AppInfo
{
    public const string ProductName = "轻拼";
    public const string ProductNameEn = "LightCollage";
    public const string Version = "1.0.0";
    public const string PipeBaseName = "LightCollage";
    public const string RegistryVerb = "LightCollage";
    public const string MenuText = "拼图";
}
```

## 3. Core API（`LightCollage.Core`）

### 3.1 `public static class LayoutEngine`

```csharp
public const int DefaultMaxPixels = 80000000;
public const int DefaultMaxSide = 32000;

// 自动基准：横向取所有图高度中位数，纵向取宽度中位数；四舍五入，至少 1。
public static int GetAutoBaseSize(IList<ImageSize> sizes, CollageOrientation orientation);

// 用默认上限计算布局。空列表返回 0x0 布局。
public static CollageLayout Compute(IList<ImageSize> sizes, CollageSettings settings);
public static CollageLayout Compute(IList<ImageSize> sizes, CollageSettings settings, int maxPixels, int maxSide);
```

规则（必须一致）：
- 横向：统一高度 `H`；`W_i = max(1, round(w_i * H / h_i))`；画布 `W = 2*margin + ΣW_i + gap*(n-1)`，高 `2*margin + H`。
- 纵向：统一宽度 `W`；`H_i = max(1, round(h_i * W / w_i))`；画布 `W_canvas = 2*margin + W`，高 `2*margin + ΣH_i + gap*(n-1)`。
- 图片依次排列，从 `margin` 开始；纵向/横向的交叉轴全部为统一尺寸，无居中偏差。
- 自动基准为 0 或未开启自动时：使用 `settings.BaseSize`（若 <1 则退回自动值）。
- 尺寸保护：若 `CanvasWidth*CanvasHeight > maxPixels` 或任一边 > `maxSide`，等比缩小基准 `H/W` 后重算，`WasClamped=true`。基准最小 1；若仍超限则接受最大可能值（不抛异常）。
- 使用 `long` 防溢出。

### 3.2 `public static class ImageLoader`

```csharp
public static string[] SupportedExtensions { get; } // 小写带点：.jpg/.jpeg/.jpe/.png/.bmp/.gif/.tif/.tiff/.webp/.heic/.heif/.avif/.jfif
public static string FileDialogFilter { get; }      // "图片文件|*.jpg;..." 供 OpenFileDialog
public static bool IsSupportedExtension(string path);
public static bool IsSupportedFile(string path);    // 扩展名支持且 File.Exists

// 永不抛异常。用于编辑期：解码 + EXIF 转正 -> 生成预览/缩略图。失败返回带 ErrorMessage 的结果，位图为 null。
public static ImageLoadResult LoadForEditing(string path, int previewMaxSide, int thumbMaxSide);

// 保存时按原图分辨率加载并应用 EXIF 转正。失败抛 ImageLoadException（中文 Message）。
public static Bitmap LoadFull(string path);
```

实现要求：
- 常见格式优先 GDI+ 解码；`.webp/.heic/.heif/.avif` 用 WIC（`System.Windows.Media.Imaging`，`BitmapDecoder`/`BitmapFrame`），再转成 `System.Drawing.Bitmap`。
- GDI+ 解码遇到不支持的文件时，尝试 WIC 兜底。
- 读取 EXIF Orientation(0x0112)，按标准 2–8 旋转/翻转；`ImageLoadResult.PixelWidth/Height` 与输出必须使用转正后尺寸。
- 不长期锁定源文件：解码完成后释放文件句柄/流。
- 缩放使用高质量双三次插值；保留 Alpha；统一转为 32bppArgb。
- WIC 解码源缺失对应扩展（如未装 HEIF 扩展）时，错误信息明确：`未安装 HEIC/HEIF 解码器，请在 Microsoft Store 安装“HEIF 图像扩展”。`
- 支持动图 GIF 只取首帧。
- 大图保护：解码失败/OOM 返回可读错误，不崩溃。

### 3.3 `public static class Compositor`

```csharp
// 用编辑期预览位图合成预览。maxWidth/maxHeight 是预览画布上限（像素），按比例 fit。
// settings.Format == Jpeg 时按 JPG 实际效果合成：背景/透明先压到白底，输出不透明预览。
// 空列表返回 null。
public static PreviewComposition ComposePreview(IList<ImageLoadResult> items, CollageSettings settings, int maxWidth, int maxHeight);

// 按原始分辨率合成并保存。layout 由调用方用 LayoutEngine.Compute 得到，必须与 sizes 对应。
// Png: 保存 ARGB；Jpeg: 先压到白底再以指定质量保存（无 Alpha）。
// progress 报告 0..100，可为 null。
public static void Save(IList<string> paths, IList<ImageSize> sizes, CollageSettings settings,
                        CollageLayout layout, string outputPath, IProgress<int> progress);

// 供测试/CLI：返回合成后的 Bitmap（PNG=ARGB；JPG=已压白底的不透明 24bpp）。调用方负责 Dispose。
public static Bitmap Compose(IList<string> paths, IList<ImageSize> sizes, CollageSettings settings,
                             CollageLayout layout, IProgress<int> progress);
```

渲染要求：
- 画布 `Format32bppArgb`，先 `Graphics.Clear(background)`（PNG 时保留 Alpha）。
- `CompositingMode.SourceOver`、高质量插值/像素偏移；不对图片做全局透明度。
- 保存时逐张 `LoadFull`，绘制到 layout 指定矩形；用完立即 Dispose。
- JPG 白底合成规则：先把背景色按自身 Alpha 合成到白色，得到不透明底色；再把图片叠加上去。
- 保存目录必须已存在（调用方保证）；编码失败让异常向上抛（UI 捕获提示）。
- 任何 GDI+ 对象用 `using`/try-finally 释放。

### 3.4 `public static class SettingsStore`

```csharp
public static string SettingsDirectory { get; } // %APPDATA%\LightCollage
public static string SettingsPath { get; }      // ...\settings.ini
public static AppSettings Load();               // 缺失/损坏返回默认值，不抛异常
public static void Save(AppSettings settings);  // 自动建目录；写失败不抛（静默/尽量）
```

INI 格式自定，但需兼容缺字段、负值、非法枚举，做 clamp（gap/margin 0..200，quality 1..100，尺寸合理）。

## 4. Shell API（`LightCollage.Shell`）

### 4.1 `public sealed class SingleInstance : IDisposable`

```csharp
public SingleInstance();                 // 尝试成为主实例（命名 Mutex）
public bool IsPrimary { get; }
public event EventHandler<FilesReceivedEventArgs> FilesReceived; // 后台线程触发
public void StartServer();               // 主实例调用：启动命名管道监听
public bool TryForward(IList<string> files, int timeoutMs); // 非主实例调用：发给主实例；成功 true
public void Dispose();                   // 关闭管道、释放 Mutex
```

要求：
- 管道名包含当前用户标识，避免跨用户冲突；`Local\` 前缀。
- 文件列表格式自定，建议 BinaryWriter：int count + 每个 UTF-8 字符串（长度前缀）。单次最多 500 个路径，总长度限制 4 MB。
- 主实例在 UI 创建前就能接收；`FilesReceived` 由后台线程触发，UI 负责 `BeginInvoke`。
- 非主实例连接重试：在 timeoutMs 内循环连接（50–100ms 间隔）；失败返回 false。
- 主实例退出/崩溃后 Mutex 释放，下次启动应能成为主实例（处理 AbandonedMutexException）。
- 注意管道 `WaitForConnection` 的异常恢复；Dispose 后线程退出。

```csharp
public sealed class FilesReceivedEventArgs : EventArgs
{
    public string[] Files { get; }
}
```

### 4.2 `public static class MenuInstaller`

```csharp
public static void Install();    // 写入 HKCU 右键菜单；失败抛异常（中文说明）
public static void Uninstall();  // 删除本工具写入的菜单项；不抛不存在错误
public static bool IsInstalled();// 检查至少一个扩展名的命令指向当前 exe
```

要求：
- 注册路径：`HKCU\Software\Classes\SystemFileAssociations\<扩展名>\shell\LightCollage`，每个支持的扩展名都写。
- 同时写 `HKCU\Software\Classes\SystemFileAssociations\image\shell\LightCollage`（图片感知类型，尽量覆盖混合格式多选）。
- 动词键值：`MUIVerb="拼图"`、`Icon="<exe路径>,0"`、`MultiSelectModel="Player"`；子键 `command` 默认值 `"<exe路径>" "%1"`。
- 使用 `Application.ExecutablePath`（不要 `Assembly.Location`，单文件场景也要可靠）。
- 写完后调用 `SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero)`。
- Uninstall 删除上述扩展名/`image` 下的 `LightCollage` 键以及其 command 子键。
- 只写 HKCU，不写 HKLM，不改文件关联。
- 需要特别验证“混合格式多选”（如 jpg+png+webp）时菜单是否显示；若 `image` 感知类型无法覆盖，可额外加 `HKCU\Software\Classes\*\shell\LightCollage` + `AppliesTo` AQS 过滤图片，但不要在所有文件上无过滤显示。

### 4.3 NativeMethods（Shell 内部）

需要 `SHChangeNotify`；其他 P/Invoke 自行按需。不要引入 COM shell extension。

## 5. 入口与 CLI（Lead 实现，Shell/Core 可参考）

`Program.cs` 支持的参数：
- `--register [--quiet]`：安装右键菜单；quiet 时不弹窗；退出码 0/1。
- `--unregister [--quiet]`：卸载。
- `--compose --layout h|v --gap N --margin N --bg #AARRGGBB --base auto|N --format png|jpg --out FILE image1 [image2 ...]`：无界面合成，用于自动化测试；成功时 stdout 输出 `OK <W>x<H> -> <path>`。
- `--version` / `--help`。
- 其余参数视为图片路径，启动编辑器。
- 命令行解析不要使用第三方库。

主流程（Lead 实现）：
```
new SingleInstance()
  if !IsPrimary:
      if TryForward(files, 2500) -> exit
      else 显示错误提示后 exit
  else:
      StartServer()
      IncomingFiles inbox 订阅 FilesReceived（后台线程 -> 队列）
      运行 MainForm
```

## 6. UI 骨架（Lead 独占）

- `src/App/MainForm.cs`、`PreviewPanel.cs`、`ThumbnailStrip.cs`、`Controls.cs`、`Theme.cs`、`AboutDialog.cs`、`IncomingFiles.cs`。
- 深色主题；右侧设置栏；中间棋盘格预览；底部缩略图；保存/另存为。
- 使用 `LayoutEngine`、`Compositor`、`ImageLoader`、`SettingsStore`；不要绕过 Core 直接写文件格式。
- 所有耗时操作在后台线程；UI 更新 `BeginInvoke`。

## 7. 构建脚本契约（Shell 开发）

- `scripts/build.ps1`：从工程根目录运行，无需参数；用系统 csc 编译 `src/**/*.cs`，`/target:winexe`、`/codepage:65001`、`/platform:anycpu`、`/optimize+`、嵌入 `assets/app.ico` 与 `assets/app.manifest`，输出 `build\LightCollage.exe`；复制到 `dist\轻拼\`；生成/覆盖 `dist\轻拼-右键拼图-v1.0.0.zip`。
- 编译引用见第 0 节；务必加 WPF/System.Xaml 引用。
- 编译失败时输出完整 csc stderr 并退出 1。
- `scripts/install.ps1`：复制 exe 到 `%LOCALAPPDATA%\Programs\LightCollage\LightCollage.exe`，调用 `LightCollage.exe --register --quiet`，打印中文成功信息和 Win11 提示。
- `scripts/uninstall.ps1`：调用已安装 exe `--unregister --quiet`，删除安装目录；保留 `%APPDATA%\LightCollage`（询问可选删除）。
- `安装右键菜单.bat` / `卸载右键菜单.bat`：包装 PowerShell，`-NoProfile -ExecutionPolicy Bypass -File`。
- `assets/app.ico`：多尺寸（16/20/24/32/40/48/64/128/256）含透明；`assets/app.manifest`：DPI Aware + supportedOS；`src/AssemblyInfo.cs`：标题/版本/版权。
- `README.md`：功能、安装/卸载、Win11「显示更多选项」说明、支持格式（WebP/HEIC 依赖）、快捷键、测试/构建、常见问题。中文。

## 8. 测试契约（Core 开发 + QA）

- `tests/core/`：不依赖测试框架的小型 C# 控制台测试，用 csc 编译为 exe；覆盖：
  - 横/纵布局尺寸、gap/margin、自动中位数、自定义基准、单图不缩放、空列表、极端尺寸 clamp。
  - 像素级合成：透明背景 PNG 的 alpha、背景色/半透明、图片自身透明像素、JPG 白底。
  - EXIF 方向（至少 1/3/6/8）。
  - 设置读写/缺省/损坏文件。
- 可用 Python 3 + Pillow 生成测试图片和校验像素（任意 Python 环境，需安装 Pillow）。
- 端到端 CLI 测试通过 `build\LightCollage.exe --compose ...`，由 QA 在集成后执行。

## 9. 协作约定

- 开始前先看 `docs/需求确认-v1.md`、本文件、`src/Common/Models.cs`。
- 只改自己目录；需要改跨目录文件先发消息给 Lead。
- 每完成一个阶段性模块，运行自己的编译检查（可建临时目录放 stub，不要把 stub 提交到 src）。
- 给 Lead 的消息要包含：完成文件、编译命令/结果、测试结果、未解决风险。
- 代码注释用中文，标识符英文；异常信息面向用户用中文。
