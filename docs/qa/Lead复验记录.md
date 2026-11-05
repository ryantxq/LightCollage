# 轻拼 LightCollage v1.0.0 · Lead 重建后复验记录

- 日期：2026-11-04
- 性质：QA 验收报告（`docs/qa/验收报告.md`）之后，Lead 修复缺陷并重建 dist 的补充复验；与 QA 报告合并阅读。
- 最终产物：`dist\轻拼\LightCollage.exe`、`dist\轻拼-右键拼图-v1.0.0.zip`

## 1. 最终构建信息

| 项目 | 值 |
|---|---|
| exe 大小 | 146,432 字节（143 KB） |
| exe SHA256 | `E5D6F35E96D9631FE2FECD8B2E1C59EE12BBCAA5753A7CFE804138CC19951F9D` |
| ZIP SHA256 | `2976C31CAC1D350CC2BA7D5A1D16AA2A1ED1799851649ACABFF2AD84C0D56346` |
| ZIP 大小 | 95,352 字节 |
| 版本 | 轻拼 1.0.0（`--version` 退出码 0） |
| 安装副本 SHA256 | `E5D6F35E96D9631FE2FECD8B2E1C59EE12BBCAA5753A7CFE804138CC19951F9D`（与 dist 一致） |

## 2. QA 缺陷修复与复验

### BUG-01（中）：损坏图片占位卡住预览/保存 → 已修复

- 修复：`src/App/MainForm.cs` 的 `OnItemLoaded`；解码失败（损坏/不支持/解码器缺失）的图片不再保留为占位项，而是从列表自动移除，收集错误；正常图片继续生成预览并可保存。
- 复验：启动
  `dist\轻拼\LightCollage.exe corrupted.png good1.png good2.png`
  - 弹出可读警告：`无法打开「corrupted.png」…`
  - 关闭警告后：**共 2 张图片**、缩略图只剩 2 张正常图、预览正常生成（124×52）、保存按钮可用；状态栏保留“有 1 张图片无法载入”。
  - 点击保存 → 生成 `拼图_yyyyMMdd_HHmmss.png`（124×52 RGBA，边距/间隙 alpha=0），保存成功。
- 证据：新增截图 `docs/qa/screenshots/20-BUG01修复后-正常图可预览.png`。

### PKG-01（P3）：dist 使用说明为旧版 → 已修复

- 重建后哈希一致：
  - `scripts\使用说明.txt` SHA256 `770377AE09255A9A80396EEA51B442348EF559F8EABD99B6E906E0BD77B62098`
  - `dist\轻拼\使用说明.txt` SHA256 同上
- ZIP 已由同一次最终构建生成，条目包含新版 `使用说明.txt`、`LightCollage.exe`、安装/卸载脚本。

### OBS-02 / OBS-04 / OBS-05 / OBS-06 细节优化

- OBS-02：UI“白色”预设改为纯白 `#FFFFFFFF`，与 JPG 透明压白一致。
- OBS-04：关于窗口状态文案改为“当前程序副本的右键菜单：已安装/未安装”，减少“已安装却显示未安装”的困惑。
- OBS-05：Stepper 的 +/− 热区与符号绘制改为随系统 DPI 缩放；200% DPI 下实测点击左侧减号有效（Gap 12 → 11，settings.ini 写入 `Gap=11`）。
- OBS-06：HEIC/HEIF/WEBP/AVIF 解码失败提示改为“系统可能未安装扩展，或文件已损坏”，不再一律断言缺解码器。
- 注：OBS-03（自定义颜色保留当前透明度）为设计行为，未改；自定义对话框打开时透明度沿用滑块值。

## 3. 真实 HEIC / AVIF 解码补验（QA N/V 项）

QA 因缺少样本未验证 HEIC/AVIF；Lead 从公开测试站点取得真实样本并补验：

| 格式 | 样本 | 命令/结果 |
|---|---|---|
| HEIC | `tests\qa\fixtures\sample640x480.heic`（5,066 B） | `--compose` → `OK 640x480`，输出 RGBA 640×480，中心像素 `(170,113,168,255)`，exit 0 |
| AVIF | `tests\qa\fixtures\sample640x480.avif`（1,284 B） | `--compose` → `OK 640x480`，输出 RGBA 640×480，中心像素 `(128,128,128,255)`，exit 0 |

结论：本机（已装 HEIF 1.2.62 / HEVC 2.5.48 / AV1）真实 HEIC、AVIF 均可通过 WIC 解码并参与拼接；缺少解码器的提示分支仍以 QA 的伪造文件测试为准。

## 4. 最终安装验证

已用最终 dist 运行 `install.ps1`，安装到：

```
%LOCALAPPDATA%\Programs\LightCollage\LightCollage.exe
```

验证结果：

- 安装 exit 0；安装副本与 dist exe SHA256 一致。
- HKCU 菜单键：13 个图片扩展名 + `image` 感知类型，共 **14/14** 指向安装路径；
  `MUIVerb=拼图`、`MultiSelectModel=Player`，元数据无异常。
- HKLM 无 LightCollage 菜单键。
- 通过 ShellExecuteEx 调用 `LightCollage` 动词：实际从安装路径启动窗口（pid 路径已核对）。
- QA 已实测：混合 jpg+png+webp 多选 →「显示更多选项」→「拼图」→ 载入 3 张；单文件、非图片场景也已验证。
- 安装后为交付默认状态：已删除 `%APPDATA%\LightCollage` 设置目录，首次启动为横向/透明/间距12/边距16/PNG；无遗留 LightCollage 进程。

## 5. 仍如实记录的能力边界

- **Win11 一级右键菜单**：仍采用经典菜单（「显示更多选项」内）；这是免管理员/轻量方案的取舍，README 与使用说明已写明。
- **另存为自定义路径**：自动化在原生 SaveFileDialog 内输入路径受限；默认文件名点击“保存”落盘已由 QA 实测通过；自定义路径建议用户手动使用（实现为标准 SaveFileDialog，无特殊逻辑）。
- **性能**：冷启动中位 322 ms、保存 3 图 201–306 ms、60 图 264 ms 已实测；3 图/10 张大图预览与参数刷新受 UIA 桥接开销影响，未取得可信毫秒值，建议日常人工感受。
- **环境差异**：以上验证在 200% DPI 的 Win11 风格 shell 上完成；100% DPI/其它输入法环境建议用户实际使用确认。
