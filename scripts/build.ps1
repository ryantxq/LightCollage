#requires -version 3.0
<#
.SYNOPSIS
    轻拼 LightCollage 一键构建脚本（无需 dotnet SDK / Visual Studio / NuGet）。

.DESCRIPTION
    使用 Windows 自带的 .NET Framework 4.x 编译器
    （C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe）把 src\**\*.cs 编译为
    单文件 exe（C# 5 语法、/codepage:65001），并嵌入 assets\app.ico 与 assets\app.manifest。

    产物：
        build\LightCollage.exe
        dist\轻拼\LightCollage.exe + 安装/卸载脚本 + 使用说明.txt
        dist\轻拼-右键拼图-v<版本>.zip

    正常使用无需任何参数，在项目根目录执行：
        powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1

.PARAMETER SourceRoot
    源码目录，默认 <项目根>\src。

.PARAMETER OutputRoot
    exe 输出目录，默认 <项目根>\build。

.PARAMETER DistRoot
    发布目录，默认 <项目根>\dist。

.PARAMETER ExtraSourceDir
    额外源码目录（可递归），用于入口尚未合并时用临时 stub 验证构建脚本；正常构建不要传。

.PARAMETER NoZip
    不生成 ZIP 压缩包。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1
#>
[CmdletBinding()]
param(
    [string]$SourceRoot = '',
    [string]$OutputRoot = '',
    [string]$DistRoot = '',
    [string]$ExtraSourceDir = '',
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Text)
    Write-Host ''
    Write-Host ('==> ' + $Text) -ForegroundColor Cyan
}

function Write-Info {
    param([string]$Text)
    Write-Host ('    ' + $Text)
}

function Stop-Build {
    param([string]$Message)
    Write-Host ''
    Write-Host ('[构建失败] ' + $Message) -ForegroundColor Red
    Write-Host ''
    exit 1
}

function Resolve-FullPath {
    param([string]$PathValue, [string]$BaseDir)
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }
    return [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($BaseDir, $PathValue))
}

# 编译参数尽量用相对项目根的路径（项目路径含中文，避免命令行编码问题）
function Convert-ToCompilePath {
    param([string]$FullName, [string]$Root)
    $prefix = $Root.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if ($FullName.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $FullName.Substring($prefix.Length)
    }
    return $FullName
}

$watch = [System.Diagnostics.Stopwatch]::StartNew()
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$scriptDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptDir)) {
    Stop-Build '无法确定脚本所在目录（请用 -File 方式运行本脚本）。'
}
$projectRoot = Split-Path -Parent $scriptDir
if ([string]::IsNullOrEmpty($projectRoot)) {
    Stop-Build '无法确定项目根目录（scripts 目录的上一级）。'
}

if ([string]::IsNullOrEmpty($SourceRoot)) { $SourceRoot = 'src' }
if ([string]::IsNullOrEmpty($OutputRoot)) { $OutputRoot = 'build' }
if ([string]::IsNullOrEmpty($DistRoot)) { $DistRoot = 'dist' }

$sourceDir = Resolve-FullPath $SourceRoot $projectRoot
$outDir = Resolve-FullPath $OutputRoot $projectRoot
$distBase = Resolve-FullPath $DistRoot $projectRoot
$iconPath = Join-Path $projectRoot 'assets\app.ico'
$manifestPath = Join-Path $projectRoot 'assets\app.manifest'

Write-Host '轻拼 LightCollage 构建脚本'
Write-Host ('项目根目录：' + $projectRoot)

# ---------------------------------------------------------------------------
# 1. 环境检查
# ---------------------------------------------------------------------------
Write-Step '检查编译环境'

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) {
    $fallback = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
    if (Test-Path -LiteralPath $fallback -PathType Leaf) {
        $csc = $fallback
    }
    else {
        Stop-Build '找不到 .NET Framework 4.x 自带编译器 csc.exe。本机需要已安装 .NET Framework 4.8。'
    }
}
$frameworkDir = Split-Path -Parent $csc
Write-Info ('编译器：' + $csc)

