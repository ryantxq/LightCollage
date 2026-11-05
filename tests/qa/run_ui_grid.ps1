# 轻拼 LightCollage v1.1.0 网格拼接 UI 验收脚本（QA 编写，不修改产品源码）
# 运行：powershell -NoProfile -ExecutionPolicy Bypass -File tests\qa\run_ui_grid.ps1
# 可选：设置环境变量 QA_EXE 指向被测 exe（默认 dist\轻拼\LightCollage.exe）
# 依赖：tests\qa\bin\QaTool.exe（含 v1.1 新增 segwait 段数参数 / matrixdrag / previewdrag）、
#        Python 3 + Pillow（生成纯色测试图与像素校验）。
# 覆盖 A1–A18 中 UI 链路：三段切换、网格设置区可见性、行列/间距、合并弹窗（合并/拆分/清空/取消）、
# 容量不足「未放入」、JPG 白底、预览拖动对调（网格/横向/纵向、无效目标、Esc）、设置记忆重启、
# 网格辅助线仅预览、旧配置/损坏配置、1×1/20×20 边界、单实例追加、最小窗口/200% DPI、性能原始数据。
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$script:root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:qa = 'C:\Temp\qa-lightcollage'
$script:qaTool = Join-Path $PSScriptRoot 'bin\QaTool.exe'
$script:exeDefault = Join-Path $script:root 'dist\轻拼\LightCollage.exe'
$script:exe = $env:QA_EXE
if (-not $script:exe -or -not (Test-Path -LiteralPath $script:exe)) { $script:exe = $script:exeDefault }
# Python 解释器探测：优先 QA_PYTHON，其次 PATH 的 python（需 Pillow）
$script:py = $env:QA_PYTHON
if ($script:py -and -not (Test-Path -LiteralPath $script:py)) { $script:py = $null }
if (-not $script:py) {
    $pythonCmd = Get-Command python -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($pythonCmd) { $script:py = $pythonCmd.Source }
}
if (-not $script:py) {
    Write-Error '未找到 Python 解释器：请设置 QA_PYTHON 或把 python 加入 PATH（需安装 Pillow）。'
    exit 1
}
$script:imgcheck = Join-Path $PSScriptRoot 'imgcheck.py'
$script:title = '轻拼 - 图片拼接'
$script:logs = Join-Path $script:root 'docs\qa\logs'
$script:screens = Join-Path $script:root 'docs\qa\screenshots'
$script:gridDir = Join-Path $script:qa 'ui-grid'
$script:outDir = Join-Path $script:qa 'out'
New-Item -ItemType Directory -Force -Path $script:logs, $script:screens, $script:gridDir, $script:outDir | Out-Null

$script:pass = 0
$script:fail = 0
$script:results = New-Object System.Collections.ArrayList
$script:log = Join-Path $script:logs 'ui-grid.log'
if (Test-Path $script:log) { Remove-Item $script:log -Force }
function Log([string]$line) { Write-Host $line; Add-Content -Path $script:log -Value $line -Encoding UTF8 }
function Check([string]$name, [bool]$cond, $expected, $actual) {
    if ($cond) { $script:pass++ } else { $script:fail++ }
    $tag = 'PASS'; if (-not $cond) { $tag = 'FAIL' }
    Log ("[$tag] $name | expected=$expected | actual=$actual")
    [void]$script:results.Add([pscustomobject]@{ name = $name; pass = $cond; expected = [string]$expected; actual = [string]$actual })
}
function Invoke-Qa([string[]]$a) { return ((& $script:qaTool @a 2>&1 | Out-String).Trim()) }
function App-Kill { Get-Process | Where-Object { $_.ProcessName -like 'LightCollage*' } | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 500 }
function Start-App([string[]]$files, [string]$settingsDir) {
    App-Kill
    if ($settingsDir) {
        $env:LIGHTCOLLAGE_SETTINGS_DIR = $settingsDir
        if (-not (Test-Path -LiteralPath $settingsDir)) { New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null }
    } else {
        Remove-Item Env:\LIGHTCOLLAGE_SETTINGS_DIR -ErrorAction SilentlyContinue
    }
    $fileArgs = @(); foreach ($f in $files) { $fileArgs += $f }
    Start-Process -FilePath $script:exe -ArgumentList $fileArgs | Out-Null
    Invoke-Qa @('waitwin', $script:title, '15000') | Out-Null
    # 等图片载入完成（状态栏「已载入 N 张图片」），避免在加载中读取计数行
    Invoke-Qa @('waittext', $script:title, '已载入', '8000') | Out-Null
    Start-Sleep -Milliseconds 600
}
function Stop-AppGraceful {
    Invoke-Qa @('bring', $script:title) | Out-Null
    Invoke-Qa @('close', $script:title) | Out-Null
    Start-Sleep -Milliseconds 900
    # 有未保存改动时主窗关闭会弹「还有未保存的拼图，确定关闭吗？」→ 点「是」（按钮名可能带 &Y，用 contains）
    $dlg = Invoke-Qa @('waitwin', '轻拼', '1500')
    if ($dlg -match 'FOUND') {
        Invoke-Qa @('clickname', '轻拼', '是', 'contains') | Out-Null
        Start-Sleep -Milliseconds 700
    }
    Invoke-Qa @('waitgone', $script:title, '5000') | Out-Null
    Start-Sleep -Milliseconds 600
    App-Kill
}
function Get-Rect {
    $o = Invoke-Qa @('rect', $script:title)
    $m = [regex]::Match($o, 'window=(-?\d+),(-?\d+),(-?\d+),(-?\d+) \((\d+)x(\d+)\)')
    $c = [regex]::Match($o, 'client=(-?\d+),(-?\d+),(-?\d+),(-?\d+) \((\d+)x(\d+)\)')
    $d = [regex]::Match($o, 'dpi=(\d+)')
    $r = @{ x = 0; y = 0; w = 0; h = 0; cw = 0; ch = 0; dpi = 0; raw = $o }
    if ($m.Success) { $r.x = [int]$m.Groups[1].Value; $r.y = [int]$m.Groups[2].Value; $r.w = [int]$m.Groups[5].Value; $r.h = [int]$m.Groups[6].Value }
    if ($c.Success) { $r.cw = [int]$c.Groups[5].Value; $r.ch = [int]$c.Groups[6].Value }
    if ($d.Success) { $r.dpi = [int]$d.Groups[1].Value }
    return $r
}
function Get-Texts([string]$tag) { return (Get-TextsOf $script:title $tag) }
function Get-TextsOf([string]$winTitle, [string]$tag) {
    $f = Join-Path $script:logs ("ui-grid-texts-" + $tag + ".txt")
    Invoke-Qa @('texts', $winTitle, $f) | Out-Null
    if (Test-Path $f) { return @(Get-Content $f -Encoding UTF8) }
    return @()
}
function Has-Text($texts, [string]$needle) {
    foreach ($l in $texts) { if ($l.IndexOf($needle, [StringComparison]::Ordinal) -ge 0) { return $true } }
    return $false
}
function Check-Has([string]$name, $texts, [string]$needle) {
    Check $name (Has-Text $texts $needle) ("包含「" + $needle + "」") "texts"
}
function Count-TextVisible($texts, [string]$needle) {
    $n = 0
    foreach ($l in $texts) {
        if ($l.IndexOf($needle, [StringComparison]::Ordinal) -ge 0 -and $l.IndexOf('off=False', [StringComparison]::Ordinal) -ge 0) { $n++ }
    }
    return $n
}
function Count-TextOff($texts, [string]$needle) {
    $n = 0
    foreach ($l in $texts) {
        if ($l.IndexOf($needle, [StringComparison]::Ordinal) -ge 0 -and $l.IndexOf('off=True', [StringComparison]::Ordinal) -ge 0) { $n++ }
    }
    return $n
}
function Get-Line([string]$needle, $texts) {
    foreach ($l in $texts) { if ($l.IndexOf($needle, [StringComparison]::Ordinal) -ge 0) { return $l } }
    return $null
}
function Shot([string]$name) {
    $f = Join-Path $script:screens $name
    Invoke-Qa @('shot', $script:title, $f) | Out-Null
    return $f
}
function ShotWin([string]$winTitle, [string]$name) {
    $f = Join-Path $script:screens $name
    Invoke-Qa @('shot', $winTitle, $f) | Out-Null
    return $f
}
function Save-App([string]$dir) {
    $o = Invoke-Qa @('ctrlsave', $script:title, $dir)
    $ms = -1
    if ($o -match 'ms=(\d+)') { $ms = [int]$Matches[1] }
    $file = $null
    if ($o -match 'file=(.+) size=(\d+)') { $file = $Matches[1].Trim() }
    return @{ output = $o; ms = $ms; file = $file }
}
function Img-Info([string]$f) { return (& $script:py $script:imgcheck 'info' $f | Out-String | ConvertFrom-Json) }
function Img-Pixel([string]$f, [int]$x, [int]$y) { return (& $script:py $script:imgcheck 'pixel' $f $x $y | Out-String | ConvertFrom-Json) }
function Img-Alpha([string]$f) { return (& $script:py $script:imgcheck 'alpha' $f | Out-String | ConvertFrom-Json) }
function Img-Diff([string]$a, [string]$b) { return (& $script:py $script:imgcheck 'diff' $a $b | Out-String | ConvertFrom-Json) }
function Img-Present([string]$f, [string]$spec) { return (& $script:py $script:imgcheck 'present' $f $spec | Out-String | ConvertFrom-Json) }
function PxIs($pixel, [int]$r, [int]$g, [int]$b, [int]$a, [int]$tol) {
    if (-not $pixel.ok) { return $false }
    $v = $pixel.rgba
    return ([math]::Abs($v[0] - $r) -le $tol) -and ([math]::Abs($v[1] - $g) -le $tol) -and ([math]::Abs($v[2] - $b) -le $tol) -and ([math]::Abs($v[3] - $a) -le $tol)
}
function Alpha-Is($pixel, [int]$a, [int]$tol) {
    if (-not $pixel.ok) { return $false }
    return [math]::Abs($pixel.rgba[3] - $a) -le $tol
}
function Read-Ini([string]$set) {
    $f = Join-Path $set 'settings.ini'
    $h = @{}
    if (Test-Path $f) {
        foreach ($line in (Get-Content $f -Encoding UTF8)) {
            if ($line -match '^([A-Za-z]+)=(.*)$') { $h[$Matches[1]] = $Matches[2] }
        }
    }
    return $h
}
function Wait-Ini([string]$set, [string]$key, [string]$value, [int]$timeoutMs) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -le $timeoutMs) {
        $ini = Read-Ini $set
        if ($ini[$key] -eq $value) { return $true }
        Start-Sleep -Milliseconds 60
    }
    return $false
}
function Seed-Settings([string]$tag, [hashtable]$overrides) {
    $dir = Join-Path $script:qa ("settings\grid-" + $tag)
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $base = @{
        Orientation = 'Horizontal'; Gap = 12; Margin = 16; BackgroundArgb = '#00000000'; AutoBaseSize = 1; BaseSize = 0;
        Format = 'Png'; JpegQuality = 92; OutputSizeMode = 'Auto'; OutputTarget = 2000;
        GridRows = 3; GridColumns = 3; RowGap = 12; ColumnGap = 12; GridMerges = ''; ShowGridGuides = 1
    }
    if ($overrides) { foreach ($k in $overrides.Keys) { $base[$k] = $overrides[$k] } }
    $lines = @('[collage]')
    foreach ($k in $base.Keys) { $lines += ($k + '=' + $base[$k]) }
    $lines += @('', '[window]', 'Width=1180', 'Height=780', 'X=-2147483648', 'Y=-2147483648', 'Maximized=0', '', '[general]', 'AutoOpenAfterSave=0')
    Set-Content -Path (Join-Path $dir 'settings.ini') -Value $lines -Encoding UTF8
    return $dir
}
function Write-RawSettings([string]$tag, [string]$content) {
    $dir = Join-Path $script:qa ("settings\grid-" + $tag)
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Set-Content -Path (Join-Path $dir 'settings.ini') -Value $content -Encoding UTF8
    return $dir
}
function Invoke-CliCompose([string[]]$composeArgs, [string[]]$images, [string]$outFile) {
    if (Test-Path $outFile) { Remove-Item $outFile -Force }
    $out = & $script:exe '--compose' @composeArgs '--out' $outFile @images 2>&1 | Out-String
    $exists = Test-Path $outFile
    if (-not $exists) { Log ("CLI_COMPOSE_FAILED out=" + $outFile + " output=" + $out.Trim()) }
    return @{ output = $out.Trim(); file = $outFile; exists = $exists }
}

