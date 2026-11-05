# 轻拼 LightCollage v1.0.0 独立 UI 验收脚本（QA 编写，不修改产品源码）
# 运行：powershell -NoProfile -ExecutionPolicy Bypass -File tests\qa\run_ui_tests.ps1
# 依赖：tests\qa\bin\QaTool.exe（本目录 QaTool.cs 编译）、Pillow python。
# 所有截图 / 日志写入 C:\Temp\qa-lightcollage\screens 与 docs\qa\logs。
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$script:root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:qa = 'C:\Temp\qa-lightcollage'
$script:qaTool = Join-Path $PSScriptRoot 'bin\QaTool.exe'
$script:exe = (Resolve-Path (Join-Path $script:root 'dist\轻拼\LightCollage.exe')).Path
# Python 解释器探测（生成/校验图片用，需 Python 3 + Pillow）：
# 优先环境变量 QA_PYTHON，其次 PATH 中的 python；均未找到时给出中文提示并退出。
$script:py = $env:QA_PYTHON
if ($script:py -and -not (Test-Path -LiteralPath $script:py)) {
    Write-Warning ('环境变量 QA_PYTHON 指向的文件不存在：' + $script:py + '，改为从 PATH 探测 python。')
    $script:py = $null
}
if (-not $script:py) {
    $pythonCmd = Get-Command python -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($pythonCmd) { $script:py = $pythonCmd.Source }
}
if (-not $script:py) {
    Write-Error '未找到 Python 解释器：请设置环境变量 QA_PYTHON 指向 python.exe，或将 python 加入 PATH（需安装 Pillow）。'
    exit 1
}
$script:imgcheck = Join-Path $PSScriptRoot 'imgcheck.py'
$script:title = '轻拼 - 图片拼接'
$script:screens = Join-Path $script:qa 'screens'
$script:logs = Join-Path $script:root 'docs\qa\logs'
$script:ui = Join-Path $script:qa 'ui'
New-Item -ItemType Directory -Force -Path $script:screens, $script:logs | Out-Null

