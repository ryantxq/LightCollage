#requires -version 3.0
<#
.SYNOPSIS
    轻拼 LightCollage 并行开发用「编译检查」脚本。
    与 scripts\build.ps1 使用相同的 csc 参数 / 引用 / 源码集合，但输出到 build\_check_<OutName>.exe，
    不覆盖 build\LightCollage.exe、不生成 dist、不打包 ZIP，供多成员并行编译检查使用。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\check-compile.ps1 -OutName ui
#>
[CmdletBinding()]
param(
    [string]$OutName = 'check',
    [string]$SourceRoot = 'src'
)

$ErrorActionPreference = 'Stop'

$scriptDir = $PSScriptRoot
$projectRoot = Split-Path -Parent $scriptDir
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) {
    $fallback = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
    if (Test-Path -LiteralPath $fallback -PathType Leaf) { $csc = $fallback }
    else { Write-Host '[编译检查失败] 找不到 csc.exe'; exit 1 }
}
$frameworkDir = Split-Path -Parent $csc
$refNames = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
    'System.Xaml.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll')
$refs = New-Object System.Collections.Generic.List[string]
foreach ($refName in $refNames) {
    $refPath = Join-Path $frameworkDir $refName
    if (-not (Test-Path -LiteralPath $refPath -PathType Leaf)) {
        Write-Host ('[编译检查失败] 缺少引用：' + $refPath); exit 1
    }
    $refs.Add($refPath)
}

$sourceDir = Join-Path $projectRoot $SourceRoot
if (-not (Test-Path -LiteralPath $sourceDir -PathType Container)) {
    Write-Host ('[编译检查失败] 源码目录不存在：' + $sourceDir); exit 1
}
$sourceFiles = @(Get-ChildItem -LiteralPath $sourceDir -Recurse -File -Filter '*.cs' | Sort-Object FullName)
if ($sourceFiles.Count -eq 0) { Write-Host '[编译检查失败] 没有 .cs 文件'; exit 1 }

$outDir = Join-Path $projectRoot 'build'
if (-not (Test-Path -LiteralPath $outDir -PathType Container)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}
$outExeName = '_check_' + $OutName + '.exe'
$outExe = Join-Path $outDir $outExeName
if (Test-Path -LiteralPath $outExe -PathType Leaf) { Remove-Item -LiteralPath $outExe -Force }

$cscArgs = New-Object System.Collections.Generic.List[string]
$cscArgs.Add('/nologo')
$cscArgs.Add('/target:winexe')
$cscArgs.Add('/platform:anycpu')
$cscArgs.Add('/optimize+')
$cscArgs.Add('/warn:4')
$cscArgs.Add('/codepage:65001')
$cscArgs.Add('/utf8output')
$cscArgs.Add('/out:build\' + $outExeName)
$cscArgs.Add('/win32icon:assets\app.ico')
$cscArgs.Add('/win32manifest:assets\app.manifest')
foreach ($refPath in $refs) { $cscArgs.Add('/r:' + $refPath) }
foreach ($sourceFile in $sourceFiles) {
    $cscArgs.Add($sourceFile.FullName.Substring($projectRoot.Length + 1))
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
        if (-not [string]::IsNullOrWhiteSpace($text)) { Write-Host $text }
    }
}
if ($compileExitCode -ne 0) {
    Write-Host ('[编译检查失败] csc 退出码 ' + $compileExitCode + '（输出：build\' + $outExeName + '）')
    exit 1
}
Write-Host ('[编译检查通过] ' + $sourceFiles.Count + ' 个源文件 -> build\' + $outExeName)
exit 0