# ---------------------------------------------------------------- 网格期望独立实现（PowerShell）
function Round-Away([double]$v) {
    if ($v -ge 0) { return [int][math]::Floor($v + 0.5) }
    return -[int][math]::Floor(-$v + 0.5)
}
function Median-Val([int[]]$values) {
    if ($values.Count -eq 0) { return 0 }
    $s = @($values | Sort-Object)
    $n = $s.Count
    if ($n % 2 -eq 1) { $m = [double]$s[[int](($n - 1) / 2)] } else { $m = ([double]$s[$n / 2 - 1] + [double]$s[$n / 2]) / 2.0 }
    $r = Round-Away $m
    if ($r -lt 1) { return 1 }
    return $r
}
function Grid-Regions([int]$rows, [int]$cols, $merges) {
    # PowerShell 数组字面量 @(@(r0,c0,r1,c1)) 会把单元素展开成 4 个整数；这里防御性还原
    if ($merges -and $merges.Count -eq 4 -and -not ($merges[0] -is [array])) { $merges = ,$merges }
    $occupied = @()
    for ($r = 0; $r -lt $rows; $r++) { $occupied += ,(@($false) * $cols) }
    $accepted = New-Object System.Collections.ArrayList
    if ($merges) {
        foreach ($m in $merges) {
            $r0 = [int]$m[0]; $c0 = [int]$m[1]; $r1 = [int]$m[2]; $c1 = [int]$m[3]
            $rs = $r1 - $r0 + 1; $cs = $c1 - $c0 + 1
            if ($rs -lt 1 -or $cs -lt 1 -or ($rs * $cs) -lt 2) { continue }
            if ($r0 -lt 0 -or $c0 -lt 0 -or $r1 -ge $rows -or $c1 -ge $cols) { continue }
            $overlap = $false
            for ($r = $r0; $r -le $r1; $r++) {
                for ($c = $c0; $c -le $c1; $c++) { if ($occupied[$r][$c]) { $overlap = $true } }
            }
            if ($overlap) { continue }
            for ($r = $r0; $r -le $r1; $r++) {
                for ($c = $c0; $c -le $c1; $c++) { $occupied[$r][$c] = $true }
            }
            [void]$accepted.Add(@($r0, $c0, $r1, $c1))
        }
    }
    $covered = @()
    for ($r = 0; $r -lt $rows; $r++) { $covered += ,(@($false) * $cols) }
    $top = @{}
    foreach ($m in $accepted) {
        for ($r = $m[0]; $r -le $m[2]; $r++) { for ($c = $m[1]; $c -le $m[3]; $c++) { $covered[$r][$c] = $true } }
        $top["$($m[0]),$($m[1])"] = $m
    }
    $regions = New-Object System.Collections.ArrayList
    for ($r = 0; $r -lt $rows; $r++) {
        for ($c = 0; $c -lt $cols; $c++) {
            if ($covered[$r][$c]) {
                $key = "$r,$c"
                if ($top.ContainsKey($key)) { [void]$regions.Add($top[$key]) }
                continue
            }
            [void]$regions.Add(@($r, $c, $r, $c))
        }
    }
    return $regions
}
function Expected-Grid([int[]]$widths, [int[]]$heights, [int]$rows, [int]$cols, $merges, [int]$rowGap, [int]$colGap, [int]$margin) {
    $regions = Grid-Regions $rows $cols $merges
    $cellW = Median-Val $widths
    $cellH = Median-Val $heights
    $cw = 2 * $margin + $cols * $cellW + ($cols - 1) * $colGap
    $ch = 2 * $margin + $rows * $cellH + ($rows - 1) * $rowGap
    $items = New-Object System.Collections.ArrayList
    foreach ($rg in $regions) {
        $x = $margin + $rg[1] * ($cellW + $colGap)
        $y = $margin + $rg[0] * ($cellH + $rowGap)
        $w = ($rg[3] - $rg[1] + 1) * $cellW + ($rg[3] - $rg[1]) * $colGap
        $h = ($rg[2] - $rg[0] + 1) * $cellH + ($rg[2] - $rg[0]) * $rowGap
        [void]$items.Add(@($x, $y, $w, $h))
    }
    $placed = $widths.Count
    if ($placed -gt $items.Count) { $placed = $items.Count }
    return @{ cw = $cw; ch = $ch; cellw = $cellW; cellh = $cellH; capacity = $items.Count; placed = $placed; items = $items }
}
function Check-GridSize([string]$name, [string]$file, $exp) {
    $info = Img-Info $file
    Check $name (($info.ok) -and ($info.size[0] -eq $exp.cw) -and ($info.size[1] -eq $exp.ch)) ("{0}x{1}" -f $exp.cw, $exp.ch) ("{0}x{1}" -f $info.size[0], $info.size[1])
}
function Check-GridCenters([string]$name, [string]$file, $exp, $colors, [int]$tol = 1) {
    $bad = @()
    $count = [math]::Min($colors.Count, $exp.items.Count)
    for ($i = 0; $i -lt $count; $i++) {
        $rect = $exp.items[$i]
        $x = [int]($rect[0] + [math]::Floor($rect[2] / 2))
        $y = [int]($rect[1] + [math]::Floor($rect[3] / 2))
        $p = Img-Pixel $file $x $y
        if (-not (PxIs $p $colors[$i][0] $colors[$i][1] $colors[$i][2] $colors[$i][3] $tol)) {
            $bad += ("i=$i@($x,$y) got=" + ($p.rgba -join ','))
        }
    }
    Check ($name + ".order") ($bad.Count -eq 0) ("前 " + $count + " 个区域中心按行优先颜色正确") ($(if ($bad.Count -eq 0) { 'ok' } else { $bad -join '; ' }))
}
function Color-Spec($colors, [int]$from) {
    $parts = @()
    for ($i = $from; $i -lt $colors.Count; $i++) { $parts += ("{0},{1},{2},{3}" -f $colors[$i][0], $colors[$i][1], $colors[$i][2], $colors[$i][3]) }
    return ($parts -join ';')
}