$refNames = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Xaml.dll',
    'WPF\WindowsBase.dll',
    'WPF\PresentationCore.dll'
)
$refs = New-Object System.Collections.Generic.List[string]
foreach ($refName in $refNames) {
    $refPath = Join-Path $frameworkDir $refName
    if (-not (Test-Path -LiteralPath $refPath -PathType Leaf)) {
        Stop-Build ('缺少编译引用：' + $refPath + '（请确认已安装 .NET Framework 4.8）')
    }
    $refs.Add($refPath)
}
Write-Info ('引用程序集：' + $refs.Count + ' 个（含 WPF / System.Xaml）')

foreach ($asset in @($iconPath, $manifestPath)) {
    if (-not (Test-Path -LiteralPath $asset -PathType Leaf)) {
        Stop-Build ('缺少资源文件：' + $asset)
    }
}
Write-Info '图标与清单：assets\app.ico、assets\app.manifest'

# ---------------------------------------------------------------------------
# 2. 收集源码
# ---------------------------------------------------------------------------
Write-Step '收集源码'

if (-not (Test-Path -LiteralPath $sourceDir -PathType Container)) {
    Stop-Build ('源码目录不存在：' + $sourceDir)
}
$sourceFiles = @(Get-ChildItem -LiteralPath $sourceDir -Recurse -File -Filter '*.cs' | Sort-Object FullName)
if ($sourceFiles.Count -eq 0) {
    Stop-Build ('源码目录里没有 .cs 文件：' + $sourceDir)
}
if (-not [string]::IsNullOrEmpty($ExtraSourceDir)) {
    $extraDir = Resolve-FullPath $ExtraSourceDir $projectRoot
    if (-not (Test-Path -LiteralPath $extraDir -PathType Container)) {
        Stop-Build ('额外源码目录不存在：' + $extraDir)
    }
    $extraFiles = @(Get-ChildItem -LiteralPath $extraDir -Recurse -File -Filter '*.cs' | Sort-Object FullName)
    $sourceFiles = $sourceFiles + $extraFiles
    Write-Info ('额外入口源码：' + $extraFiles.Count + ' 个（' + $extraDir + '）')
}

$hasEntryPoint = $false
foreach ($sourceFile in $sourceFiles) {
    if (Select-String -LiteralPath $sourceFile.FullName -Pattern 'static\s+(void|int)\s+Main\s*\(' -Quiet) {
        $hasEntryPoint = $true
        break
    }
}
if (-not $hasEntryPoint) {
    Stop-Build ('没有找到程序入口 static Main(...)。请确认 src\Program.cs 已就绪；若只是调试构建脚本，可用 -ExtraSourceDir 指定临时入口。')
}

$compileSources = New-Object System.Collections.Generic.List[string]
foreach ($sourceFile in $sourceFiles) {
    $compileSources.Add((Convert-ToCompilePath $sourceFile.FullName $projectRoot))
}
Write-Info ('源文件：' + $compileSources.Count + ' 个')

# ---------------------------------------------------------------------------
# 3. 编译
# ---------------------------------------------------------------------------
Write-Step '编译（csc /target:winexe）'

if (-not (Test-Path -LiteralPath $outDir -PathType Container)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}
$outExe = Join-Path $outDir 'LightCollage.exe'
if (Test-Path -LiteralPath $outExe -PathType Leaf) {
    Remove-Item -LiteralPath $outExe -Force
}

$outExeArg = Convert-ToCompilePath $outExe $projectRoot
$iconArg = Convert-ToCompilePath $iconPath $projectRoot
$manifestArg = Convert-ToCompilePath $manifestPath $projectRoot

$cscArgs = New-Object System.Collections.Generic.List[string]
$cscArgs.Add('/nologo')
$cscArgs.Add('/target:winexe')
$cscArgs.Add('/platform:anycpu')
$cscArgs.Add('/optimize+')
$cscArgs.Add('/warn:4')
$cscArgs.Add('/codepage:65001')
$cscArgs.Add('/utf8output')
$cscArgs.Add('/out:' + $outExeArg)
$cscArgs.Add('/win32icon:' + $iconArg)
$cscArgs.Add('/win32manifest:' + $manifestArg)
foreach ($refPath in $refs) {
    $cscArgs.Add('/r:' + $refPath)
}
foreach ($compileSource in $compileSources) {
    $cscArgs.Add($compileSource)
}

