#requires -version 3.0
<#
.SYNOPSIS
    轻拼 LightCollage 右键菜单安装脚本（仅写当前用户 HKCU，无需管理员权限）。

.DESCRIPTION
    1. 把 LightCollage.exe 复制到 %LOCALAPPDATA%\Programs\LightCollage\；
    2. 调用 LightCollage.exe --register --quiet 写入当前用户右键菜单；
    3. 打印中文安装结果与 Windows 11「显示更多选项」提示。

    一般由「安装右键菜单.bat」调用；也可直接运行：
        powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1

.PARAMETER Quiet
    安静模式：不输出额外说明（保留关键结果）。

.PARAMETER DryRun
    调试用：只打印将要执行的步骤，不复制文件、不写注册表（开发期安全测试用）。
#>
[CmdletBinding()]
param(
    [switch]$Quiet,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

function Write-Step {
    param([string]$Text)
    Write-Host ('==> ' + $Text) -ForegroundColor Cyan
}

function Write-Ok {
    param([string]$Text)
    Write-Host $Text -ForegroundColor Green
}

function Write-Warn {
    param([string]$Text)
    Write-Host $Text -ForegroundColor Yellow
}

function Stop-Install {
    param([string]$Message)
    Write-Host ''
    Write-Host ('[安装失败] ' + $Message) -ForegroundColor Red
    exit 1
}

$scriptDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptDir)) {
    Stop-Install '无法确定脚本所在目录（请用 -File 方式运行本脚本）。'
}
if ([string]::IsNullOrEmpty($env:LOCALAPPDATA)) {
    Stop-Install '无法读取环境变量 LOCALAPPDATA。'
}

Write-Host '轻拼 LightCollage · 右键菜单安装程序' -ForegroundColor White
Write-Host ''

# 1. 找 exe：dist 目录 / 项目 build 目录 / 脚本同目录
$sourceExe = ''
$candidates = @(
    (Join-Path $scriptDir 'LightCollage.exe'),
    (Join-Path $scriptDir '..\build\LightCollage.exe'),
    (Join-Path $scriptDir '..\dist\轻拼\LightCollage.exe')
)
foreach ($candidate in $candidates) {
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $sourceExe = (Resolve-Path -LiteralPath $candidate).Path
        break
    }
}
if ([string]::IsNullOrEmpty($sourceExe)) {
    Stop-Install '未找到 LightCollage.exe。请把本脚本与 exe 放在同一目录，或先运行 scripts\build.ps1 构建。'
}

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\LightCollage'
$targetExe = Join-Path $installDir 'LightCollage.exe'

Write-Host ('程序来源：' + $sourceExe)
Write-Host ('安装位置：' + $targetExe)
Write-Host ''

if ($DryRun) {
    Write-Warn '（DryRun 调试模式：只显示步骤，不会复制文件、不会写入注册表）'
    Write-Host ('  1) 创建目录：' + $installDir)
    Write-Host ('  2) 复制 exe：' + $sourceExe + '  ->  ' + $targetExe)
    Write-Host ('  3) 调用：' + $targetExe + ' --register --quiet')
    Write-Host '  4) 打印使用方法与 Windows 11「显示更多选项」提示'
    Write-Ok '[DryRun 完成] 未对系统做任何修改。'
    exit 0
}

# 2. 复制到安装目录（复制失败通常是旧版本仍在运行）
try {
    if (-not (Test-Path -LiteralPath $installDir -PathType Container)) {
        New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    }
    $sameFile = $false
    if (Test-Path -LiteralPath $targetExe -PathType Leaf) {
        $sourceItem = Get-Item -LiteralPath $sourceExe
        $targetItem = Get-Item -LiteralPath $targetExe
        $sameFile = [string]::Equals($sourceItem.FullName, $targetItem.FullName, [System.StringComparison]::OrdinalIgnoreCase)
    }
    if (-not $sameFile) {
        Copy-Item -LiteralPath $sourceExe -Destination $targetExe -Force
    }
}
catch {
    Stop-Install ('复制程序失败。如果「轻拼」窗口正在运行，请先关闭后重试。' + [Environment]::NewLine + '详细信息：' + $_.Exception.Message)
}
Write-Host '已复制程序文件。'

# 3. 调用 exe 注册右键菜单（--register 只写 HKCU）
try {
    $process = Start-Process -FilePath $targetExe -ArgumentList '--register', '--quiet' -Wait -PassThru
}
catch {
    Stop-Install ('启动 LightCollage.exe --register 失败：' + $_.Exception.Message)
}
if ($null -eq $process -or $process.ExitCode -ne 0) {
    $codeText = '未知'
    if ($null -ne $process) { $codeText = [string]$process.ExitCode }
    Stop-Install ('注册右键菜单失败（退出码 ' + $codeText + '）。可尝试重新运行本脚本，或手动执行：' + [Environment]::NewLine + '  "' + $targetExe + '" --register')
}
Write-Host '已写入当前用户右键菜单注册表（HKCU，无需管理员）。'
Write-Host ''

Write-Ok '[完成] 「拼图」右键菜单安装成功！'
Write-Host ''
Write-Host '使用方法：'
Write-Host '  1. 在文件资源管理器中选中一张或多张图片（可跨文件夹、可混合格式）'
Write-Host '  2. 右键 →「拼图」'
Write-Host '  3. 在「轻拼」窗口里调整拼接方式、间距、边距、背景与透明度，然后保存'
Write-Host ''
Write-Host 'Windows 11 提示：'
Write-Host '  系统默认右键菜单是精简版，「拼图」位于「显示更多选项」里。'
Write-Host '  这是无需管理员权限、免安装的经典菜单方案，属预期行为。'
Write-Host ''
Write-Host '卸载：运行同一目录下的「卸载右键菜单.bat」。'
Write-Host '设置文件：%APPDATA%\LightCollage\settings.ini（卸载时默认保留）。'
exit 0