# ---------------------------------------------------------------- 环境与测试图
Log ("===== 轻拼 v1.1 网格 UI 验收开始 " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + " =====")
Log ("EXE=" + $script:exe)
Log ("EXE_SIZE=" + (Get-Item $script:exe).Length + " SHA256=" + (Get-FileHash $script:exe -Algorithm SHA256).Hash)
Log ("QaTool=" + $script:qaTool + " Python=" + $script:py)
Remove-Item Env:\LIGHTCOLLAGE_SETTINGS_DIR -ErrorAction SilentlyContinue
App-Kill

& $script:py -c @"
from PIL import Image
import os
d = r'$script:gridDir'
colors = [(255,0,0,255),(0,255,0,255),(0,0,255,255),(255,255,0,255),(255,0,255,255),(0,255,255,255),(255,128,0,255),(128,0,255,255),(0,128,128,255)]
for i in range(9):
    Image.new('RGBA', (200, 120), colors[i]).save(os.path.join(d, 'g9-%d.png' % i))
for i in range(4):
    Image.new('RGBA', (200, 120), colors[i]).save(os.path.join(d, 'g4-%d.png' % i))
for i in range(3):
    Image.new('RGBA', (200, 120), colors[i]).save(os.path.join(d, 'g3-%d.png' % i))
    Image.new('RGBA', (10, 10), colors[i]).save(os.path.join(d, 's10-%d.png' % i))
# 单实例追加用第 10 张不同图片（不能与已加载文件重复：MainForm 会跳过重复路径）
Image.new('RGBA', (200, 120), (64, 64, 64, 255)).save(os.path.join(d, 'g10.png'))
# v1.0 零回归用三张样例尺寸（与 collage-samples/photo1-3 相同 1200x800 / 800x1200 / 1600x900）
for i, size in enumerate([(1200, 800), (800, 1200), (1600, 900)]):
    Image.new('RGBA', size, colors[i]).save(os.path.join(d, 'lin-%d.png' % i))
print('grid fixtures ok')
"@ | Out-Null

$C = @(
    @(255, 0, 0, 255), @(0, 255, 0, 255), @(0, 0, 255, 255),
    @(255, 255, 0, 255), @(255, 0, 255, 255), @(0, 255, 255, 255),
    @(255, 128, 0, 255), @(128, 0, 255, 255), @(0, 128, 128, 255)
)
$g9 = @(); for ($i = 0; $i -lt 9; $i++) { $g9 += (Join-Path $script:gridDir ("g9-" + $i + ".png")) }
$g4 = @(); for ($i = 0; $i -lt 4; $i++) { $g4 += (Join-Path $script:gridDir ("g4-" + $i + ".png")) }
$g3 = @(); for ($i = 0; $i -lt 3; $i++) { $g3 += (Join-Path $script:gridDir ("g3-" + $i + ".png")) }
$s10 = @(); for ($i = 0; $i -lt 3; $i++) { $s10 += (Join-Path $script:gridDir ("s10-" + $i + ".png")) }

# =====================================================================
# U1 三段拼接方式 / 网格设置区显隐 / 自动尺寸 / 预览胶囊 / 保存像素 / 与 CLI 一致
# =====================================================================
Log ''
Log '----- U1 三段切换 + 网格 3×3 自动布局 -----'
$setMain = Seed-Settings 'main' @{ Orientation = 'Horizontal' }
Start-App $g9 $setMain
$t = Get-Texts 'u1-default'
Check-Has 'u1.default.linear_label' $t '共 9 张图片 · 横向拼接'
Check 'u1.default.grid_controls_hidden' ((Count-TextVisible $t '行数') -eq 0 -and (Count-TextVisible $t '合并单元格') -eq 0) '网格控件未显示' ('visible_rows=' + (Count-TextVisible $t '行数'))

$sw = Invoke-Qa @('segwait', $script:title, '拼接方式', '2', '网格拼接', '4000', '3')
Check 'u1.switch_grid' ($sw -match 'SEGWAIT_OK') '切换到网格并刷新' $sw
Start-Sleep -Milliseconds 500
$t = Get-Texts 'u1-grid'
Check-Has 'u1.count_text' $t '共 9 张图片 · 网格拼接 3×3 · 容量 9 · 合并 0 处'
Check 'u1.grid_section_title' ((Count-TextVisible $t '网格设置') -ge 1) '侧栏「网格设置」区可见' ('visible=' + (Count-TextVisible $t '网格设置'))
Check-Has 'u1.size_hint' $t '自动：单元格取图片尺寸中位数'
# 说明：预览左上/右上信息胶囊为 PreviewPanel 自绘文本，UIA 不暴露；以截图 + 保存尺寸（656×416）交叉验证。
Check 'u1.grid_controls_visible' ((Count-TextVisible $t '行数') -ge 1 -and (Count-TextVisible $t '列数') -ge 1 -and (Count-TextVisible $t '行间距') -ge 1 -and (Count-TextVisible $t '列间距') -ge 1 -and (Count-TextVisible $t '合并单元格') -ge 1 -and (Count-TextVisible $t '显示网格辅助线') -ge 1) '网格设置区控件可见' ('rows=' + (Count-TextVisible $t '行数') + ' merge=' + (Count-TextVisible $t '合并单元格'))
Check 'u1.gap_hidden' ((Count-TextVisible $t '图片间距') -eq 0) '「图片间距」在网格模式隐藏' ('visible_gap=' + (Count-TextVisible $t '图片间距'))
$shot = Shot 'v11-01-grid-3x3.png'
Check 'u1.screenshot' (Test-Path $shot) '截图存在' $shot
$okIni = Wait-Ini $setMain 'Orientation' 'Grid' 3000
Check 'u1.ini_orientation' $okIni 'Orientation=Grid' (Read-Ini $setMain).Orientation
$okIni = Wait-Ini $setMain 'GridRows' '3' 3000
Check 'u1.ini_gridrows' $okIni 'GridRows=3' (Read-Ini $setMain).GridRows