$script:pass = 0
$script:fail = 0
$script:results = New-Object System.Collections.ArrayList
$script:log = Join-Path $script:logs 'ui-tests.log'
if (Test-Path $script:log) { Remove-Item $script:log -Force }
function Log([string]$line) {
    Write-Host $line
    Add-Content -Path $script:log -Value $line -Encoding UTF8
}
function Check([string]$name, [bool]$cond, $expected, $actual) {
    if ($cond) { $script:pass++ } else { $script:fail++ }
    $tag = 'PASS'
    if (-not $cond) { $tag = 'FAIL' }
    Log ("[$tag] $name | expected=$expected | actual=$actual")
    [void]$script:results.Add([pscustomobject]@{ name = $name; pass = $cond; expected = [string]$expected; actual = [string]$actual })
}
function Invoke-Qa([string[]]$qaArgs) {
    $out = & $script:qaTool @qaArgs 2>&1 | Out-String
    return $out.Trim()
}
function App-Kill {
    Get-Process | Where-Object { $_.ProcessName -like 'LightCollage*' } | Stop-Process -Force
    Start-Sleep -Milliseconds 450
}
function Start-App([string[]]$files, [string]$settingsDir) {
    App-Kill
    if ($settingsDir) {
        $env:LIGHTCOLLAGE_SETTINGS_DIR = $settingsDir
        if (-not (Test-Path $settingsDir)) { New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null }
    } else {
        Remove-Item Env:\LIGHTCOLLAGE_SETTINGS_DIR
    }
    $args = @()
    foreach ($f in $files) { $args += $f }
    $p = Start-Process -FilePath $script:exe -ArgumentList $args -PassThru
    Invoke-Qa @('waitwin', $script:title, '15000') | Out-Null
    Start-Sleep -Milliseconds 1600
    return $p
}
function Get-AppRect {
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
function Get-AppTexts([string]$name) {
    $f = Join-Path $script:screens ("texts-" + $name + ".txt")
    Invoke-Qa @('texts', $script:title, $f) | Out-Null
    if (Test-Path $f) { return @(Get-Content $f -Encoding UTF8) }
    return @()
}
function Has-Text($texts, [string]$pattern) { return [bool]($texts | Select-String -Pattern $pattern -Quiet) }
function Shot-App([string]$name) {
    $f = Join-Path $script:screens $name
    Invoke-Qa @('shot', $script:title, $f) | Out-Null
    return $f
}
function Shot-Screen([string]$name) {
    $f = Join-Path $script:screens $name
    Invoke-Qa @('screen', $f) | Out-Null
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
function PxIs($pixel, [int]$r, [int]$g, [int]$b, [int]$a, [int]$tol) {
    if (-not $pixel.ok) { return $false }
    $v = $pixel.rgba
    return ([math]::Abs($v[0] - $r) -le $tol) -and ([math]::Abs($v[1] - $g) -le $tol) -and ([math]::Abs($v[2] - $b) -le $tol) -and ([math]::Abs($v[3] - $a) -le $tol)
}
function Alpha-Is($pixel, [int]$a, [int]$tol) {
    if (-not $pixel.ok) { return $false }
    return [math]::Abs($pixel.rgba[3] - $a) -le $tol
}
function Wait-Window([string]$winTitle, [int]$timeout) {
    return (Invoke-Qa @('waitwin', $winTitle, [string]$timeout))
}
function Click-Name([string]$winTitle, [string]$name) {
    return (Invoke-Qa @('clickname', $winTitle, $name))
}
function Click-NameContains([string]$winTitle, [string]$name) {
    return (Invoke-Qa @('clickname', $winTitle, $name, 'contains'))
}
function Dump-Dialog([string]$winTitle, [string]$file) {
    return (Invoke-Qa @('uia', $winTitle, (Join-Path $script:screens $file)))
}

Log ("===== 轻拼 UI 验收开始 " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + " =====")
Log ("EXE=" + $script:exe)
Log ("EXE_SIZE=" + (Get-Item $script:exe).Length)

# =====================================================================
# S1. 默认值 / 三图预览 / 默认保存 / 另存为 / 打开文件夹
# =====================================================================
Log ''
Log '----- S1 默认值 / 预览 / 默认保存 -----'
$settingsS1 = Join-Path $script:qa 'settings\ui-s1'
Remove-Item $settingsS1 -Recurse -Force
Get-ChildItem $script:ui -Filter '拼图_*' -ErrorAction SilentlyContinue | Remove-Item -Force
Start-App @((Join-Path $script:ui '1-photo1.png'), (Join-Path $script:ui '2-photo2.png'), (Join-Path $script:ui '3-photo3.png')) $settingsS1 | Out-Null
$rect = Get-AppRect
Check 'S1.window.physical' (($rect.w -eq 2360) -and ($rect.h -eq 1560)) '2360x1560 物理 (1180x780 逻辑@200%)' ("{0}x{1}" -f $rect.w, $rect.h)
Check 'S1.window.logical' (([math]::Round($rect.w / 2)) -eq 1180 -and ([math]::Round($rect.h / 2)) -eq 780) '1180x780 逻辑' ("{0}x{1}" -f ($rect.w / 2), ($rect.h / 2))
Check 'S1.dpi' ($rect.dpi -eq 192) 'dpi=192 (200%)' ("dpi=" + $rect.dpi)
$t = Get-AppTexts 'default'
Check 'S1.default.gap_margin_panes' ((Has-Text $t '图片间距') -and (Has-Text $t '外边距')) '存在间距/边距控件' 'ok'
Check 'S1.default.bg_hex' (Has-Text $t '#00000000') '#00000000（透明背景）' 'texts'
Check 'S1.default.alpha0' (Has-Text $t '透明度 0%') '透明度 0%' 'texts'
Check 'S1.default.png' (Has-Text $t 'PNG 完整保留每张图的透明通道') 'PNG 默认说明' 'texts'
Check 'S1.default.savebtn' (Has-Text $t '保存图片  Ctrl+S') '保存图片  Ctrl+S' 'texts'
Check 'S1.default.three' (Has-Text $t '共 3 张图片 · 横向拼接') '共 3 张图片 · 横向拼接' 'texts'
Check 'S1.default.loaded' (Has-Text $t '已载入 3 张图片') '已载入 3 张图片' 'texts'
Check 'S1.default.auto_base' (Has-Text $t '自动（中位数）') '自动基准' 'texts'
Check 'S1.default.format_vertical' (Has-Text $t '横向排列') '横向排列' 'texts'
$shotDefault = Shot-App 'ui-S1-default.png'
Check 'S1.preview.screenshot' (Test-Path $shotDefault) '截图存在' $shotDefault

# 默认保存（Ctrl+S，默认路径=第一张图目录）
$save1 = Save-App $script:ui
Check 'S1.save.ok' ($null -ne $save1.file) '生成新 PNG' $save1.output
Check 'S1.save.time_lt_3000' (($save1.ms -ge 0) -and ($save1.ms -lt 3000)) '保存 3 图 PNG < 3000ms' ($save1.ms.ToString() + 'ms')
if ($save1.file) {
    $fn = [System.IO.Path]::GetFileName($save1.file)
    Check 'S1.save.filename_pattern' ($fn -match '^拼图_\d{8}_\d{6}(_\d+)?\.png$') '拼图_yyyyMMdd_HHmmss.png' $fn
    $dir = [System.IO.Path]::GetDirectoryName($save1.file)
    Check 'S1.save.default_dir' ($dir.TrimEnd('\') -eq $script:ui.TrimEnd('\')) '第一张图所在目录' $dir
    $info = Img-Info $save1.file
    Check 'S1.save.size' ($info.size[0] -eq 3606 -and $info.size[1] -eq 932) '3606x932' ("{0}x{1}" -f $info.size[0], $info.size[1])
    Check 'S1.save.mode' ($info.mode -eq 'RGBA') 'RGBA' $info.mode
    $p00 = Img-Pixel $save1.file 0 0
    Check 'S1.save.margin_alpha0' (Alpha-Is $p00 0 0) '角像素 alpha=0' (($p00.rgba -join ','))
    $gapx = 16 + 1350 + 6
    $pg = Img-Pixel $save1.file $gapx 466
    Check 'S1.save.gap_alpha0' (Alpha-Is $pg 0 0) '间隙 alpha=0' (($pg.rgba -join ','))
    $p1 = Img-Pixel $save1.file 691 466
    $p2 = Img-Pixel $save1.file 1662 466
    $p3 = Img-Pixel $save1.file 2774 466
    Check 'S1.save.3images_opaque' ((Alpha-Is $p1 255 0) -and (Alpha-Is $p2 255 0) -and (Alpha-Is $p3 255 0)) '3 张图中心 alpha=255' (($p1.rgba -join ','), ($p2.rgba -join ','), ($p3.rgba -join ','))
    # 与 CLI 同参数输出做像素级一致性对比（UI 保存 vs 引擎合成）
    $cliRef = Join-Path $script:qa 'out\h_transparent.png'
    if (Test-Path $cliRef) {
        $d = Img-Diff $save1.file $cliRef
        Check 'S1.save.pixel_identical_to_cli' ($d.ok -and $d.same) '与 CLI --compose 同参数输出像素完全一致' ($d | ConvertTo-Json -Compress)
    }
    $t = Get-AppTexts 'after_save1'
    Check 'S1.save.status_text' (Has-Text $t '已保存：') '状态栏显示已保存路径' 'texts'
    Check 'S1.save.open_folder_button' (Has-Text $t '打开文件夹') '出现「打开文件夹」按钮' 'texts'
}

# 另存为（Ctrl+Shift+S）
$customSave = Join-Path $script:ui 'qa-custom-save.png'
Remove-Item $customSave -Force
Invoke-Qa @('bring', $script:title) | Out-Null
Invoke-Qa @('keys', '^+s') | Out-Null
Start-Sleep -Milliseconds 900
$dialogSave = Wait-Window '另存拼图' 8000
Check 'S1.saveas.dialog' ($dialogSave -match 'FOUND') '另存为对话框出现' $dialogSave
if ($dialogSave -match 'FOUND') {
    Shot-App 'ui-S1-saveas-dialog.png' | Out-Null
    Dump-Dialog '另存拼图' 'ui-S1-saveas-dialog-uia.txt' | Out-Null
    Invoke-Qa @('settext', '另存拼图', '文件名(N):', $customSave) | Out-Null
    Invoke-Qa @('clickname', '另存拼图', '保存', 'contains') | Out-Null
    Start-Sleep -Milliseconds 1500
    $exists = Test-Path $customSave
    Check 'S1.saveas.saved' $exists '自定义路径文件存在' $customSave
    if ($exists) {
        $info = Img-Info $customSave
        Check 'S1.saveas.valid' ($info.ok -and $info.size[0] -eq 3606) '另存文件可读且尺寸正确' ("{0}x{1}" -f $info.size[0], $info.size[1])
    }
}

# 打开文件夹按钮
$textsBefore = Get-AppTexts 'before_openfolder'
if (Has-Text $textsBefore '打开文件夹') {
    $winsBefore = (Invoke-Qa @('wins'))
    Invoke-Qa @('clickname', $script:title, '打开文件夹') | Out-Null
    Start-Sleep -Milliseconds 1500
    $winsAfter = (Invoke-Qa @('wins'))
    $cntBefore = ([regex]::Matches($winsBefore, 'CabinetWClass')).Count
    $cntAfter = ([regex]::Matches($winsAfter, 'CabinetWClass')).Count
    Check 'S1.openfolder.launches_explorer' ($cntAfter -gt $cntBefore) '打开文件夹后资源管理器窗口增加' ($cntBefore.ToString() + ' -> ' + $cntAfter.ToString())
    # 关闭新开的资源管理器窗口（标题含 ui）
    $m = [regex]::Match($winsAfter, 'hwnd=0x([0-9A-F]+) pid=\d+ class=CabinetWClass rect=[^\r\n]*title=ui - 文件资源管理器')
    if ($m.Success) {
        Invoke-Qa @('close', 'ui - 文件资源管理器') | Out-Null
    }
} else {
    Check 'S1.openfolder.button' $false '保存后出现「打开文件夹」' '未找到按钮'
}

# =====================================================================
# S2. 设置交互：横向/纵向、间距/边距、背景/透明度/自定义、格式、基准
# =====================================================================
Log ''
Log '----- S2 设置交互 -----'
$sw = Invoke-Qa @('segwait', $script:title, '拼接方式', '1', '纵向拼接', '3000')
Check 'S2.orientation.vertical_switch' ($sw -match 'SEGWAIT_OK') '切换到纵向且预览刷新' $sw
$refreshMs = -1
if ($sw -match 'after (\d+)ms') { $refreshMs = [int]$Matches[1] }
Check 'S2.orientation.refresh_lt_2000' (($refreshMs -ge 0) -and ($refreshMs -lt 2000)) '参数刷新（含 90ms 防抖）< 2000ms' ($refreshMs.ToString() + 'ms')
$t = Get-AppTexts 'vertical'
Check 'S2.orientation.label' (Has-Text $t '共 3 张图片 · 纵向拼接') '内容标签变纵向' 'texts'
Shot-App 'ui-S2-vertical.png' | Out-Null
$saveV = Save-App $script:ui
Check 'S2.orientation.save_size' ($null -ne $saveV.file) '纵向保存成功' $saveV.output
if ($saveV.file) {
    $info = Img-Info $saveV.file
    Check 'S2.orientation.size' ($info.size[0] -eq 1232 -and $info.size[1] -eq 3331) '1232x3331' ("{0}x{1}" -f $info.size[0], $info.size[1])
    $p = Img-Pixel $saveV.file 0 0
    Check 'S2.orientation.transparent' (Alpha-Is $p 0 0) '纵向透明背景角 alpha=0' (($p.rgba -join ','))
}
Invoke-Qa @('segwait', $script:title, '拼接方式', '0', '横向拼接', '3000') | Out-Null

# 间距 +3
Invoke-Qa @('step', $script:title, '图片间距', '+', '3') | Out-Null
Start-Sleep -Milliseconds 500
$saveG = Save-App $script:ui
if ($saveG.file) {
    $info = Img-Info $saveG.file
    Check 'S2.gap_plus3.size' ($info.size[0] -eq 3612 -and $info.size[1] -eq 932) 'gap 15 → 3612x932' ("{0}x{1}" -f $info.size[0], $info.size[1])
}
Invoke-Qa @('step', $script:title, '图片间距', '-', '3') | Out-Null
# 边距 +10
Invoke-Qa @('step', $script:title, '外边距', '+', '10') | Out-Null
Start-Sleep -Milliseconds 500
$saveM = Save-App $script:ui
if ($saveM.file) {
    $info = Img-Info $saveM.file
    Check 'S2.margin_plus10.size' ($info.size[0] -eq 3626 -and $info.size[1] -eq 952) 'margin 26 → 3626x952' ("{0}x{1}" -f $info.size[0], $info.size[1])
}
Invoke-Qa @('step', $script:title, '外边距', '-', '10') | Out-Null

# 背景白色
Invoke-Qa @('clickname', $script:title, '背景白色') | Out-Null
Start-Sleep -Milliseconds 500
$t = Get-AppTexts 'white'
Check 'S2.bg.white_hex' (Has-Text $t '#FFF5F7FA') '白色预设 #FFF5F7FA' 'texts'
Check 'S2.bg.white_alpha' (Has-Text $t '透明度 100%') '透明度 100%' 'texts'
$saveW = Save-App $script:ui
if ($saveW.file) {
    $p = Img-Pixel $saveW.file 0 0
    Check 'S2.bg.white_pixel' (PxIs $p 245 247 250 255 2) '角像素 (245,247,250,255)' (($p.rgba -join ','))
}
# 背景黑色
Invoke-Qa @('clickname', $script:title, '背景黑色') | Out-Null
Start-Sleep -Milliseconds 400
$t = Get-AppTexts 'black'
Check 'S2.bg.black_hex' (Has-Text $t '#FF101216') '黑色预设 #FF101216' 'texts'
$saveB = Save-App $script:ui
if ($saveB.file) {
    $p = Img-Pixel $saveB.file 0 0
    Check 'S2.bg.black_pixel' (PxIs $p 16 18 22 255 2) '角像素 (16,18,22,255)' (($p.rgba -join ','))
}
# 透明度滑块 50%
Invoke-Qa @('slider', $script:title, '背景透明度', '50') | Out-Null
Start-Sleep -Milliseconds 400
$t = Get-AppTexts 'alpha50'
Check 'S2.alpha.label50' (Has-Text $t '透明度 50%') '透明度 50%' 'texts'
Check 'S2.alpha.hex80' (Has-Text $t '#80101216') '#80101216' 'texts'
$saveA = Save-App $script:ui
if ($saveA.file) {
    $p = Img-Pixel $saveA.file 0 0
    Check 'S2.alpha.pixel128' (PxIs $p 16 18 22 128 2) '角像素 (16,18,22,128)' (($p.rgba -join ','))
}
# 自定义颜色
Invoke-Qa @('clickname', $script:title, '背景自定义') | Out-Null
$dlg = Wait-Window '颜色' 8000
Check 'S2.custom.dialog' ($dlg -match 'FOUND') '自定义颜色对话框出现' $dlg
if ($dlg -match 'FOUND') {
    Shot-Screen 'ui-S2-color-dialog.png' | Out-Null
    Dump-Dialog '颜色' 'ui-S2-color-dialog-uia.txt' | Out-Null
    Invoke-Qa @('settext', '颜色', '红(R):', '10') | Out-Null
    Invoke-Qa @('settext', '颜色', '绿(G):', '200') | Out-Null
    Invoke-Qa @('settext', '颜色', '蓝(U):', '30') | Out-Null
    Start-Sleep -Milliseconds 250
    Invoke-Qa @('clickname', '颜色', '确定') | Out-Null
    Start-Sleep -Milliseconds 600
    $t = Get-AppTexts 'custom'
    Check 'S2.custom.hex' (Has-Text $t '#FF0AC81E') '#FF0AC81E' 'texts'
    $saveC = Save-App $script:ui
    if ($saveC.file) {
        $p = Img-Pixel $saveC.file 0 0
        Check 'S2.custom.pixel' (PxIs $p 10 200 30 255 2) '角像素 (10,200,30,255)' (($p.rgba -join ','))
    }
    Shot-App 'ui-S2-custom-color.png' | Out-Null
}
# 恢复透明背景
Invoke-Qa @('clickname', $script:title, '背景透明') | Out-Null
Start-Sleep -Milliseconds 300
$t = Get-AppTexts 'transparent_again'
Check 'S2.bg.transparent_restore' (Has-Text $t '#00000000') '#00000000' 'texts'

# 输出格式 JPG
$swj = Invoke-Qa @('segwait', $script:title, '输出格式选择', '1', 'JPG 不支持透明，透明区域将填白', '3000')
Check 'S2.format.jpg_note' ($swj -match 'SEGWAIT_OK') 'JPG 提示文案切换' $swj
$t = Get-AppTexts 'jpg'
Check 'S2.format.jpg_savebtn' (Has-Text $t '保存 JPG  Ctrl+S') '保存 JPG  Ctrl+S' 'texts'
Start-Sleep -Milliseconds 400
$saveJ = Save-App $script:ui
Check 'S2.format.jpg_savefile' ($null -ne $saveJ.file -and $saveJ.file -match '\.jpg$') '保存为 .jpg' $saveJ.output
if ($saveJ.file) {
    $info = Img-Info $saveJ.file
    Check 'S2.format.jpg_no_alpha' ($info.mode -eq 'RGB' -or $info.mode -eq 'L') 'JPG 模式无 Alpha' $info.mode
    Check 'S2.format.jpg_size' ($info.size[0] -eq 3606 -and $info.size[1] -eq 932) '3606x932' ("{0}x{1}" -f $info.size[0], $info.size[1])
    $p = Img-Pixel $saveJ.file 0 0
    Check 'S2.format.jpg_white_bg' (PxIs $p 255 255 255 255 3) '透明背景压白 (255,255,255)' (($p.rgba -join ','))
}
Invoke-Qa @('segwait', $script:title, '输出格式选择', '0', 'PNG 完整保留每张图的透明通道', '3000') | Out-Null

# 基准尺寸：取消自动 + 手动 910
Invoke-Qa @('clickname', $script:title, '自动（中位数）') | Out-Null
Start-Sleep -Milliseconds 400
Invoke-Qa @('step', $script:title, '基准尺寸', '+', '1') | Out-Null
Start-Sleep -Milliseconds 500
$saveBase = Save-App $script:ui
if ($saveBase.file) {
    $info = Img-Info $saveBase.file
    Check 'S2.base.manual910.size' ($info.size[0] -eq 3646 -and $info.size[1] -eq 942) '手动基准 910 → 3646x942' ("{0}x{1}" -f $info.size[0], $info.size[1])
}
Invoke-Qa @('clickname', $script:title, '自动（中位数）') | Out-Null
Start-Sleep -Milliseconds 500
$saveAuto = Save-App $script:ui
if ($saveAuto.file) {
    $info = Img-Info $saveAuto.file
    Check 'S2.base.auto.size' ($info.size[0] -eq 3606 -and $info.size[1] -eq 932) '恢复自动 → 3606x932' ("{0}x{1}" -f $info.size[0], $info.size[1])
}

# =====================================================================
# S3. 设置记忆 + 窗口大小/位置记忆
# =====================================================================
Log ''
Log '----- S3 设置记忆 / 窗口记忆 -----'
# 制造可区分的设置：纵向 + 黑底 50% + JPG + gap+2
Invoke-Qa @('segwait', $script:title, '拼接方式', '1', '纵向拼接', '3000') | Out-Null
Invoke-Qa @('clickname', $script:title, '背景黑色') | Out-Null
Start-Sleep -Milliseconds 250
Invoke-Qa @('slider', $script:title, '背景透明度', '50') | Out-Null
Start-Sleep -Milliseconds 250
Invoke-Qa @('segwait', $script:title, '输出格式选择', '1', 'JPG 不支持透明', '3000') | Out-Null
Invoke-Qa @('step', $script:title, '图片间距', '+', '2') | Out-Null
Start-Sleep -Milliseconds 800
$saveMem = Save-App $script:ui
Start-Sleep -Milliseconds 500
$ini = Join-Path $settingsS1 'settings.ini'
Check 'S3.settings.file_exists' (Test-Path $ini) '设置文件已写入' $ini
if (Test-Path $ini) {
    $iniText = Get-Content $ini -Encoding UTF8
    Check 'S3.settings.orientation' (Has-Text $iniText 'Orientation=Vertical') 'Orientation=Vertical' 'ini'
    Check 'S3.settings.gap' (Has-Text $iniText 'Gap=14') 'Gap=14' 'ini'
    Check 'S3.settings.bg' (Has-Text $iniText 'BackgroundArgb=#80101216') 'BackgroundArgb=#80101216' 'ini'
    Check 'S3.settings.format' (Has-Text $iniText 'Format=Jpeg') 'Format=Jpeg' 'ini'
}
$infoMem = $null
if ($saveMem.file) { $infoMem = Img-Info $saveMem.file }
Check 'S3.settings.save_size' ($null -ne $infoMem -and $infoMem.size[0] -eq 1232 -and $infoMem.size[1] -eq 3335) '纵向 gap14 → 1232x3335' ("{0}x{1}" -f $infoMem.size[0], $infoMem.size[1])
Shot-App 'ui-S3-memory-before-close.png' | Out-Null
# 改变窗口大小/位置后正常关闭（WM_CLOSE 触发 SaveWindowSettings）
Invoke-Qa @('setpos', $script:title, '100', '80', '2400', '1500') | Out-Null
Start-Sleep -Milliseconds 600
Invoke-Qa @('close', $script:title) | Out-Null
Start-Sleep -Milliseconds 1500
$still = Get-Process | Where-Object { $_.ProcessName -like 'LightCollage*' }
Check 'S3.close.graceful' ($null -eq $still -or $still.Count -eq 0) '正常关闭（无未保存提示）' ('process count=' + @($still).Count)
if (Test-Path $ini) {
    $iniText = Get-Content $ini -Encoding UTF8
    Check 'S3.window.size_saved' (Has-Text $iniText 'Width=1200') 'Width=1200（逻辑）' 'ini'
    Check 'S3.window.height_saved' (Has-Text $iniText 'Height=750') 'Height=750（逻辑）' 'ini'
}
# 重新启动验证记忆
$p2 = Start-Process -FilePath $script:exe -ArgumentList @((Join-Path $script:ui '1-photo1.png'), (Join-Path $script:ui '2-photo2.png'), (Join-Path $script:ui '3-photo3.png')) -PassThru
$env:LIGHTCOLLAGE_SETTINGS_DIR = $settingsS1
Invoke-Qa @('waitwin', $script:title, '15000') | Out-Null
Start-Sleep -Milliseconds 1800
$rect2 = Get-AppRect
Check 'S3.window.restored_size' (($rect2.w -eq 2400) -and ($rect2.h -eq 1500)) '重启后窗口 2400x1500 物理' ("{0}x{1}" -f $rect2.w, $rect2.h)
Check 'S3.window.restored_pos' (($rect2.x -eq 100) -and ($rect2.y -eq 80)) '重启后窗口位置 100,80' ("{0},{1}" -f $rect2.x, $rect2.y)
$t = Get-AppTexts 'memory_restored'
Check 'S3.memory.orientation' (Has-Text $t '共 3 张图片 · 纵向拼接') '纵向排列已记忆' 'texts'
Check 'S3.memory.bg' (Has-Text $t '#80101216') '背景 #80101216 已记忆' 'texts'
Check 'S3.memory.alpha' (Has-Text $t '透明度 50%') '透明度 50% 已记忆' 'texts'
Check 'S3.memory.jpg' (Has-Text $t 'JPG 不支持透明，透明区域将填白') 'JPG 已记忆' 'texts'
$saveMem2 = Save-App $script:ui
if ($saveMem2.file) {
    $info = Img-Info $saveMem2.file
    Check 'S3.memory.effective_size' ($info.size[0] -eq 1232 -and $info.size[1] -eq 3335) '记忆设置生效：1232x3335' ("{0}x{1}" -f $info.size[0], $info.size[1])
}
Shot-App 'ui-S3-memory-restored.png' | Out-Null
App-Kill

# =====================================================================
# S4. 图片编辑：添加 / 删除 / 缩略图
# =====================================================================
Log ''
Log '----- S4 图片编辑 -----'
$settingsS4 = Join-Path $script:qa 'settings\ui-s4'
Remove-Item $settingsS4 -Recurse -Force
Start-App @((Join-Path $script:ui '1-photo1.png'), (Join-Path $script:ui '2-photo2.png')) $settingsS4 | Out-Null
$t = Get-AppTexts 'edit_start'
Check 'S4.start.two' (Has-Text $t '共 2 张图片') '初始 2 张' 'texts'
# Ctrl+O 添加 4-sticker.png
Invoke-Qa @('bring', $script:title) | Out-Null
Invoke-Qa @('keys', '^o') | Out-Null
$dlgOpen = Wait-Window '添加图片' 8000
Check 'S4.add.dialog' ($dlgOpen -match 'FOUND') 'Ctrl+O 打开添加对话框' $dlgOpen
if ($dlgOpen -match 'FOUND') {
    Start-Sleep -Milliseconds 700
    Invoke-Qa @('settext', '添加图片', '文件名(N):', (Join-Path $script:ui '4-sticker.png')) | Out-Null
    Invoke-Qa @('clickname', '添加图片', '打开(O)') | Out-Null
    Start-Sleep -Milliseconds 1500
    $t = Get-AppTexts 'edit_added'
    Check 'S4.add.count3' (Has-Text $t '共 3 张图片') '添加后 3 张' 'texts'
}
# 点击第一张缩略图后 Delete
$o = Invoke-Qa @('clickthumb', $script:title, '0')
Invoke-Qa @('keys', '{DELETE}') | Out-Null
Start-Sleep -Milliseconds 900
$t = Get-AppTexts 'edit_deleted'
Check 'S4.delete.count2' (Has-Text $t '共 2 张图片') 'Delete 删除后 2 张' ('clickthumb=' + $o)
Shot-App 'ui-S4-after-delete.png' | Out-Null

# =====================================================================
# S5. 排序 / 拖动排序 / 清空（使用纯色小图，像素级验证顺序）
# =====================================================================
Log ''
Log '----- S5 排序 / 拖动排序 / 清空 -----'
$orderDir = Join-Path $script:qa 'ui-order'
New-Item -ItemType Directory -Force -Path $orderDir | Out-Null
& $script:py -c @"
from PIL import Image
import os
d = r'$orderDir'
for name, color in [('a-blue.png',(0,0,255,255)),('m-green.png',(0,255,0,255)),('z-red.png',(255,0,0,255))]:
    Image.new('RGBA',(40,20),color).save(os.path.join(d,name))
print('order fixtures ok')
"@ | Out-Null
$settingsS5 = Join-Path $script:qa 'settings\ui-s5'
Remove-Item $settingsS5 -Recurse -Force
New-Item -ItemType Directory -Force -Path $settingsS5 | Out-Null
$iniSeed = @'
[collage]
Orientation=Horizontal
Gap=0
Margin=0
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
Set-Content -Path (Join-Path $settingsS5 'settings.ini') -Value $iniSeed -Encoding UTF8
Start-App @((Join-Path $orderDir 'z-red.png'), (Join-Path $orderDir 'a-blue.png'), (Join-Path $orderDir 'm-green.png')) $settingsS5 | Out-Null
$saveO = Save-App $orderDir
Check 'S5.order.initial_saved' ($null -ne $saveO.file) '初始顺序保存' $saveO.output
if ($saveO.file) {
    $info = Img-Info $saveO.file
    Check 'S5.order.initial_size' ($info.size[0] -eq 120 -and $info.size[1] -eq 20) '120x20' ("{0}x{1}" -f $info.size[0], $info.size[1])
    $px0 = Img-Pixel $saveO.file 20 10
    $px1 = Img-Pixel $saveO.file 60 10
    $px2 = Img-Pixel $saveO.file 100 10
    Check 'S5.order.initial' ((PxIs $px0 255 0 0 255 1) -and (PxIs $px1 0 0 255 255 1) -and (PxIs $px2 0 255 0 255 1)) '传入顺序 red,blue,green' (($px0.rgba -join ','), ($px1.rgba -join ','), ($px2.rgba -join ','))
}
# 按名称排序 → a-blue, m-green, z-red
Invoke-Qa @('clickname', $script:title, '按名称排序') | Out-Null
Start-Sleep -Milliseconds 900
$saveSort = Save-App $orderDir
if ($saveSort.file) {
    $px0 = Img-Pixel $saveSort.file 20 10
    $px1 = Img-Pixel $saveSort.file 60 10
    $px2 = Img-Pixel $saveSort.file 100 10
    Check 'S5.sort.by_name' ((PxIs $px0 0 0 255 255 1) -and (PxIs $px1 0 255 0 255 1) -and (PxIs $px2 255 0 0 255 1)) '名称排序 blue,green,red' (($px0.rgba -join ','), ($px1.rgba -join ','), ($px2.rgba -join ','))
}
# 拖动缩略图 1 → 3（blue 移到最后）
$dragOut = Invoke-Qa @('dragthumb', $script:title, '0', '3')
Start-Sleep -Milliseconds 800
$saveDrag = Save-App $orderDir
if ($saveDrag.file) {
    $px0 = Img-Pixel $saveDrag.file 20 10
    $px1 = Img-Pixel $saveDrag.file 60 10
    $px2 = Img-Pixel $saveDrag.file 100 10
    Check 'S5.drag.reorder' ((PxIs $px0 0 255 0 255 1) -and (PxIs $px1 255 0 0 255 1) -and (PxIs $px2 0 0 255 255 1)) '拖动后 green,red,blue' (($px0.rgba -join ','), ($px1.rgba -join ','), ($px2.rgba -join ','))
    Shot-App 'ui-S5-order.png' | Out-Null
}
Check 'S5.drag.output' ($dragOut -match 'dragthumb done') '拖动命令执行' $dragOut
# 清空（需要确认）
Invoke-Qa @('clickname', $script:title, '清空') | Out-Null
$dlgClear = Wait-Window '轻拼' 5000
Check 'S5.clear.confirm_dialog' ($dlgClear -match 'FOUND') '清空确认框出现' $dlgClear
if ($dlgClear -match 'FOUND') {
    Shot-Screen 'ui-S5-clear-confirm.png' | Out-Null
    $r = Click-NameContains '轻拼' '是'
    Start-Sleep -Milliseconds 800
    $t = Get-AppTexts 'cleared'
    Check 'S5.clear.empty_count' (Has-Text $t '共 0 张图片') '清空后 0 张' ($r + ' / texts')
    Shot-App 'ui-S5-empty.png' | Out-Null
}

# =====================================================================
# S6. 单图 / 透明源图
# =====================================================================
Log ''
Log '----- S6 单图 / 透明源图 -----'
$settingsS6 = Join-Path $script:qa 'settings\ui-s6'
Remove-Item $settingsS6 -Recurse -Force
Start-App @((Join-Path $script:ui '4-sticker.png')) $settingsS6 | Out-Null
$t = Get-AppTexts 'single'
Check 'S6.single.count' (Has-Text $t '共 1 张图片') '单图加载' 'texts'
$saveS = Save-App $script:ui
Check 'S6.single.save' ($null -ne $saveS.file) '单图保存' $saveS.output
if ($saveS.file) {
    $info = Img-Info $saveS.file
    Check 'S6.single.size' ($info.size[0] -eq 532 -and $info.size[1] -eq 532) '500x500 + 边距 16 → 532x532' ("{0}x{1}" -f $info.size[0], $info.size[1])
    $infoSrc = Img-Info (Join-Path $script:ui '4-sticker.png')
    Check 'S6.single.src_rgba' ($infoSrc.mode -eq 'RGBA') '源图 RGBA' $infoSrc.mode
    # 源图透明区域在输出中仍透明（取源图左上角对应输出 (16,16)）
    $alphaOut = Img-Alpha $saveS.file
    Check 'S6.single.alpha_extrema' ($alphaOut.min -eq 0 -and $alphaOut.max -eq 255) '输出含透明与不透明像素 (0..255)' ("min={0} max={1}" -f $alphaOut.min, $alphaOut.max)
}
App-Kill

# =====================================================================
# S7. 损坏图片容错
# =====================================================================
Log ''
Log '----- S7 损坏图片容错 -----'
$settingsS7 = Join-Path $script:qa 'settings\ui-s7'
Remove-Item $settingsS7 -Recurse -Force
$fixtureCorrupt = Join-Path $script:root 'tests\core\fixtures\corrupted.png'
Start-App @($fixtureCorrupt, (Join-Path $script:ui '1-photo1.png')) $settingsS7 | Out-Null
$dlgErr = Wait-Window '轻拼' 8000
Check 'S7.corrupt.warning_dialog' ($dlgErr -match 'FOUND') '损坏图片弹出可读警告' $dlgErr
if ($dlgErr -match 'FOUND') {
    Shot-Screen 'ui-S7-corrupt-dialog.png' | Out-Null
    Dump-Dialog '轻拼' 'ui-S7-corrupt-dialog-uia.txt' | Out-Null
    Invoke-Qa @('clickname', '轻拼', '确定') | Out-Null
    Start-Sleep -Milliseconds 1000
}
$t = Get-AppTexts 'after_corrupt'
Check 'S7.corrupt.valid_still_loaded' (Has-Text $t '共 1 张图片') '损坏图跳过，正常图仍为 1 张' 'texts'
$shot = Shot-App 'ui-S7-after-corrupt.png'
Check 'S7.screenshot' (Test-Path $shot) '截图保存' $shot

# =====================================================================
# S8. 超多图（60 张）加载与保存
# =====================================================================
Log ''
Log '----- S8 60 张图片 -----'
$manyDir = Join-Path $script:qa 'ui-60'
New-Item -ItemType Directory -Force -Path $manyDir | Out-Null
& $script:py -c @"
from PIL import Image
import os
d = r'$manyDir'
for i in range(60):
    c = ((i*37)%256, (i*91)%256, (i*53)%256, 255)
    Image.new('RGBA', (200, 100), c).save(os.path.join(d, 'many-%02d.png' % i))
print('60 fixtures ok')
"@ | Out-Null
$settingsS8 = Join-Path $script:qa 'settings\ui-s8'
Remove-Item $settingsS8 -Recurse -Force
$env:LIGHTCOLLAGE_SETTINGS_DIR = $settingsS8
$manyFiles = @(Get-ChildItem $manyDir -Filter '*.png' | Sort-Object Name | ForEach-Object { $_.FullName })
$manyArgs = @('uistart', $script:exe, '已载入 60 张图片', '30000') + $manyFiles
$ustart = Invoke-Qa $manyArgs
Check 'S8.load60.text' ($ustart -match 'found=True') '60 张全部载入' $ustart
Check 'S8.load60.window_visible' ($ustart -match 'window_visible_ms=(\d+)') '窗口可见时间有记录' $ustart
$t = Get-AppTexts 'many'
Check 'S8.load60.count' (Has-Text $t '共 60 张图片') '共 60 张' 'texts'
Shot-App 'ui-S8-60images.png' | Out-Null
$saveMany = Save-App $manyDir
Check 'S8.save60.ok' ($null -ne $saveMany.file) '60 张保存成功' $saveMany.output
if ($saveMany.file) {
    $info = Img-Info $saveMany.file
    # 60 张 200x100，base=100，gap12 margin16 → W=32+200*60+12*59=12740, H=132
    Check 'S8.save60.size' ($info.size[0] -eq 12740 -and $info.size[1] -eq 132) '12740x132' ("{0}x{1}" -f $info.size[0], $info.size[1])
    Check 'S8.save60.time' ($saveMany.ms -ge 0 -and $saveMany.ms -lt 10000) '保存 60 图 < 10000ms' ($saveMany.ms.ToString() + 'ms')
}
App-Kill

# =====================================================================
# S9. 最小窗口 + 200% DPI 布局检查
# =====================================================================
Log ''
Log '----- S9 最小窗口 / DPI 布局 -----'
$settingsS9 = Join-Path $script:qa 'settings\ui-s9'
Remove-Item $settingsS9 -Recurse -Force
Start-App @((Join-Path $script:ui '1-photo1.png'), (Join-Path $script:ui '2-photo2.png'), (Join-Path $script:ui '3-photo3.png')) $settingsS9 | Out-Null
Invoke-Qa @('setpos', $script:title, '200', '150', '2040', '1400') | Out-Null
Start-Sleep -Milliseconds 800
$rectMin = Get-AppRect
Check 'S9.minwindow.size' (($rectMin.w -eq 2040) -and ($rectMin.h -eq 1400)) '最小窗口 2040x1400 物理 (1020x700 逻辑)' ("{0}x{1}" -f $rectMin.w, $rectMin.h)
$t = Get-AppTexts 'minwindow'
Check 'S9.minwindow.still_3images' (Has-Text $t '共 3 张图片') '最小窗口下仍有 3 张图' 'texts'
# 检查所有 UIA 元素 rect 都在窗口范围内（允许 2px 误差）
$outOfWindow = 0
$checked = 0
$winLeft = $rectMin.x
$winTop = $rectMin.y
$winRight = $rectMin.x + $rectMin.w
$winBottom = $rectMin.y + $rectMin.h
foreach ($line in $t) {
    $m = [regex]::Match($line, 'rect=(-?\d+),(-?\d+),(-?\d+),(-?\d+)\toff=False')
    if (-not $m.Success) { continue }
    $x1 = [int]$m.Groups[1].Value; $y1 = [int]$m.Groups[2].Value
    $x2 = [int]$m.Groups[3].Value; $y2 = [int]$m.Groups[4].Value
    if ($x2 -le $x1 -or $y2 -le $y1) { continue }
    $checked++
    if ($x1 -lt ($winLeft - 2) -or $y1 -lt ($winTop - 2) -or $x2 -gt ($winRight + 2) -or $y2 -gt ($winBottom + 2)) { $outOfWindow++ }
}
Check 'S9.minwindow.no_clipping' ($outOfWindow -eq 0) 'UI 元素全部在窗口内（无裁切）' ("checked=$checked out=$outOfWindow")
Shot-App 'ui-S9-minwindow.png' | Out-Null
$saveMin = Save-App $script:ui
if ($saveMin.file) {
    $info = Img-Info $saveMin.file
    Check 'S9.minwindow.save_size' ($info.size[0] -eq 3606 -and $info.size[1] -eq 932) '最小窗口保存 3606x932' ("{0}x{1}" -f $info.size[0], $info.size[1])
}
App-Kill

# =====================================================================
# S10. 性能：冷启动（无图）/ 3 图进入预览
# =====================================================================
Log ''
Log '----- S10 性能 -----'
$perfStart = @()
for ($i = 1; $i -le 3; $i++) {
    App-Kill
    Remove-Item Env:\LIGHTCOLLAGE_SETTINGS_DIR
    $settingsPerf = Join-Path $script:qa 'settings\ui-perf'
    Remove-Item $settingsPerf -Recurse -Force
    $env:LIGHTCOLLAGE_SETTINGS_DIR = $settingsPerf
    $o = Invoke-Qa @('uistart', $script:exe, '共 0 张图片', '15000')
    $ms = -1
    if ($o -match 'window_visible_ms=(\d+)') { $ms = [int]$Matches[1] }
    $perfStart += $ms
    Log ("coldstart run$i : " + $o)
}
$validPerf = @($perfStart | Where-Object { $_ -ge 0 })
Check 'S10.coldstart.recorded' ($validPerf.Count -ge 3) '3 次冷启动记录' ($perfStart -join ',')
$minStart = ($validPerf | Measure-Object -Minimum).Minimum
Check 'S10.coldstart.min_lt_400' ($minStart -lt 400) '最快冷启动 < 400ms' ($minStart.ToString() + 'ms')
$median = ($validPerf | Sort-Object)[[int]($validPerf.Count / 2)]
Check 'S10.coldstart.median_lt_400' ($median -lt 400) '冷启动中位数 < 400ms' ($median.ToString() + 'ms')
App-Kill
# 3 图进入预览
$settingsPerf2 = Join-Path $script:qa 'settings\ui-perf2'
Remove-Item $settingsPerf2 -Recurse -Force
$env:LIGHTCOLLAGE_SETTINGS_DIR = $settingsPerf2
$o3 = Invoke-Qa @('uistart', $script:exe, '已载入 3 张图片', '15000', (Join-Path $script:ui '1-photo1.png'), (Join-Path $script:ui '2-photo2.png'), (Join-Path $script:ui '3-photo3.png'))
Check 'S10.three_images.text' ($o3 -match 'found=True') '3 张进入预览' $o3
$readyMs = -1
if ($o3 -match 'text_ready_ms=(\d+)') { $readyMs = [int]$Matches[1] }
Check 'S10.three_images.lt_3000' (($readyMs -ge 0) -and ($readyMs -lt 3000)) '3 张进入预览 < 3000ms' ($readyMs.ToString() + 'ms')
App-Kill
Remove-Item Env:\LIGHTCOLLAGE_SETTINGS_DIR

# =====================================================================
# 汇总
# =====================================================================
Log ''
Log ("===== 汇总：PASS=" + $script:pass + " FAIL=" + $script:fail + " TOTAL=" + ($script:pass + $script:fail) + " =====")
foreach ($r in $script:results) {
    if (-not $r.pass) { Log ("FAILED: " + $r.name + " | expected=" + $r.expected + " | actual=" + $r.actual) }
}
$summary = [pscustomobject]@{
    total = $script:pass + $script:fail
    pass = $script:pass
    fail = $script:fail
    results = $script:results
    perf = [pscustomobject]@{ coldstart_ms = $perfStart; three_images_ready_ms = $readyMs }
}
$summary | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $script:logs 'ui-results.json') -Encoding UTF8
if ($script:fail -gt 0) { exit 1 } else { exit 0 }
