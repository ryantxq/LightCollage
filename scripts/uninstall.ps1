#requires -version 3.0
<#
.SYNOPSIS
    轻拼 LightCollage 右键菜单卸载脚本（只清理当前用户 HKCU 下本工具写入的项）。

.DESCRIPTION
    1. 优先调用已安装 exe：LightCollage.exe --unregister --quiet；
    2. 无论 exe 是否存在，都兜底清理 HKCU\Software\Classes\SystemFileAssociations
       下本工具写入的 LightCollage 菜单项，保证卸载干净；
    3. 删除安装目录 %LOCALAPPDATA%\Programs\LightCollage；
    4. 询问是否删除设置 %APPDATA%\LightCollage（默认保留）。

    一般由「卸载右键菜单.bat」调用；也可直接运行：
        powershell -NoProfile -ExecutionPolicy Bypass -File uninstall.ps1

.PARAMETER Quiet
    安静模式：不询问是否删除设置（默认保留设置）。

.PARAMETER RemoveSettings
    同时删除设置目录 %APPDATA%\LightCollage（不询问）。

.PARAMETER DryRun
    调试用：只打印将要执行的步骤，不删除文件、不写注册表（开发期安全测试用）。
#>
[CmdletBinding()]
param(
    [switch]$Quiet,
    [switch]$RemoveSettings,
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

$scriptDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptDir)) {
    Write-Host '[卸载失败] 无法确定脚本所在目录（请用 -File 方式运行本脚本）。' -ForegroundColor Red
    exit 1
}

# 与 src\Shell\MenuInstaller.cs 的扩展名列表保持一致
$menuExtensions = @(
    '.jpg', '.jpeg', '.jpe', '.png', '.bmp', '.gif', '.tif', '.tiff',
    '.webp', '.heic', '.heif', '.avif', '.jfif'
)

$installDir = ''
if (-not [string]::IsNullOrEmpty($env:LOCALAPPDATA)) {
    $installDir = Join-Path $env:LOCALAPPDATA 'Programs\LightCollage'
}
$installedExe = ''
if (-not [string]::IsNullOrEmpty($installDir)) {
    $installedExe = Join-Path $installDir 'LightCollage.exe'
}

Write-Host '轻拼 LightCollage · 右键菜单卸载程序' -ForegroundColor White
Write-Host ''

if ($DryRun) {
    Write-Warn '（DryRun 调试模式：只显示步骤，不会删除文件或注册表项）'
    Write-Host '  1) 调用 --unregister 卸载右键菜单（若已安装）'
    Write-Host '  2) 兜底清理 HKCU\Software\Classes\SystemFileAssociations 下 LightCollage 菜单项'
    Write-Host '  3) 刷新资源管理器菜单缓存（SHChangeNotify）'
    Write-Host ('  4) 删除安装目录：' + $installDir)
    Write-Host '  5) 按参数/交互决定是否删除设置目录：%APPDATA%\LightCollage'
    Write-Ok '[DryRun 完成] 未对系统做任何修改。'
    exit 0
}

$hadProblem = $false

# ---------------------------------------------------------------------------
# 1. 优先调用 exe 自带的 --unregister
# ---------------------------------------------------------------------------
$unregisterExe = ''
$exeCandidates = @()
if (-not [string]::IsNullOrEmpty($installedExe)) { $exeCandidates += $installedExe }
$exeCandidates += @(
    (Join-Path $scriptDir 'LightCollage.exe'),
    (Join-Path $scriptDir '..\build\LightCollage.exe'),
    (Join-Path $scriptDir '..\dist\轻拼\LightCollage.exe')
)
foreach ($candidate in $exeCandidates) {
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $unregisterExe = (Resolve-Path -LiteralPath $candidate).Path
        break
    }
}

if (-not [string]::IsNullOrEmpty($unregisterExe)) {
    Write-Step ('调用卸载命令：' + $unregisterExe + ' --unregister --quiet')
    try {
        $process = Start-Process -FilePath $unregisterExe -ArgumentList '--unregister', '--quiet' -Wait -PassThru
        if ($null -eq $process -or $process.ExitCode -ne 0) {
            Write-Warn '程序返回非零退出码，继续使用脚本兜底清理注册表。'
        }
    }
    catch {
        Write-Warn ('调用程序卸载失败，继续使用脚本兜底清理注册表：' + $_.Exception.Message)
    }
}
else {
    Write-Warn '没有找到 LightCollage.exe，直接用脚本兜底清理右键菜单注册表项。'
}