$exp = Expected-Grid @(200, 200, 200, 200, 200, 200, 200, 200, 200) @(120, 120, 120, 120, 120, 120, 120, 120, 120) 3 3 $null 12 12 16
$saveU1 = Save-App $script:gridDir
Check 'u1.save.ok' ($null -ne $saveU1.file) '保存成功' $saveU1.output
if ($saveU1.file) {
    Check-GridSize 'u1.save.size' $saveU1.file $exp
    Check-GridCenters 'u1.save' $saveU1.file $exp $C 1
    $p = Img-Pixel $saveU1.file 0 0
    Check 'u1.save.margin_alpha0' (Alpha-Is $p 0 1) '角像素 alpha=0' ($p.rgba -join ',')
    $p = Img-Pixel $saveU1.file (16 + 200 + 6) (16 + 60)
    Check 'u1.save.col_gap_alpha0' (Alpha-Is $p 0 1) '列间距 alpha=0' ($p.rgba -join ',')
    $p = Img-Pixel $saveU1.file (16 + 100) (16 + 120 + 6)
    Check 'u1.save.row_gap_alpha0' (Alpha-Is $p 0 1) '行间距 alpha=0' ($p.rgba -join ',')
    # UI 保存 vs CLI 同参数像素一致（A16/预览=保存）
    $cliOut = Join-Path $script:outDir 'ui-grid-3x3-cli.png'
    Invoke-CliCompose @('--layout', 'g', '--rows', '3', '--cols', '3', '--row-gap', '12', '--col-gap', '12', '--margin', '16', '--bg', '#00000000', '--format', 'png') $g9 $cliOut | Out-Null
    $d = Img-Diff $saveU1.file $cliOut
    Check 'u1.ui_vs_cli_pixels' ($d.ok -and $d.same) 'UI 保存与 CLI 同参数网格输出像素完全一致' ($d | ConvertTo-Json -Compress)
}

# =====================================================================
# U2 行列设置（4×3 / 3×4）/ U3 行、列间距独立生效
# =====================================================================
Log ''
Log '----- U2 行列设置 / U3 行列间距 -----'
Invoke-Qa @('step', $script:title, '行数', '+', '1') | Out-Null
$ok = Wait-Ini $setMain 'GridRows' '4' 3000
Check 'u2.rows_plus1.ini' $ok 'GridRows=4' (Read-Ini $setMain).GridRows
Start-Sleep -Milliseconds 700
$t = Get-Texts 'u2-rows4'
Check-Has 'u2.rows4.count_text' $t '网格拼接 4×3 · 容量 12'
$exp = Expected-Grid (@(200) * 9) (@(120) * 9) 4 3 $null 12 12 16
$s = Save-App $script:gridDir
if ($s.file) { Check-GridSize 'u2.rows4.save_size' $s.file $exp; Check-GridCenters 'u2.rows4.save' $s.file $exp $C 1 }
Invoke-Qa @('step', $script:title, '行数', '-', '1') | Out-Null
$ok = Wait-Ini $setMain 'GridRows' '3' 3000
Check 'u2.rows_restore.ini' $ok 'GridRows=3' (Read-Ini $setMain).GridRows
Invoke-Qa @('step', $script:title, '列数', '+', '1') | Out-Null
$ok = Wait-Ini $setMain 'GridColumns' '4' 3000
Check 'u2.cols_plus1.ini' $ok 'GridColumns=4' (Read-Ini $setMain).GridColumns
Start-Sleep -Milliseconds 700
$t = Get-Texts 'u2-cols4'
Check-Has 'u2.cols4.count_text' $t '网格拼接 3×4 · 容量 12'
$exp = Expected-Grid (@(200) * 9) (@(120) * 9) 3 4 $null 12 12 16
$s = Save-App $script:gridDir
if ($s.file) { Check-GridSize 'u2.cols4.save_size' $s.file $exp; Check-GridCenters 'u2.cols4.save' $s.file $exp $C 1 }
Invoke-Qa @('step', $script:title, '列数', '-', '1') | Out-Null
$ok = Wait-Ini $setMain 'GridColumns' '3' 3000
Check 'u2.cols_restore.ini' $ok 'GridColumns=3' (Read-Ini $setMain).GridColumns

Invoke-Qa @('step', $script:title, '行间距', '+', '3') | Out-Null
$ok = Wait-Ini $setMain 'RowGap' '15' 3000
Check 'u3.row_gap_plus3.ini' $ok 'RowGap=15' (Read-Ini $setMain).RowGap
$exp = Expected-Grid (@(200) * 9) (@(120) * 9) 3 3 $null 15 12 16
$s = Save-App $script:gridDir
if ($s.file) { Check-GridSize 'u3.row_gap15.save_size' $s.file $exp }
Invoke-Qa @('step', $script:title, '行间距', '-', '3') | Out-Null
$ok = Wait-Ini $setMain 'RowGap' '12' 3000
Check 'u3.row_gap_restore.ini' $ok 'RowGap=12' (Read-Ini $setMain).RowGap
Invoke-Qa @('step', $script:title, '列间距', '+', '3') | Out-Null
$ok = Wait-Ini $setMain 'ColumnGap' '15' 3000
Check 'u3.col_gap_plus3.ini' $ok 'ColumnGap=15' (Read-Ini $setMain).ColumnGap
$exp = Expected-Grid (@(200) * 9) (@(120) * 9) 3 3 $null 12 15 16
$s = Save-App $script:gridDir
if ($s.file) { Check-GridSize 'u3.col_gap15.save_size' $s.file $exp }
Invoke-Qa @('step', $script:title, '列间距', '-', '3') | Out-Null
$ok = Wait-Ini $setMain 'ColumnGap' '12' 3000
Check 'u3.col_gap_restore.ini' $ok 'ColumnGap=12' (Read-Ini $setMain).ColumnGap

# =====================================================================
# U4/U5 合并弹窗：合并 2×2 → 容量不足未放入；拆分；清空；取消不应用
# =====================================================================
Log ''
Log '----- U4/U5 合并弹窗 / 容量不足 -----'
Invoke-Qa @('clickname', $script:title, '合并单元格') | Out-Null
$dlg = Invoke-Qa @('waitwin', '编辑网格合并', '8000')
Check 'u4.dialog.open' ($dlg -match 'FOUND') '合并编辑器弹窗出现' $dlg
Start-Sleep -Milliseconds 700
ShotWin '编辑网格合并' 'v11-02-merge-dialog.png' | Out-Null
Invoke-Qa @('uia', '编辑网格合并', (Join-Path $script:logs 'ui-grid-merge-dialog-uia.txt')) | Out-Null
$dt = Get-TextsOf '编辑网格合并' 'u4-dialog'
Check-Has 'u4.dialog.matrix' $dt '网格矩阵'
Check-Has 'u4.dialog.capacity9' $dt '容量 9 格 · 合并 0 处'
Check-Has 'u4.dialog.images9' $dt '图片 9 张'
Check-Has 'u4.dialog.caption' $dt '网格 3 × 3'

$mr = Invoke-Qa @('matrixdrag', '编辑网格合并', '3', '3', '0', '0', '1', '1')
Check 'u4.matrixdrag.exec' ($mr -match 'MATRIXDRAG') '矩阵拖选执行' $mr
Start-Sleep -Milliseconds 400
$dt = Get-TextsOf '编辑网格合并' 'u4-dialog-selected'
Check-Has 'u4.dialog.selection' $dt '第 1–2 行 · 第 1–2 列（2 × 2）'
Check-Has 'u4.dialog.preview_capacity' $dt '合并后容量：9 → 6 格（减少 3 格）'
ShotWin '编辑网格合并' 'v11-03-merge-dialog-selected.png' | Out-Null
Invoke-Qa @('clickname', '编辑网格合并', '合并所选') | Out-Null
Start-Sleep -Milliseconds 500
$dt = Get-TextsOf '编辑网格合并' 'u4-dialog-merged'
Check-Has 'u4.dialog.merged_stats' $dt '容量 6 格 · 合并 1 处'
Check-Has 'u4.dialog.merged_message' $dt '已合并所选区域'
Invoke-Qa @('clickname', '编辑网格合并', '确定') | Out-Null
Invoke-Qa @('waitgone', '编辑网格合并', '5000') | Out-Null
Start-Sleep -Milliseconds 900
$t = Get-Texts 'u5-capacity-shortage'
Check-Has 'u5.count_text' $t '共 9 张图片 · 网格拼接 3×3 · 容量 6 · 合并 1 处 · 未放入 3 张'
Check-Has 'u5.thumbnail_strip' $t '缩略图条'
$shot = Shot 'v11-04-merge-applied-capacity-shortage.png'
Check 'u5.screenshot' (Test-Path $shot) '截图存在（未放入提示/角标）' $shot
$exp = Expected-Grid (@(200) * 9) (@(120) * 9) 3 3 @(@(0, 0, 1, 1)) 12 12 16
$s = Save-App $script:gridDir
Check 'u5.save.ok' ($null -ne $s.file) '合并后保存成功' $s.output
if ($s.file) {
    Check-GridSize 'u5.save.size' $s.file $exp
    Check-GridCenters 'u5.save' $s.file $exp $C 1
    $spec = Color-Spec $C 6
    $pr = Img-Present $s.file $spec
    Check 'u5.overflow_not_drawn' ($pr.ok -and $pr.present.Count -eq 0) '第 7–9 张图颜色不出现在输出（未放入）' ($pr.present | ConvertTo-Json -Compress)
    $cliOut = Join-Path $script:outDir 'ui-grid-merge-cli.png'
    Invoke-CliCompose @('--layout', 'g', '--rows', '3', '--cols', '3', '--row-gap', '12', '--col-gap', '12', '--margin', '16', '--merge', '1,1,2,2', '--bg', '#00000000', '--format', 'png') $g9 $cliOut | Out-Null
    $d = Img-Diff $s.file $cliOut
    Check 'u5.ui_vs_cli_pixels' ($d.ok -and $d.same) '合并网格 UI 保存与 CLI 像素一致' ($d | ConvertTo-Json -Compress)
}

