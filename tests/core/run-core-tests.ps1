# LightCollage Core 单元测试运行脚本（无需 dotnet SDK / 测试框架）
# 用法（在任意目录）：
#   powershell -ExecutionPolicy Bypass -File tests\core\run-core-tests.ps1
# 可选参数：
#   -Fixtures <dir>   测试图片目录（默认 tests\core\fixtures）
#   -Python <exe>     指定 Python（默认自动探测：Pillow 用于生成测试图）
#   -SkipFixtures     跳过生成测试图（复用已有 fixtures）
# 退出码：0 全部通过；1 有失败；2 环境/编译问题。

param(
    [string]$Fixtures = '',
    [string]$Python = '',
    [switch]$SkipFixtures
)

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
$projectRoot = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
$frameworkDir = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $frameworkDir 'csc.exe'
if (-not (Test-Path $csc)) {
    Write-Host "ERROR: csc.exe not found at $csc" -ForegroundColor Red
    exit 2
}
if (-not $Fixtures) {
    $Fixtures = Join-Path $scriptDir 'fixtures'
}

# ---- 1) 生成测试图片（Pillow） ----
if (-not $SkipFixtures) {
    # Python 解释器探测：显式 -Python 参数 → 环境变量 QA_PYTHON → PATH 中的 python（需 Python 3 + Pillow）
    $pythonExe = $Python
    if (-not $pythonExe) { $pythonExe = $env:QA_PYTHON }
    if ($pythonExe -and -not (Test-Path -LiteralPath $pythonExe)) {
        Write-Host ('警告：指定的 Python 不存在：' + $pythonExe + '，改为从 PATH 探测 python。') -ForegroundColor Yellow
        $pythonExe = $null
    }
    if (-not $pythonExe) {
        $command = Get-Command python -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command) {
            $pythonExe = $command.Source
        }
    }
    if (-not $pythonExe) {
        Write-Host '错误：未找到 Python 解释器（需 Python 3 + Pillow）。请用 -Python <路径> 指定、设置环境变量 QA_PYTHON，或将 python 加入 PATH；也可加 -SkipFixtures 复用已有测试图。' -ForegroundColor Red
        exit 2
    }
    Write-Host "Generating fixtures with: $pythonExe"
    & $pythonExe (Join-Path $scriptDir 'make_test_images.py') $Fixtures
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: fixture generation failed (exit $LASTEXITCODE)" -ForegroundColor Red
        exit 2
    }
}

if (-not (Test-Path $Fixtures)) {
    Write-Host "ERROR: fixtures directory not found: $Fixtures" -ForegroundColor Red
    exit 2
}

# ---- 2) 编译（src/Common + src/Core + CoreTests.cs -> 单 exe） ----
$binDir = Join-Path $scriptDir 'bin'
if (-not (Test-Path $binDir)) {
    New-Item -ItemType Directory -Path $binDir -Force | Out-Null
}
$exePath = Join-Path $binDir 'CoreTests.exe'

$sources = @()
$sources += Get-ChildItem -Path (Join-Path $projectRoot 'src\Common') -Filter '*.cs' -Recurse -File | ForEach-Object { $_.FullName }
$sources += Get-ChildItem -Path (Join-Path $projectRoot 'src\Core') -Filter '*.cs' -Recurse -File | ForEach-Object { $_.FullName }
$sources += (Join-Path $scriptDir 'CoreTests.cs')

$cscArgs = @(
    '/nologo',
    '/target:exe',
    '/platform:anycpu',
    '/codepage:65001',
    '/utf8output',
    ('/out:' + $exePath),
    ('/r:' + (Join-Path $frameworkDir 'System.dll')),
    ('/r:' + (Join-Path $frameworkDir 'System.Core.dll')),
    ('/r:' + (Join-Path $frameworkDir 'System.Drawing.dll')),
    ('/r:' + (Join-Path $frameworkDir 'System.Windows.Forms.dll')),
    ('/r:' + (Join-Path $frameworkDir 'System.Xaml.dll')),
    ('/r:' + (Join-Path $frameworkDir 'WPF\WindowsBase.dll')),
    ('/r:' + (Join-Path $frameworkDir 'WPF\PresentationCore.dll'))
) + $sources

Write-Host ''
Write-Host "Compiling Core + tests ($($sources.Count) files) ..."
$compileOutput = & $csc @cscArgs 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    Write-Host $compileOutput
    Write-Host "COMPILE FAILED (exit $LASTEXITCODE)" -ForegroundColor Red
    exit 2
}
if ($compileOutput.Trim().Length -gt 0) {
    Write-Host $compileOutput
}

# ---- 3) 运行 ----
Write-Host "Running: $exePath $Fixtures"
Write-Host ''
$output = & $exePath $Fixtures 2>&1 | Out-String
$runExit = $LASTEXITCODE
Write-Host $output

$logPath = Join-Path $scriptDir 'last-run.log'
$logText = "LightCollage Core Tests log`r`nDate: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')`r`nFixtures: $Fixtures`r`n`r`n" + $output
try {
    [System.IO.File]::WriteAllText($logPath, $logText, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Log written: $logPath"
} catch {
    Write-Host "WARN: could not write log: $_" -ForegroundColor Yellow
}

if ($runExit -eq 0) {
    Write-Host ''
    Write-Host 'CORE TESTS PASSED' -ForegroundColor Green
} else {
    Write-Host ''
    Write-Host "CORE TESTS FAILED (exit $runExit)" -ForegroundColor Red
}
exit $runExit