# ---------------------------------------------------------------------------
# 2. 兜底清理 HKCU 注册表项（幂等，只删本工具的动词键）
# ---------------------------------------------------------------------------
Write-Step '清理当前用户右键菜单注册表项'

function Invoke-ShellRefresh {
    try {
        if ($null -eq ('LightCollagePowerShell.ShellRefresh' -as [type])) {
            Add-Type -Namespace LightCollagePowerShell -Name ShellRefresh -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = false)]
public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
public static void Notify() { SHChangeNotify(0x08000000, 0x1000, System.IntPtr.Zero, System.IntPtr.Zero); }
'@
        }
        [LightCollagePowerShell.ShellRefresh]::Notify()
    }
    catch {
        # 刷新失败不影响卸载结果
    }
}

$verbTargets = New-Object System.Collections.Generic.List[string]
$assocRoot = 'HKCU:\Software\Classes\SystemFileAssociations'
foreach ($extension in $menuExtensions) {
    $verbTargets.Add($assocRoot + '\' + $extension + '\shell\LightCollage')
}
$verbTargets.Add($assocRoot + '\image\shell\LightCollage')

$remaining = 0
foreach ($target in $verbTargets) {
    if (Test-Path -LiteralPath $target) {
        try {
            Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop
        }
        catch {
            Write-Warn ('删除注册表项失败：' + $target + ' (' + $_.Exception.Message + ')')
        }
    }
    if (Test-Path -LiteralPath $target) {
        $remaining++
    }
}

Invoke-ShellRefresh

if ($remaining -eq 0) {
    Write-Host '右键菜单注册表项已清理。'
}
else {
    Write-Warn ('仍有 ' + $remaining + ' 个注册表项未能删除，可手动删除：HKCU\Software\Classes\SystemFileAssociations\<扩展名>\shell\LightCollage')
    $hadProblem = $true
}

# ---------------------------------------------------------------------------
# 3. 删除安装目录
# ---------------------------------------------------------------------------
if (-not [string]::IsNullOrEmpty($installDir)) {
    Write-Step ('删除安装目录：' + $installDir)
    if (Test-Path -LiteralPath $installDir) {
        try {
            Remove-Item -LiteralPath $installDir -Recurse -Force -ErrorAction Stop
            Write-Host '安装目录已删除。'
        }
        catch {
            Write-Warn ('删除安装目录失败：' + $_.Exception.Message)
            Write-Warn '如果程序正在运行，请关闭「轻拼」窗口后重新运行本脚本。'
            $hadProblem = $true
        }
    }
    else {
        Write-Host '安装目录不存在，跳过。'
    }
}

# ---------------------------------------------------------------------------
# 4. 设置目录（默认保留，可选删除）
# ---------------------------------------------------------------------------
$settingsDir = ''
if (-not [string]::IsNullOrEmpty($env:APPDATA)) {
    $settingsDir = Join-Path $env:APPDATA 'LightCollage'
}
if (-not [string]::IsNullOrEmpty($settingsDir) -and (Test-Path -LiteralPath $settingsDir -PathType Container)) {
    $removeSettingsNow = [bool]$RemoveSettings
    if (-not $removeSettingsNow -and -not $Quiet) {
        Write-Host ''
        $answer = ''
        try {
            $answer = Read-Host '是否同时删除设置（间距/边距/背景/窗口位置等）？输入 Y 删除，直接回车保留'
        }
        catch {
            $answer = ''
        }
        if ($null -ne $answer -and $answer.Trim() -match '^(?i)y(es)?$') {
            $removeSettingsNow = $true
        }
    }

    if ($removeSettingsNow) {
        try {
            Remove-Item -LiteralPath $settingsDir -Recurse -Force -ErrorAction Stop
            Write-Host '设置目录已删除。'
        }
        catch {
            Write-Warn ('删除设置目录失败：' + $_.Exception.Message)
        }
    }
    else {
        Write-Host ('已保留设置目录：' + $settingsDir)
    }
}

# ---------------------------------------------------------------------------
# 5. 结果
# ---------------------------------------------------------------------------
Write-Host ''
if ($hadProblem) {
    Write-Warn '[完成（有警告）] 右键菜单已卸载，但部分文件/注册表项未能删除，请查看上方提示。'
    exit 1
}
Write-Ok '[完成] 轻拼右键菜单已卸载。'
Write-Host '提示：资源管理器菜单可能需要几秒钟刷新；已打开的「轻拼」窗口不受影响。'
exit 0