# 拆分（选中大格内任意格 → 拆分所选 → 确定）
Invoke-Qa @('clickname', $script:title, '合并单元格') | Out-Null
Invoke-Qa @('waitwin', '编辑网格合并', '8000') | Out-Null
Start-Sleep -Milliseconds 600
Invoke-Qa @('matrixdrag', '编辑网格合并', '3', '3', '0', '0', '0', '0') | Out-Null
Start-Sleep -Milliseconds 300
Invoke-Qa @('clickname', '编辑网格合并', '拆分所选') | Out-Null
Start-Sleep -Milliseconds 500
$dt = Get-TextsOf '编辑网格合并' 'u4-dialog-split'
Check-Has 'u4.split.message' $dt '已拆分 1 处合并区域（剩余 0 处）'
Check-Has 'u4.split.stats' $dt '容量 9 格 · 合并 0 处'
Invoke-Qa @('clickname', '编辑网格合并', '确定') | Out-Null
Invoke-Qa @('waitgone', '编辑网格合并', '5000') | Out-Null
Start-Sleep -Milliseconds 800
$t = Get-Texts 'u4-after-split'
Check-Has 'u4.split.count_text' $t '网格拼接 3×3 · 容量 9 · 合并 0 处'
$exp = Expected-Grid (@(200) * 9) (@(120) * 9) 3 3 $null 12 12 16
$s = Save-App $script:gridDir
if ($s.file) {
    Check-GridSize 'u4.split.save_size' $s.file $exp
    $pr = Img-Present $s.file (Color-Spec $C 0)
    Check 'u4.split.all9_present' ($pr.ok -and $pr.present.Count -eq 9) '拆分后 9 张图颜色全部出现' ($pr.present.Count)
}

# 清空所有合并
Invoke-Qa @('clickname', $script:title, '合并单元格') | Out-Null
Invoke-Qa @('waitwin', '编辑网格合并', '8000') | Out-Null
Start-Sleep -Milliseconds 600
Invoke-Qa @('matrixdrag', '编辑网格合并', '3', '3', '0', '0', '0', '1') | Out-Null
Start-Sleep -Milliseconds 300
Invoke-Qa @('clickname', '编辑网格合并', '合并所选') | Out-Null
Start-Sleep -Milliseconds 400
Invoke-Qa @('clickname', '编辑网格合并', '清空所有合并') | Out-Null
Start-Sleep -Milliseconds 500
$dt = Get-TextsOf '编辑网格合并' 'u4-dialog-clear'
Check-Has 'u4.clear.message' $dt '已清空 1 处合并'
Check-Has 'u4.clear.stats' $dt '容量 9 格 · 合并 0 处'
Invoke-Qa @('clickname', '编辑网格合并', '取消') | Out-Null
Invoke-Qa @('waitgone', '编辑网格合并', '5000') | Out-Null
Start-Sleep -Milliseconds 600
$t = Get-Texts 'u4-after-clear-cancel'
Check-Has 'u4.clear.cancel_keeps' $t '网格拼接 3×3 · 容量 9 · 合并 0 处'

# 取消不应用：合并后点取消 → 主界面结构不变
Invoke-Qa @('clickname', $script:title, '合并单元格') | Out-Null
Invoke-Qa @('waitwin', '编辑网格合并', '8000') | Out-Null
Start-Sleep -Milliseconds 600
Invoke-Qa @('matrixdrag', '编辑网格合并', '3', '3', '0', '0', '1', '1') | Out-Null
Start-Sleep -Milliseconds 300
Invoke-Qa @('clickname', '编辑网格合并', '合并所选') | Out-Null
Start-Sleep -Milliseconds 400
Invoke-Qa @('clickname', '编辑网格合并', '取消') | Out-Null
Invoke-Qa @('waitgone', '编辑网格合并', '5000') | Out-Null
Start-Sleep -Milliseconds 600
$t = Get-Texts 'u4-after-cancel'
Check-Has 'u4.cancel.not_applied' $t '网格拼接 3×3 · 容量 9 · 合并 0 处'

# =====================================================================
# U6 网格 JPG（白底、无 alpha）
# =====================================================================
Log ''
Log '----- U6 网格 JPG -----'
# 网格模式侧栏为紧凑布局时格式说明文案可能隐藏，改用保存按钮名称「保存 JPG」判定切换
$sw = Invoke-Qa @('segwait', $script:title, '输出格式选择', '1', '保存 JPG', '4000', '2')
Check 'u6.switch_jpg' ($sw -match 'SEGWAIT_OK') '切换 JPG（保存按钮变「保存 JPG」）' $sw
$ok = Wait-Ini $setMain 'Format' 'Jpeg' 3000
Check 'u6.ini_format_jpeg' $ok 'Format=Jpeg' (Read-Ini $setMain).Format
Start-Sleep -Milliseconds 500
$s = Save-App $script:gridDir
Check 'u6.save.ok' ($null -ne $s.file -and $s.file -match '\.jpg$') '保存为 .jpg' $s.output
if ($s.file) {
    $info = Img-Info $s.file
    Check 'u6.mode_no_alpha' ($info.mode -eq 'RGB' -or $info.mode -eq 'L') 'JPG 无 Alpha' $info.mode
    Check 'u6.size' ($info.size[0] -eq 656 -and $info.size[1] -eq 416) '656×416' ("{0}x{1}" -f $info.size[0], $info.size[1])
    $p = Img-Pixel $s.file 0 0
    Check 'u6.margin_white' (PxIs $p 255 255 255 255 6) '透明边距压白' ($p.rgba -join ',')
    $p = Img-Pixel $s.file (16 + 200 + 6) (16 + 60)
    Check 'u6.gap_white' (PxIs $p 255 255 255 255 6) '透明间隙压白' ($p.rgba -join ',')
    $bad = @()
    for ($ci = 0; $ci -lt 9; $ci++) {
        $col = $ci % 3; $row = [int][math]::Floor($ci / 3)
        $x = 16 + $col * 212 + 100; $y = 16 + $row * 132 + 60
        $p = Img-Pixel $s.file $x $y
        if (-not (PxIs $p $C[$ci][0] $C[$ci][1] $C[$ci][2] 255 12)) { $bad += "i=$ci got=$($p.rgba -join ',')" }
    }
    Check 'u6.centers' ($bad.Count -eq 0) '九宫格中心颜色近似（JPEG 容差 12）' ($(if ($bad.Count -eq 0) { 'ok' } else { $bad -join '; ' }))
}
Invoke-Qa @('segwait', $script:title, '输出格式选择', '0', '保存图片', '4000', '2') | Out-Null
App-Kill

# =====================================================================
# U7 网格预览拖动对调（成功 / 无效目标 / Esc 取消）
# =====================================================================
Log ''
Log '----- U7 网格预览拖动对调 -----'
$setDrag = Seed-Settings 'drag' @{ Orientation = 'Grid'; GridRows = 2; GridColumns = 2; RowGap = 0; ColumnGap = 0; Margin = 0; Gap = 0 }
Start-App $g4 $setDrag
$t = Get-Texts 'u7-start'
Check-Has 'u7.start.count' $t '网格拼接 2×2 · 容量 4 · 合并 0 处'
$exp = Expected-Grid (@(200) * 4) (@(120) * 4) 2 2 $null 0 0 0
$s0 = Save-App $script:gridDir
if ($s0.file) { Check-GridSize 'u7.baseline.size' $s0.file $exp; Check-GridCenters 'u7.baseline' $s0.file $exp $C 1 }
$pd = Invoke-Qa @('previewdrag', $script:title, '400', '240', '100', '60', '300', '60')
Check 'u7.drag.exec' ($pd -match 'PREVIEWDRAG') '预览拖动执行（1/3 图中心）' $pd
Start-Sleep -Milliseconds 1200
$t = Get-Texts 'u7-after-swap'
Check-Has 'u7.drag.status' $t '已交换 第1张 ↔ 第2张'
$shot = Shot 'v11-05-preview-swap-2x2.png'
Check 'u7.screenshot' (Test-Path $shot) '截图存在' $shot
$s1 = Save-App $script:gridDir
if ($s1.file) {
    $expSwap = Expected-Grid (@(200) * 4) (@(120) * 4) 2 2 $null 0 0 0
    $colorsSwap = @($C[1], $C[0], $C[2], $C[3])
    Check-GridSize 'u7.swap.size' $s1.file $expSwap
    Check-GridCenters 'u7.swap' $s1.file $expSwap $colorsSwap 1
}
App-Kill

