# 티저 영상 전체를 만든다: Godot 무비 메이커로 프레임(AVI) → 소리 합성(cine_audio.py) → ffmpeg로 MP4.
# 결과: shots/cine/render/project_fleet_teaser.mp4 (v3) 또는 project_fleet_v4.mp4 (-Cut v4)
param([string]$Size = '1920x1080', [int]$Crf = 17, [ValidateSet('v3','v4')][string]$Cut = 'v3')
. "$PSScriptRoot\godot.ps1"
$ErrorActionPreference = 'Stop'
$dir = Join-Path $ProjectDir 'shots\cine\render'
New-Item -ItemType Directory -Force $dir | Out-Null
$tag = if ($Cut -eq 'v3') { 'teaser' } else { $Cut }
$avi = Join-Path $dir "${tag}_raw.avi"
$events = Join-Path $dir $(if ($Cut -eq 'v3') { 'events.json' } else { "${tag}_events.json" })
$wav = Join-Path $dir "${tag}_audio.wav"
$mp4 = Join-Path $dir $(if ($Cut -eq 'v3') { 'project_fleet_teaser.mp4' } else { "project_fleet_$Cut.mp4" })
$log = Join-Path $dir $(if ($Cut -eq 'v3') { 'render.log' } else { "${tag}_render.log" })

& "$PSScriptRoot\build.ps1" | Out-Null

# MJPEG 화질을 녹화 동안만 올린다(override.cfg는 프로젝트 설정을 덮어쓴다).
$override = Join-Path $ProjectDir 'override.cfg'
$w, $h = $Size -split 'x'
Set-Content -Path $override -Value "[editor]`nmovie_writer/mjpeg_quality=0.95`n[display]`nwindow/size/viewport_width=$w`nwindow/size/viewport_height=$h`n" -Encoding ascii
try {
    $argList = @('--path', "`"$ProjectDir`"", '--resolution', $Size, '--write-movie', "`"$avi`"", '--fixed-fps', '30',
        '--log-file', "`"$log`"", '--', '--cine', "--cine-cut=$Cut", "--cine-events=`"$($events -replace '\\', '/')`"")
    $proc = Start-Process -FilePath $Godot -ArgumentList $argList -WindowStyle Hidden -PassThru
    if (-not $proc.WaitForExit(3600000)) { Stop-Process -Id $proc.Id -Force; throw 'render timed out' }
}
finally {
    [System.IO.File]::Delete($override)
}
Get-Content $log | Select-String 'cine|ERROR|Exception' | Select-Object -Last 15
if (-not (Test-Path $avi)) { throw "no movie: $avi" }

python "$PSScriptRoot\cine_audio.py" $events $wav
$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
if (-not $ffmpeg) { $ffmpeg = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter ffmpeg.exe | Select-Object -First 1 -ExpandProperty FullName }
& $ffmpeg -y -hide_banner -loglevel error -i $avi -i $wav -map 0:v -map 1:a -c:v libx264 -preset slow -crf $Crf -pix_fmt yuv420p `
    -c:a aac -b:a 192k -shortest -movflags +faststart $mp4
Write-Output "teaser: $mp4"
