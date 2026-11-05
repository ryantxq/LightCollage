# 轻拼 LightCollage 补充 UI 验证（修正前一轮测试脚本的预期错误 / 干净实例）
# 运行：powershell -NoProfile -ExecutionPolicy Bypass -File tests\qa\run_ui_supplement.ps1
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$script:root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:qa = 'C:\Temp\qa-lightcollage'
$script:qaTool = Join-Path $PSScriptRoot 'bin\QaTool.exe'
$script:exe = (Resolve-Path (Join-Path $script:root 'dist\轻拼\LightCollage.exe')).Path
# Python 解释器探测（像素校验用，需 Python 3 + Pillow）：
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
$script:title = '轻拼 - 图片拼接'
$script:screens = Join-Path $script:qa 'screens'
$script:logs = Join-Path $script:root 'docs\qa\logs'
$script:ui = Join-Path $script:qa 'ui'
$script:pass = 0; $script:fail = 0
$script:results = New-Object System.Collections.ArrayList
$script:log = Join-Path $script:logs 'ui-supplement.log'
if (Test-Path $script:log) { Remove-Item $script:log -Force }
function Log([string]$line) { Write-Host $line; Add-Content -Path $script:log -Value $line -Encoding UTF8 }
function Check([string]$name, [bool]$cond, $expected, $actual) {
    if ($cond) { $script:pass++ } else { $script:fail++ }
    $tag = 'PASS'; if (-not $cond) { $tag = 'FAIL' }
    Log ("[$tag] $name | expected=$expected | actual=$actual")
    [void]$script:results.Add([pscustomobject]@{ name = $name; pass = $cond; expected = [string]$expected; actual = [string]$actual })
}
function Invoke-Qa([string[]]$a) { return ((& $script:qaTool @a 2>&1 | Out-String).Trim()) }
function App-Kill { Get-Process | Where-Object { $_.ProcessName -like 'LightCollage*' } | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 450 }
function Start-Fresh([string[]]$files, [string]$tag) {
    App-Kill
    $set = Join-Path $script:qa ("settings\supp-" + $tag)
    Remove-Item $set -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $set | Out-Null
    $env:LIGHTCOLLAGE_SETTINGS_DIR = $set
    $fileArgs = @(); foreach ($f in $files) { $fileArgs += $f }
    Start-Process -FilePath $script:exe -ArgumentList $fileArgs | Out-Null
    Invoke-Qa @('waitwin', $script:title, '15000') | Out-Null
    Start-Sleep -Milliseconds 1800
    return $set
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
function Get-Texts([string]$name) {
    $f = Join-Path $script:screens ("supp-texts-" + $name + ".txt")
    Invoke-Qa @('texts', $script:title, $f) | Out-Null
    if (Test-Path $f) { return @(Get-Content $f -Encoding UTF8) }
    return @()
}
function Has($texts, [string]$pat) { return [bool]($texts | Select-String -Pattern $pat -SimpleMatch -Quiet) }
function Shot([string]$name) { $f = Join-Path $script:screens $name; Invoke-Qa @('shot', $script:title, $f) | Out-Null; return $f }
function Save-App([string]$dir) {
    $o = Invoke-Qa @('ctrlsave', $script:title, $dir)
    $ms = -1; if ($o -match 'ms=(\d+)') { $ms = [int]$Matches[1] }
    $file = $null; if ($o -match 'file=(.+) size=(\d+)') { $file = $Matches[1].Trim() }
    return @{ output = $o; ms = $ms; file = $file }
}
function Img-Info([string]$f) { return (& $script:py (Join-Path $PSScriptRoot 'imgcheck.py') 'info' $f | Out-String | ConvertFrom-Json) }
function Img-Pixel([string]$f, [int]$x, [int]$y) { return (& $script:py (Join-Path $PSScriptRoot 'imgcheck.py') 'pixel' $f $x $y | Out-String | ConvertFrom-Json) }
function Expected-Size([string]$orient, [int]$gap, [int]$margin, [string]$base) {
    $sizes = @(@(1200, 800), @(800, 1200), @(1600, 900))
    if ($orient -eq 'Vertical') {
        $ws = @($sizes | ForEach-Object { $_[0] } | Sort-Object)
        if ($base -eq 'auto') { $W = $ws[1] } else { $W = [int]$base }
        $hs = @(); foreach ($s in $sizes) { $hs += [int][math]::Round($s[1] * $W / [double]$s[0]) }
        return @(2 * $margin + $W, 2 * $margin + (($hs | Measure-Object -Sum).Sum) + $gap * 2)
    } else {
        $hs = @($sizes | ForEach-Object { $_[1] } | Sort-Object)
        if ($base -eq 'auto') { $H = $hs[1] } else { $H = [int]$base }
        $ws = @(); foreach ($s in $sizes) { $ws += [int][math]::Round($s[0] * $H / [double]$s[1]) }
        return @(2 * $margin + (($ws | Measure-Object -Sum).Sum) + $gap * 2, 2 * $margin + $H)
    }
}
function Check-Size([string]$name, [string]$file, [string]$orient, [int]$gap, [int]$margin, [string]$base) {
    $e = Expected-Size $orient $gap $margin $base
    $info = Img-Info $file
    Check $name (($info.size[0] -eq $e[0]) -and ($info.size[1] -eq $e[1])) ("{0}x{1}" -f $e[0], $e[1]) ("{0}x{1}" -f $info.size[0], $info.size[1])
    return $info
}

Log ("===== 补充 UI 验证 " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + " =====")
$three = @((Join-Path $script:ui '1-photo1.png'), (Join-Path $script:ui '2-photo2.png'), (Join-Path $script:ui '3-photo3.png'))
$set1 = Start-Fresh $three 'main'

# T1 保存按钮点击 + 状态栏 + 打开文件夹
$t = Get-Texts 'main-default'
Check 'T1.savebtn.exists' (Has $t '保存图片  Ctrl+S') 'Pane 名称「保存图片  Ctrl+S」' 'found'
Check 'T1.default.size_text' (Has $t '共 3 张图片 · 横向拼接') '默认横向 3 张' 'found'
$saveBtn = Invoke-Qa @('clickname', $script:title, '保存图片  Ctrl+S')
Check 'T1.savebtn.click' ($saveBtn -match 'click=') '点击保存按钮' $saveBtn
Start-Sleep -Milliseconds 1500
$t = Get-Texts 'main-after-save'
Check 'T1.save.status' (Has $t '已保存：') '状态栏「已保存：<路径>」' 'found'
Check 'T1.save.open_folder_btn' (Has $t '打开文件夹') '出现「打开文件夹」' 'found'
# 默认路径/文件名/像素
$latest = Get-ChildItem $script:ui -Filter '拼图_*.png' | Sort-Object LastWriteTime | Select-Object -Last 1
Check 'T1.save.file_pattern' ($latest.Name -match '^拼图_\d{8}_\d{6}(_\d+)?\.png$') '拼图_yyyyMMdd_HHmmss.png' $latest.Name
Check 'T1.save.dir' ($latest.DirectoryName.TrimEnd('\') -eq $script:ui.TrimEnd('\')) '第一张图目录' $latest.DirectoryName
$info = Check-Size 'T1.save.size' $latest.FullName 'Horizontal' 12 16 'auto'
Check 'T1.save.mode' ($info.mode -eq 'RGBA') 'RGBA' $info.mode
$p = Img-Pixel $latest.FullName 0 0
Check 'T1.save.transparent_corner' ($p.rgba[3] -eq 0) '角 alpha=0' (($p.rgba -join ','))
Shot 'supp-T1-saved.png' | Out-Null
# 点击「打开文件夹」
$winsBefore = Invoke-Qa @('wins')
Invoke-Qa @('clickname', $script:title, '打开文件夹') | Out-Null
Start-Sleep -Milliseconds 1500
$winsAfter = Invoke-Qa @('wins')
$cb = ([regex]::Matches($winsBefore, 'class=CabinetWClass')).Count
$ca = ([regex]::Matches($winsAfter, 'class=CabinetWClass')).Count
Check 'T1.openfolder' ($ca -gt $cb) '资源管理器窗口增加' ($cb.ToString() + ' -> ' + $ca.ToString())
$m = [regex]::Match($winsAfter, 'class=CabinetWClass rect=[^\r\n]*title=ui - 文件资源管理器')
if ($m.Success) { Invoke-Qa @('close', 'ui - 文件资源管理器') | Out-Null }

# T2 键盘 Ctrl+S
$save = Save-App $script:ui
Check 'T2.ctrlsave.keyboard' ($null -ne $save.file) 'Ctrl+S 保存成功' $save.output
Check 'T2.ctrlsave.time' ($save.ms -ge 0 -and $save.ms -lt 5000) '保存 3 图耗时记录' ($save.ms.ToString() + 'ms')

# T3 另存为 Ctrl+Shift+S
$custom = Join-Path $script:ui 'qa-supp-saveas.png'
Remove-Item $custom -Force -ErrorAction SilentlyContinue
Invoke-Qa @('keywin', $script:title, '^+s') | Out-Null
$dlg = Invoke-Qa @('waitwin', '另存拼图', '8000')
Check 'T3.saveas.dialog' ($dlg -match 'FOUND') '另存为对话框出现' $dlg
if ($dlg -match 'FOUND') {
    Invoke-Qa @('settext', '另存拼图', '文件名(N):', $custom) | Out-Null
    Invoke-Qa @('clickname', '另存拼图', '保存', 'contains') | Out-Null
    Start-Sleep -Milliseconds 1500
    Check 'T3.saveas.file' (Test-Path $custom) '另存文件存在' $custom
    if (Test-Path $custom) { $i2 = Img-Info $custom; Check 'T3.saveas.valid' ($i2.ok -and $i2.size[0] -eq 3606) '3606x932' ("{0}x{1}" -f $i2.size[0], $i2.size[1]) }
}

# T4 横向/纵向切换
$sw = Invoke-Qa @('segwait', $script:title, '拼接方式', '1', '纵向拼接', '4000')
Check 'T4.to_vertical' ($sw -match 'SEGWAIT_OK') '切换纵向并刷新' $sw
$okV = Wait-Ini $set1 'Orientation' 'Vertical' 3000
Check 'T4.ini_vertical' $okV 'Orientation=Vertical' (Read-Ini $set1).Orientation
$t = Get-Texts 'vertical'
Check 'T4.label_vertical' (Has $t '共 3 张图片 · 纵向拼接') '标签纵向' 'found'
$tv = Save-App $script:ui
if ($tv.file) { Check-Size 'T4.vertical.size' $tv.file 'Vertical' 12 16 'auto' | Out-Null }
$sw = Invoke-Qa @('segwait', $script:title, '拼接方式', '0', '横向拼接', '4000')
Check 'T4.back_horizontal' ($sw -match 'SEGWAIT_OK') '切回横向并刷新' $sw
$okH = Wait-Ini $set1 'Orientation' 'Horizontal' 3000
Check 'T4.ini_horizontal' $okH 'Orientation=Horizontal' (Read-Ini $set1).Orientation
Shot 'supp-T4-horizontal.png' | Out-Null

# T5 间距/边距 步进器（含减号）
Invoke-Qa @('step', $script:title, '图片间距', '+', '3') | Out-Null
$ok = Wait-Ini $set1 'Gap' '15' 3000
Check 'T5.gap_plus3' $ok 'Gap=15' (Read-Ini $set1).Gap
$sg = Save-App $script:ui
if ($sg.file) { Check-Size 'T5.gap_plus3.size' $sg.file 'Horizontal' 15 16 'auto' | Out-Null }
Invoke-Qa @('step', $script:title, '图片间距', '-', '3') | Out-Null
$ok = Wait-Ini $set1 'Gap' '12' 3000
Check 'T5.gap_minus3' $ok 'Gap=12（减号有效）' (Read-Ini $set1).Gap
Invoke-Qa @('step', $script:title, '外边距', '+', '10') | Out-Null
$ok = Wait-Ini $set1 'Margin' '26' 3000
Check 'T5.margin_plus10' $ok 'Margin=26' (Read-Ini $set1).Margin
$sm = Save-App $script:ui
if ($sm.file) { Check-Size 'T5.margin_plus10.size' $sm.file 'Horizontal' 12 26 'auto' | Out-Null }
Invoke-Qa @('step', $script:title, '外边距', '-', '10') | Out-Null
$ok = Wait-Ini $set1 'Margin' '16' 3000
Check 'T5.margin_minus10' $ok 'Margin=16（减号有效）' (Read-Ini $set1).Margin

# T6 基准尺寸 自动/手动
Invoke-Qa @('clickname', $script:title, '自动（中位数）') | Out-Null
$ok = Wait-Ini $set1 'AutoBaseSize' '0' 3000
Check 'T6.auto_off' $ok 'AutoBaseSize=0' (Read-Ini $set1).AutoBaseSize
$before = [int](Read-Ini $set1).BaseSize
Invoke-Qa @('step', $script:title, '基准尺寸', '+', '1') | Out-Null
$sw = [Diagnostics.Stopwatch]::StartNew()
$okStep = $false; $after = $before
while ($sw.ElapsedMilliseconds -lt 3000) { $after = [int](Read-Ini $set1).BaseSize; if ($after -eq 910) { $okStep = $true; break }; Start-Sleep -Milliseconds 80 }
Check 'T6.base_plus10' $okStep 'BaseSize=910（自动基准 900+10）' ('before=' + $before + ' after=' + $after)
$sb = Save-App $script:ui
if ($sb.file) { Check-Size 'T6.manual_base.size' $sb.file 'Horizontal' 12 16 ([string]$after) | Out-Null }
Invoke-Qa @('clickname', $script:title, '自动（中位数）') | Out-Null
$ok = Wait-Ini $set1 'AutoBaseSize' '1' 3000
Check 'T6.auto_on' $ok 'AutoBaseSize=1' (Read-Ini $set1).AutoBaseSize

# T7 自定义颜色（当前背景透明 → alpha 应为 255）
Invoke-Qa @('clickname', $script:title, '背景透明') | Out-Null
Start-Sleep -Milliseconds 500
Invoke-Qa @('clickname', $script:title, '背景自定义') | Out-Null
$dlg = Invoke-Qa @('waitwin', '颜色', '8000')
Check 'T7.color_dialog' ($dlg -match 'FOUND') '颜色对话框出现' $dlg
if ($dlg -match 'FOUND') {
    Invoke-Qa @('settext', '颜色', '红(R):', '10') | Out-Null
    Invoke-Qa @('settext', '颜色', '绿(G):', '200') | Out-Null
    Invoke-Qa @('settext', '颜色', '蓝(U):', '30') | Out-Null
    Start-Sleep -Milliseconds 300
    Invoke-Qa @('clickname', '颜色', '确定') | Out-Null
    Start-Sleep -Milliseconds 800
    $ini = Read-Ini $set1
    Check 'T7.custom_argb' ($ini.BackgroundArgb -eq '#FF0AC81E') '#FF0AC81E（透明背景下自定义 alpha=255）' $ini.BackgroundArgb
    $sc = Save-App $script:ui
    if ($sc.file) {
        $p = Img-Pixel $sc.file 0 0
        Check 'T7.custom_pixel' (($p.rgba[0] -eq 10) -and ($p.rgba[1] -eq 200) -and ($p.rgba[2] -eq 30) -and ($p.rgba[3] -eq 255)) '(10,200,30,255)' (($p.rgba -join ','))
    }
}
Invoke-Qa @('clickname', $script:title, '背景透明') | Out-Null
Start-Sleep -Milliseconds 300

# T8 输出格式 JPG
$sw = Invoke-Qa @('segwait', $script:title, '输出格式选择', '1', 'JPG 不支持透明', '4000')
Check 'T8.to_jpg' ($sw -match 'SEGWAIT_OK') '切换到 JPG' $sw
Start-Sleep -Milliseconds 500
$t = Get-Texts 'jpg'
Check 'T8.jpg_savebtn' (Has $t '保存 JPG  Ctrl+S') '按钮「保存 JPG  Ctrl+S」' 'found'
Check 'T8.jpg_note' (Has $t 'JPG 不支持透明，透明区域将填白') 'JPG 提示' 'found'
$sj = Save-App $script:ui
Check 'T8.jpg.saved' ($null -ne $sj.file -and $sj.file -match '\.jpg$') '保存 .jpg' $sj.output
if ($sj.file) {
    $ij = Img-Info $sj.file
    Check 'T8.jpg.mode' ($ij.mode -eq 'RGB') 'RGB 无 Alpha' $ij.mode
    Check 'T8.jpg.size' ($ij.size[0] -eq 3606 -and $ij.size[1] -eq 932) '3606x932' ("{0}x{1}" -f $ij.size[0], $ij.size[1])
    $p = Img-Pixel $sj.file 0 0
    Check 'T8.jpg.white' (($p.rgba[0] -ge 250) -and ($p.rgba[1] -ge 250) -and ($p.rgba[2] -ge 250)) '透明压白 ≈255,255,255' (($p.rgba -join ','))
}
Invoke-Qa @('segwait', $script:title, '输出格式选择', '0', 'PNG 完整保留', '4000') | Out-Null
Shot 'supp-T8-png.png' | Out-Null
App-Kill

# T9 干净的 60 张图片（确认无旧实例）
Log '----- T9 干净实例 60 张 -----'
$manyDir = Join-Path $script:qa 'ui-60'
$set9 = Join-Path $script:qa 'settings\supp-60'
Remove-Item $set9 -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $set9 | Out-Null
$env:LIGHTCOLLAGE_SETTINGS_DIR = $set9
$manyFiles = @(Get-ChildItem $manyDir -Filter '*.png' | Sort-Object Name | ForEach-Object { $_.FullName })
$uargs = @('uistart', $script:exe, '已载入 60 张图片', '30000') + $manyFiles
$u = Invoke-Qa $uargs
Check 'T9.load60' ($u -match 'found=True') '60 张全部载入（干净实例）' $u
$t = Get-Texts '60'
Check 'T9.count60' (Has $t '共 60 张图片') '共 60 张' 'found'
Check 'T9.status60' (Has $t '已载入 60 张图片') '已载入 60 张' 'found'
Shot 'supp-T9-60images.png' | Out-Null
$s60 = Save-App $manyDir
Check 'T9.save60' ($null -ne $s60.file) '60 张保存成功' $s60.output
if ($s60.file) {
    $i60 = Img-Info $s60.file
    Check 'T9.save60.size' ($i60.size[0] -eq 12740 -and $i60.size[1] -eq 132) '12740x132' ("{0}x{1}" -f $i60.size[0], $i60.size[1])
    Check 'T9.save60.time' ($s60.ms -ge 0 -and $s60.ms -lt 15000) '保存耗时' ($s60.ms.ToString() + 'ms')
}
App-Kill

# T10 性能：5 次冷启动 + 3 次三图就绪
Log '----- T10 性能 -----'
$cold = @()
for ($i = 1; $i -le 5; $i++) {
    App-Kill
    $setP = Join-Path $script:qa 'settings\supp-perf'
    Remove-Item $setP -Recurse -Force -ErrorAction SilentlyContinue
    $env:LIGHTCOLLAGE_SETTINGS_DIR = $setP
    $o = Invoke-Qa @('uistart', $script:exe, '共 0 张图片', '15000')
    $ms = -1; if ($o -match 'window_visible_ms=(\d+)') { $ms = [int]$Matches[1] }
    $cold += $ms
    Log ("coldstart run$i : window_visible_ms=" + $ms)
}
$sorted = @($cold | Sort-Object)
$median = $sorted[[int](($sorted.Count - 1) / 2)]
Check 'T10.coldstart.recorded' ($cold.Count -eq 5) '5 次窗口可见时间' ($cold -join ',')
Check 'T10.coldstart.min' ($sorted[0] -lt 400) '最快 < 400ms' ($sorted[0].ToString() + 'ms')
Check 'T10.coldstart.median' ($median -lt 400) '中位数 < 400ms' ($median.ToString() + 'ms')
$ready = @()
for ($i = 1; $i -le 3; $i++) {
    App-Kill
    $setP = Join-Path $script:qa 'settings\supp-perf3'
    Remove-Item $setP -Recurse -Force -ErrorAction SilentlyContinue
    $env:LIGHTCOLLAGE_SETTINGS_DIR = $setP
    $o = Invoke-Qa @('uistart', $script:exe, '已载入 3 张图片', '15000', $three[0], $three[1], $three[2])
    $rms = -1; if ($o -match 'text_ready_ms=(\d+)') { $rms = [int]$Matches[1] }
    $ready += $rms
    Log ("three-images run$i : text_ready_ms=" + $rms + " (" + $o + ")")
}
Check 'T10.three.recorded' ($ready.Count -eq 3) '3 次三图就绪时间' ($ready -join ',')
Check 'T10.three.median_lt_3000' (($ready | Sort-Object)[1] -lt 3000) '三图中位数 < 3000ms' (($ready | Sort-Object)[1].ToString() + 'ms')
App-Kill
Remove-Item Env:\LIGHTCOLLAGE_SETTINGS_DIR -ErrorAction SilentlyContinue

Log ''
Log ("===== 补充汇总：PASS=" + $script:pass + " FAIL=" + $script:fail + " =====")
foreach ($r in $script:results) { if (-not $r.pass) { Log ("FAILED: " + $r.name + " | expected=" + $r.expected + " | actual=" + $r.actual) } }
$summary = [pscustomobject]@{ total = $script:pass + $script:fail; pass = $script:pass; fail = $script:fail; results = $script:results; coldstart_ms = $cold; three_images_ready_ms = $ready }
$summary | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $script:logs 'ui-supplement-results.json') -Encoding UTF8
if ($script:fail -gt 0) { exit 1 } else { exit 0 }