# 无效目标（拖到空位）不生效
$setDrag3 = Seed-Settings 'drag3' @{ Orientation = 'Grid'; GridRows = 2; GridColumns = 2; RowGap = 0; ColumnGap = 0; Margin = 0; Gap = 0 }
Start-App $g3 $setDrag3
$t = Get-Texts 'u7b-start'
Check-Has 'u7b.start.count' $t '网格拼接 2×2 · 容量 4 · 合并 0 处'
$pd = Invoke-Qa @('previewdrag', $script:title, '400', '240', '100', '60', '300', '180')
Check 'u7b.drag.exec' ($pd -match 'PREVIEWDRAG') '拖动到空格执行' $pd
Start-Sleep -Milliseconds 900
$t = Get-Texts 'u7b-after'
Check-Has 'u7b.invalid_target_status' $t '已取消拖动'
$exp3 = Expected-Grid (@(200) * 3) (@(120) * 3) 2 2 $null 0 0 0
$s = Save-App $script:gridDir
if ($s.file) {
    Check-GridSize 'u7b.size' $s.file $exp3
    Check-GridCenters 'u7b.order_unchanged' $s.file $exp3 @($C[0], $C[1], $C[2]) 1
}
$pd = Invoke-Qa @('previewdrag', $script:title, '400', '240', '100', '60', '300', '60', '700', 'esc')
Check 'u7b.esc.exec' ($pd -match 'PREVIEWDRAG') '拖动中按 Esc' $pd
Start-Sleep -Milliseconds 700
$t = Get-Texts 'u7c-after-esc'
Check-Has 'u7c.esc.commit' $t '已取消拖动'
$s = Save-App $script:gridDir
if ($s.file) {
    Check-GridCenters 'u7c.order_unchanged_after_esc' $s.file $exp3 @($C[0], $C[1], $C[2]) 1
}
App-Kill

# =====================================================================
# U8 横向 / 纵向也支持预览拖动对调（A10）
# =====================================================================
Log ''
Log '----- U8 横向/纵向预览拖动（A10） -----'
$setH = Seed-Settings 'dragH' @{ Orientation = 'Horizontal'; Gap = 0; Margin = 0 }
Start-App $g3 $setH
$t = Get-Texts 'u8-h-start'
Check-Has 'u8.h.count' $t '共 3 张图片 · 横向拼接'
$expH = Expected-Grid (@(200) * 3) (@(120) * 3) 1 3 $null 0 0 0
$s0 = Save-App $script:gridDir
if ($s0.file) { Check-GridSize 'u8.h.baseline_size' $s0.file $expH }
$pd = Invoke-Qa @('previewdrag', $script:title, '600', '120', '100', '60', '300', '60')
Check 'u8.h.drag.exec' ($pd -match 'PREVIEWDRAG') '横向预览拖动执行' $pd
Start-Sleep -Milliseconds 1000
$t = Get-Texts 'u8-h-after'
Check-Has 'u8.h.swap_status' $t '已交换 第1张 ↔ 第2张'
$s = Save-App $script:gridDir
if ($s.file) {
    $bad = @()
    $expectedColors = @($C[1], $C[0], $C[2])
    for ($i = 0; $i -lt 3; $i++) {
        $p = Img-Pixel $s.file (100 + $i * 200) 60
        if (-not (PxIs $p $expectedColors[$i][0] $expectedColors[$i][1] $expectedColors[$i][2] 255 1)) { $bad += "i=$i got=$($p.rgba -join ',')" }
    }
    Check 'u8.h.order_after_swap' ($bad.Count -eq 0) '横向保存顺序 c1,c0,c2' ($(if ($bad.Count -eq 0) { 'ok' } else { $bad -join '; ' }))
}
App-Kill

$setV = Seed-Settings 'dragV' @{ Orientation = 'Vertical'; Gap = 0; Margin = 0 }
Start-App $g3 $setV
$t = Get-Texts 'u8-v-start'
Check-Has 'u8.v.count' $t '共 3 张图片 · 纵向拼接'
$pd = Invoke-Qa @('previewdrag', $script:title, '200', '360', '100', '60', '100', '180')
Check 'u8.v.drag.exec' ($pd -match 'PREVIEWDRAG') '纵向预览拖动执行' $pd
Start-Sleep -Milliseconds 1000
$t = Get-Texts 'u8-v-after'
Check-Has 'u8.v.swap_status' $t '已交换 第1张 ↔ 第2张'
$s = Save-App $script:gridDir
if ($s.file) {
    $bad = @()
    $expectedColors = @($C[1], $C[0], $C[2])
    for ($i = 0; $i -lt 3; $i++) {
        $p = Img-Pixel $s.file 100 (60 + $i * 120)
        if (-not (PxIs $p $expectedColors[$i][0] $expectedColors[$i][1] $expectedColors[$i][2] 255 1)) { $bad += "i=$i got=$($p.rgba -join ',')" }
    }
    Check 'u8.v.order_after_swap' ($bad.Count -eq 0) '纵向保存顺序 c1,c0,c2' ($(if ($bad.Count -eq 0) { 'ok' } else { $bad -join '; ' }))
}
App-Kill

# =====================================================================
# U9 设置记忆（行列/间距/合并/辅助线）+ 重启恢复（A8/A14）
# =====================================================================
Log ''
Log '----- U9 设置记忆 / 重启恢复 -----'
$setMem = Seed-Settings 'memory' @{ Orientation = 'Grid'; GridRows = 3; GridColumns = 3; RowGap = 12; ColumnGap = 12 }
Start-App $g9 $setMem
Invoke-Qa @('step', $script:title, '列数', '-', '1') | Out-Null
$ok = Wait-Ini $setMem 'GridColumns' '2' 3000
Check 'u9.cols_to2.ini' $ok 'GridColumns=2' (Read-Ini $setMem).GridColumns
Invoke-Qa @('step', $script:title, '行间距', '+', '8') | Out-Null
$ok = Wait-Ini $setMem 'RowGap' '20' 3000
Check 'u9.rowgap20.ini' $ok 'RowGap=20' (Read-Ini $setMem).RowGap
Invoke-Qa @('step', $script:title, '列间距', '-', '7') | Out-Null
$ok = Wait-Ini $setMem 'ColumnGap' '5' 3000
Check 'u9.colgap5.ini' $ok 'ColumnGap=5' (Read-Ini $setMem).ColumnGap
# 合并第 1 行第 1–2 列（3×2 网格）
Invoke-Qa @('clickname', $script:title, '合并单元格') | Out-Null
Invoke-Qa @('waitwin', '编辑网格合并', '8000') | Out-Null
Start-Sleep -Milliseconds 600
Invoke-Qa @('matrixdrag', '编辑网格合并', '3', '2', '0', '0', '0', '1') | Out-Null
Start-Sleep -Milliseconds 300
Invoke-Qa @('clickname', '编辑网格合并', '合并所选') | Out-Null
Start-Sleep -Milliseconds 400
Invoke-Qa @('clickname', '编辑网格合并', '确定') | Out-Null
Invoke-Qa @('waitgone', '编辑网格合并', '5000') | Out-Null
Start-Sleep -Milliseconds 700
$ok = Wait-Ini $setMem 'GridMerges' '1,1,1,2' 3000
Check 'u9.merges.ini' $ok 'GridMerges=1,1,1,2' (Read-Ini $setMem).GridMerges
# 关闭网格辅助线
Invoke-Qa @('clickname', $script:title, '显示网格辅助线') | Out-Null
$ok = Wait-Ini $setMem 'ShowGridGuides' '0' 3000
Check 'u9.guides_off.ini' $ok 'ShowGridGuides=0' (Read-Ini $setMem).ShowGridGuides
$t = Get-Texts 'u9-before-close'
Check-Has 'u9.before_close.count' $t '共 9 张图片 · 网格拼接 3×2 · 容量 5 · 合并 1 处 · 未放入 4 张'
Stop-AppGraceful
$ini = Read-Ini $setMem
Check 'u9.ini.orientation' ($ini.Orientation -eq 'Grid') 'Orientation=Grid' $ini.Orientation
Check 'u9.ini.rows' ($ini.GridRows -eq '3') 'GridRows=3' $ini.GridRows
Check 'u9.ini.cols' ($ini.GridColumns -eq '2') 'GridColumns=2' $ini.GridColumns
Check 'u9.ini.rowgap' ($ini.RowGap -eq '20') 'RowGap=20' $ini.RowGap
Check 'u9.ini.colgap' ($ini.ColumnGap -eq '5') 'ColumnGap=5' $ini.ColumnGap
Check 'u9.ini.merges' ($ini.GridMerges -eq '1,1,1,2') 'GridMerges=1,1,1,2' $ini.GridMerges