Push-Location -LiteralPath $projectRoot
try {
    $compileOutput = & $csc @($cscArgs.ToArray()) 2>&1
    $compileExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($null -ne $compileOutput) {
    foreach ($line in $compileOutput) {
        $text = [string]$line
        if ([string]::IsNullOrEmpty($text.Trim())) { continue }
        Write-Host ('    ' + $text)
    }
}

if ($compileExitCode -ne 0) {
    Stop-Build ('csc 编译失败（退出码 ' + $compileExitCode + '），完整错误信息见上方。')
}
if (-not (Test-Path -LiteralPath $outExe -PathType Leaf)) {
    Stop-Build ('编译命令成功但未生成 exe：' + $outExe)
}

$exeItem = Get-Item -LiteralPath $outExe
Write-Info ('输出：' + $outExe)
Write-Info ('大小：' + [math]::Round($exeItem.Length / 1KB, 1) + ' KB')
if ($exeItem.Length -gt 2MB) {
    Write-Host '    警告：exe 体积超过 2 MB，请检查是否误引入了外部资源。' -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# 4. 发布目录
# ---------------------------------------------------------------------------
Write-Step '生成发布目录 dist\轻拼'

$version = '1.0.0'
$modelsFile = Join-Path $projectRoot 'src\Common\Models.cs'
if (Test-Path -LiteralPath $modelsFile -PathType Leaf) {
    $versionMatch = Select-String -LiteralPath $modelsFile -Pattern 'public const string Version = "([0-9]+\.[0-9]+\.[0-9]+)"' | Select-Object -First 1
    if ($null -ne $versionMatch -and $versionMatch.Matches.Count -gt 0) {
        $version = $versionMatch.Matches[0].Groups[1].Value
    }
}
Write-Info ('版本号：v' + $version)

$distDir = Join-Path $distBase '轻拼'
if (Test-Path -LiteralPath $distDir) {
    Remove-Item -LiteralPath $distDir -Recurse -Force
}
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

Copy-Item -LiteralPath $outExe -Destination (Join-Path $distDir 'LightCollage.exe') -Force

$publishFiles = @(
    '使用说明.txt',
    '安装右键菜单.bat',
    '卸载右键菜单.bat',
    'install.ps1',
    'uninstall.ps1'
)
foreach ($publishFile in $publishFiles) {
    $from = Join-Path $scriptDir $publishFile
    if (-not (Test-Path -LiteralPath $from -PathType Leaf)) {
        Stop-Build ('缺少发布文件：' + $from)
    }
    Copy-Item -LiteralPath $from -Destination (Join-Path $distDir $publishFile) -Force
}
Write-Info ('发布文件：' + ($publishFiles.Count + 1) + ' 个')

# ---------------------------------------------------------------------------
# 5. ZIP
# ---------------------------------------------------------------------------
if (-not $NoZip) {
    Write-Step '生成 ZIP 压缩包'
    $zipPath = Join-Path $distBase ('轻拼-右键拼图-v' + $version + '.zip')
    if (Test-Path -LiteralPath $zipPath -PathType Leaf) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    try {
        Compress-Archive -Path $distDir -DestinationPath $zipPath -CompressionLevel Optimal -Force
    }
    catch {
        # 回退到 .NET 原生 ZipFile（个别 PowerShell 版本的 Compress-Archive 对中文路径有兼容问题）
        Write-Info ('Compress-Archive 失败，改用 .NET ZipFile：' + $_.Exception.Message)
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $distDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $true)
    }
    if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
        Stop-Build ('未生成 ZIP：' + $zipPath)
    }
    $zipItem = Get-Item -LiteralPath $zipPath
    Write-Info ('输出：' + $zipPath)
    Write-Info ('大小：' + [math]::Round($zipItem.Length / 1KB, 1) + ' KB')
}

# ---------------------------------------------------------------------------
# 6. 汇总
# ---------------------------------------------------------------------------
$watch.Stop()
Write-Host ''
Write-Host '[构建完成]' -ForegroundColor Green
Write-Host ('    exe   ：' + $outExe)
Write-Host ('    发布   ：' + $distDir)
if (-not $NoZip) {
    Write-Host ('    ZIP   ：' + (Join-Path $distBase ('轻拼-右键拼图-v' + $version + '.zip')))
}
Write-Host ('    用时   ：' + [math]::Round($watch.Elapsed.TotalSeconds, 1) + ' 秒')
Write-Host ''
Write-Host '安装/卸载请运行 dist\轻拼\ 下的「安装右键菜单.bat / 卸载右键菜单.bat」（仅写 HKCU，无需管理员）。'
exit 0