Start-Process -FilePath $script:exe -ArgumentList $g9 | Out-Null
Invoke-Qa @('waitwin', $script:title, '15000') | Out-Null
Start-Sleep -Milliseconds 1800
$t = Get-Texts 'u9-restored'
Check-Has 'u9.restore.count_text' $t '共 9 张图片 · 网格拼接 3×2 · 容量 5 · 合并 1 处 · 未放入 4 张'
$exp = Expected-Grid (@(200) * 9) (@(120) * 9) 3 2 @(@(0, 0, 0, 1)) 20 5 16
$s = Save-App $script:gridDir
Check 'u9.restore.save.ok' ($null -ne $s.file) '重启后保存成功' $s.output
if ($s.file) {
    Check-GridSize 'u9.restore.save_size' $s.file $exp
    Check-GridCenters 'u9.restore.save' $s.file $exp @($C[0], $C[1], $C[2], $C[3], $C[4]) 1
}
Shot 'v11-06-grid-memory-restored.png' | Out-Null

# =====================================================================
# U10 网格辅助线仅预览、不写入输出（A18）
# =====================================================================
Log ''
Log '----- U10 网格辅助线仅预览 -----'
$t = Get-Texts 'u10-guides-off'
Check 'u10.guides_off_state' ((Read-Ini $setMem).ShowGridGuides -eq '0') '记忆为关闭' (Read-Ini $setMem).ShowGridGuides
$saveOff = Save-App $script:gridDir
$shotOff = Shot 'v11-07-guides-off.png'
Invoke-Qa @('clickname', $script:title, '显示网格辅助线') | Out-Null
$ok = Wait-Ini $setMem 'ShowGridGuides' '1' 3000
Check 'u10.guides_on.ini' $ok 'ShowGridGuides=1' (Read-Ini $setMem).ShowGridGuides
Start-Sleep -Milliseconds 600
$shotOn = Shot 'v11-08-guides-on.png'
$saveOn = Save-App $script:gridDir
if ($saveOff.file -and $saveOn.file) {
    $d = Img-Diff $saveOff.file $saveOn.file
    Check 'u10.guides_not_in_output' ($d.ok -and $d.same) '辅助线开/关保存输出像素一致（不写入输出）' ($d | ConvertTo-Json -Compress)
}
App-Kill

# =====================================================================
# U11 旧配置 / 损坏配置兼容（A14）
# =====================================================================
Log ''
Log '----- U11 旧配置 / 损坏配置 -----'
$setOld = Write-RawSettings 'oldv10' @'
[collage]
Orientation=Horizontal
Gap=12
Margin=16
BackgroundArgb=#00000000
AutoBaseSize=1
BaseSize=0
Format=Png
JpegQuality=92
[window]
Width=1180
Height=780
X=-2147483648
Y=-2147483648
Maximized=0
[general]
AutoOpenAfterSave=0
'@
Start-App $g9 $setOld
$t = Get-Texts 'u11-old'
Check-Has 'u11.old.default_linear' $t '共 9 张图片 · 横向拼接'
$sw = Invoke-Qa @('segwait', $script:title, '拼接方式', '2', '网格拼接', '4000', '3')
Check 'u11.old.switch_grid' ($sw -match 'SEGWAIT_OK') '旧配置下切换到网格' $sw
Start-Sleep -Milliseconds 500
$t = Get-Texts 'u11-old-grid'
Check-Has 'u11.old.grid_defaults' $t '网格拼接 3×3 · 容量 9 · 合并 0 处'
App-Kill

$setBad = Write-RawSettings 'corrupt' "this is not an ini file`r`n[collage`r`nGap=abc`r`nGridRows=-99`r`n`r`n@@@"
Start-App $g9 $setBad
$t = Get-Texts 'u11-bad'
Check-Has 'u11.bad.default_linear' $t '共 9 张图片 · 横向拼接'
$sw = Invoke-Qa @('segwait', $script:title, '拼接方式', '2', '网格拼接', '4000', '3')
Check 'u11.bad.switch_grid' ($sw -match 'SEGWAIT_OK') '损坏配置下切换到网格仍正常' $sw
Start-Sleep -Milliseconds 500
$t = Get-Texts 'u11-bad-grid'
Check-Has 'u11.bad.grid_defaults' $t '网格拼接 3×3 · 容量 9 · 合并 0 处'
App-Kill

# =====================================================================
# U12 边界：1×1 网格 / 20×20 网格（A2）
# =====================================================================
Log ''
Log '----- U12 1×1 / 20×20 边界 -----'
$set1 = Seed-Settings 'g1x1' @{ Orientation = 'Grid'; GridRows = 1; GridColumns = 1; RowGap = 0; ColumnGap = 0; Margin = 0; Gap = 0 }
Start-App $g3 $set1
$t = Get-Texts 'u12-1x1'
Check-Has 'u12.1x1.count' $t '共 3 张图片 · 网格拼接 1×1 · 容量 1 · 合并 0 处 · 未放入 2 张'
$exp = Expected-Grid (@(200) * 3) (@(120) * 3) 1 1 $null 0 0 0
$s = Save-App $script:gridDir
if ($s.file) {
    Check-GridSize 'u12.1x1.save_size' $s.file $exp
    $p = Img-Pixel $s.file 100 60
    Check 'u12.1x1.first_image' (PxIs $p 255 0 0 255 1) '只输出第 1 张（红）' ($p.rgba -join ',')
}
App-Kill

$set20 = Seed-Settings 'g20x20' @{ Orientation = 'Grid'; GridRows = 20; GridColumns = 20; RowGap = 12; ColumnGap = 12; Margin = 16; Gap = 12 }
Start-App $s10 $set20
$t = Get-Texts 'u12-20x20'
Check-Has 'u12.20x20.count' $t '网格拼接 20×20 · 容量 400'
$exp = Expected-Grid (@(10) * 3) (@(10) * 3) 20 20 $null 12 12 16
$s = Save-App $script:gridDir
Check 'u12.20x20.save.ok' ($null -ne $s.file) '20×20 保存成功' $s.output
if ($s.file) {
    Check-GridSize 'u12.20x20.save_size' $s.file $exp
    Check-GridCenters 'u12.20x20' $s.file $exp @($C[0], $C[1], $C[2]) 1
    $p = Img-Pixel $s.file (16 + 3 * 22 + 5) (16 + 3 * 22 + 5)
    Check 'u12.20x20.empty_slot_alpha0' (Alpha-Is $p 0 1) '第 4 个区域为空（透明）' ($p.rgba -join ',')
}
Shot 'v11-09-grid-20x20.png' | Out-Null
App-Kill

# =====================================================================
# U13 单实例追加图片（A15）：网格参数保持、追加后未放入提示更新
# =====================================================================
Log ''
Log '----- U13 单实例追加（A15） -----'
$setSingle = Seed-Settings 'single' @{ Orientation = 'Grid'; GridRows = 3; GridColumns = 3 }
Start-App $g9 $setSingle
$t = Get-Texts 'u13-before'
Check-Has 'u13.before.count' $t '共 9 张图片 · 网格拼接 3×3 · 容量 9 · 合并 0 处'
$extra = Join-Path $script:gridDir 'g10.png'
$p2 = Start-Process -FilePath $script:exe -ArgumentList @($extra) -PassThru
$exited = $false
for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 250; if ($p2.HasExited) { $exited = $true; break } }
$p2code = -1
if ($exited) { try { $p2code = $p2.ExitCode } catch { $p2code = -2 } }
Check 'u13.second_instance.exit' ($exited -and $p2code -eq 0) '第二实例转发后退出码 0' ("exited=" + $exited + " code=" + $p2code)
$t = $null
for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Milliseconds 400
    $t = Get-Texts 'u13-after'
    if (Has-Text $t '共 10 张图片') { break }
}
Check-Has 'u13.after.count' $t '共 10 张图片 · 网格拼接 3×3 · 容量 9 · 合并 0 处 · 未放入 1 张'
Shot 'v11-10-single-instance-append.png' | Out-Null
App-Kill

# =====================================================================
# U14 横向/纵向 v1.0 零回归 + UI 保存 vs CLI 像素一致（A1）
# =====================================================================
Log ''
Log '----- U14 横向/纵向零回归（A1） -----'
$setLin = Seed-Settings 'linear' @{ Orientation = 'Horizontal'; Gap = 12; Margin = 16 }
$linThree = @()
for ($i = 0; $i -lt 3; $i++) { $linThree += (Join-Path $script:gridDir ("lin-" + $i + ".png")) }
Start-App $linThree $setLin
$saveH = Save-App $script:gridDir
Check 'u14.horizontal.save' ($null -ne $saveH.file) '横向保存成功' $saveH.output
if ($saveH.file) {
    $info = Img-Info $saveH.file
    Check 'u14.horizontal.size' ($info.size[0] -eq 3606 -and $info.size[1] -eq 932) '3606×932（与 v1.0 一致）' ("{0}x{1}" -f $info.size[0], $info.size[1])
    $cliOut = Join-Path $script:outDir 'ui-linear-h-cli.png'
    Invoke-CliCompose @('--layout', 'h', '--gap', '12', '--margin', '16', '--bg', '#00000000', '--base', 'auto', '--format', 'png') $linThree $cliOut | Out-Null
    $d = Img-Diff $saveH.file $cliOut
    Check 'u14.horizontal.ui_vs_cli' ($d.ok -and $d.same) '横向 UI 保存与 CLI 像素一致' ($d | ConvertTo-Json -Compress)
}
$sw = Invoke-Qa @('segwait', $script:title, '拼接方式', '1', '纵向拼接', '4000', '3')
Check 'u14.switch_vertical' ($sw -match 'SEGWAIT_OK') '切换纵向（三段 index=1 命中中间段）' $sw
Start-Sleep -Milliseconds 500
$saveV = Save-App $script:gridDir
if ($saveV.file) {
    $info = Img-Info $saveV.file
    Check 'u14.vertical.size' ($info.size[0] -eq 1232 -and $info.size[1] -eq 3331) '1232×3331（与 v1.0 一致）' ("{0}x{1}" -f $info.size[0], $info.size[1])
    $cliOut = Join-Path $script:outDir 'ui-linear-v-cli.png'
    Invoke-CliCompose @('--layout', 'v', '--gap', '12', '--margin', '16', '--bg', '#00000000', '--base', 'auto', '--format', 'png') $linThree $cliOut | Out-Null
    $d = Img-Diff $saveV.file $cliOut
    Check 'u14.vertical.ui_vs_cli' ($d.ok -and $d.same) '纵向 UI 保存与 CLI 像素一致' ($d | ConvertTo-Json -Compress)
}
App-Kill

# =====================================================================
# U15 最小窗口 / 200% DPI 网格模式无裁切 + 性能原始数据（A17/A18）
# =====================================================================
Log ''
Log '----- U15 最小窗口 / DPI / 性能 -----'
$setPerf = Seed-Settings 'perf' @{ Orientation = 'Grid'; GridRows = 3; GridColumns = 3 }
$env:LIGHTCOLLAGE_SETTINGS_DIR = $setPerf
App-Kill
$perfArgs = @('uistart', $script:exe, '已载入 9 张图片', '30000') + $g9
$u9 = Invoke-Qa $perfArgs
Check 'u15.uistart.load9' ($u9 -match 'found=True') '9 张图进入网格预览' $u9
$visibleMs = -1; $readyMs = -1
if ($u9 -match 'window_visible_ms=(\d+)') { $visibleMs = [int]$Matches[1] }
if ($u9 -match 'text_ready_ms=(\d+)') { $readyMs = [int]$Matches[1] }
Check 'u15.preview.ready_lt_4000' (($readyMs -ge 0) -and ($readyMs -lt 4000)) '9 图网格预览就绪 < 4000ms' ($readyMs.ToString() + 'ms')
$rect = Get-Rect
Check 'u15.dpi' ($rect.dpi -eq 192) 'dpi=192（200%）' ('dpi=' + $rect.dpi)
Check 'u15.default_window' (($rect.w -eq 2360) -and ($rect.h -eq 1560)) '默认窗口 1180×780 逻辑（2360×1560 物理）' ("{0}x{1}" -f $rect.w, $rect.h)
$setpos = Invoke-Qa @('setpos', $script:title, '200', '150', '2040', '1520')
Start-Sleep -Milliseconds 900
$rectMin = Get-Rect
Check 'u15.grid_minwindow.size' (($rectMin.w -eq 2040) -and ($rectMin.h -eq 1520)) '网格最小窗口 1020×760 逻辑（2040×1520 物理）' ("{0}x{1}" -f $rectMin.w, $rectMin.h)
$t = Get-Texts 'u15-minwindow'
$outOfWindow = 0; $checked = 0
$winLeft = $rectMin.x; $winTop = $rectMin.y; $winRight = $rectMin.x + $rectMin.w; $winBottom = $rectMin.y + $rectMin.h
foreach ($line in $t) {
    $m = [regex]::Match($line, 'rect=(-?\d+),(-?\d+),(-?\d+),(-?\d+)\toff=False')
    if (-not $m.Success) { continue }
    $x1 = [int]$m.Groups[1].Value; $y1 = [int]$m.Groups[2].Value
    $x2 = [int]$m.Groups[3].Value; $y2 = [int]$m.Groups[4].Value
    if ($x2 -le $x1 -or $y2 -le $y1) { continue }
    $checked++
    if ($x1 -lt ($winLeft - 2) -or $y1 -lt ($winTop - 2) -or $x2 -gt ($winRight + 2) -or $y2 -gt ($winBottom + 2)) { $outOfWindow++ }
}
Check 'u15.grid_minwindow.no_clipping' ($outOfWindow -eq 0) '网格最小窗口下 UI 元素不裁切' ("checked=$checked out=$outOfWindow")
Shot 'v11-11-grid-minwindow-200dpi.png' | Out-Null
$saveMin = Save-App $script:gridDir
if ($saveMin.file) {
    $info = Img-Info $saveMin.file
    Check 'u15.minwindow.save_size' ($info.size[0] -eq 656 -and $info.size[1] -eq 416) '网格最小窗口下保存仍 656×416' ("{0}x{1}" -f $info.size[0], $info.size[1])
    Check 'u15.save9.time_lt_5000' (($saveMin.ms -ge 0) -and ($saveMin.ms -lt 5000)) '9 图网格保存 < 5000ms' ($saveMin.ms.ToString() + 'ms')
}
App-Kill
Remove-Item Env:\LIGHTCOLLAGE_SETTINGS_DIR -ErrorAction SilentlyContinue

# =====================================================================
# 汇总
# =====================================================================
Log ''
Log ("===== 汇总：PASS=" + $script:pass + " FAIL=" + $script:fail + " TOTAL=" + ($script:pass + $script:fail) + " =====")
foreach ($r in $script:results) {
    if (-not $r.pass) { Log ("FAILED: " + $r.name + " | expected=" + $r.expected + " | actual=" + $r.actual) }
}
$summary = [pscustomobject]@{
    exe = $script:exe
    exe_size = (Get-Item $script:exe).Length
    exe_sha256 = (Get-FileHash $script:exe -Algorithm SHA256).Hash
    total = $script:pass + $script:fail
    pass = $script:pass
    fail = $script:fail
    results = $script:results
    perf = [pscustomobject]@{ window_visible_ms = $visibleMs; text_ready_ms = $readyMs; save9_ms = $(if ($saveMin) { $saveMin.ms } else { -1 }) }
}
$summary | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $script:logs 'ui-grid-results.json') -Encoding UTF8
if ($script:fail -gt 0) { exit 1 } else { exit 0 }
