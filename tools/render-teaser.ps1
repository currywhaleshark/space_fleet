# Render the isolated in-engine teaser at a deterministic 30 fps, then mix existing game SFX.
param(
    [string]$Name = 'teaser_v1',
    [string]$Python = '',
    [string]$Ffmpeg = '',
    [switch]$SkipRender
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\godot.ps1"
if ($Name -notmatch '^[A-Za-z0-9_-]+$') { throw 'Use a simple output folder name' }
$teaserDir = Join-Path $ProjectDir "shots/$Name"
New-Item -ItemType Directory -Force $teaserDir | Out-Null
if (!$Python) {
    $Python = (Get-Command python -ErrorAction SilentlyContinue).Source
    $bundledPython = Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
    if (Test-Path -LiteralPath $bundledPython) { $Python = $bundledPython }
}
if (!$Ffmpeg) {
    $Ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
    if (!$Ffmpeg) {
        $package = Join-Path $env:LOCALAPPDATA 'Microsoft/WinGet/Packages'
        $Ffmpeg = Get-ChildItem -LiteralPath $package -Filter ffmpeg.exe -Recurse -File | Select-Object -First 1 -ExpandProperty FullName
    }
}
if (!$Python -or !$Ffmpeg) { throw 'Existing Python with numpy and FFmpeg are required; pass -Python / -Ffmpeg paths' }
$teaserRaw = Join-Path $teaserDir 'engine_capture.avi'
$teaserCues = Join-Path $teaserDir 'sound_cues.json'
$teaserAudio = Join-Path $teaserDir 'sound_design.wav'
$teaserOutput = Join-Path $teaserDir 'Space_Fleet_Teaser.mp4'
$teaserLog = Join-Path $teaserDir 'render.log'
if (!$SkipRender) {
    dotnet build (Join-Path $ProjectDir 'SpaceFleet.csproj') -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    $teaserArgs = @('--path',('"'+$ProjectDir+'"'),'--fixed-fps','30','--disable-vsync',
        '--write-movie',('"'+$teaserRaw+'"'),'--log-file',('"'+$teaserLog+'"'),'--','--teaser',('--teaser-cues="'+$teaserCues+'"'))
    $teaserProc = Start-Process -FilePath $Godot -WindowStyle Hidden -ArgumentList $teaserArgs -PassThru
    # Poll at short intervals so the caller can receive progress without a long blocking tool wait.
    while (!$teaserProc.WaitForExit(1000)) {
        if ((Get-Date)-$teaserProc.StartTime -gt [TimeSpan]::FromMinutes(25)) { Stop-Process -Id $teaserProc.Id; throw 'Teaser render timed out' }
    }
    if ($teaserProc.ExitCode -ne 0) { throw "Godot failed: $teaserLog" }
    if (Select-String -LiteralPath $teaserLog -Pattern 'ERROR:|Exception' -Quiet) { throw "Render errors: $teaserLog" }
}
& $Python (Join-Path $PSScriptRoot 'mix-teaser.py') --cues $teaserCues --assets (Join-Path $ProjectDir 'assets/audio') --output $teaserAudio
if ($LASTEXITCODE -ne 0) { throw 'Audio mixing failed' }
& $Ffmpeg -y -hide_banner -loglevel warning -i $teaserRaw -i $teaserAudio -map 0:v:0 -map 1:a:0 -t 38 -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -r 30 -c:a aac -b:a 256k -movflags +faststart $teaserOutput
if ($LASTEXITCODE -ne 0) { throw 'Video encoding failed' }
$teaserProbe = Join-Path (Split-Path -Parent $Ffmpeg) 'ffprobe.exe'
& $teaserProbe -v error -show_entries 'format=duration,size:stream=codec_name,width,height,r_frame_rate,nb_frames,sample_rate,channels' -of json $teaserOutput | Set-Content -Encoding UTF8 (Join-Path $teaserDir 'verification.json')
if ($LASTEXITCODE -ne 0) { throw 'Video verification failed' }
$teaserInfo = Get-Content -LiteralPath (Join-Path $teaserDir 'verification.json') -Encoding UTF8 -Raw | ConvertFrom-Json
$teaserVideo = $teaserInfo.streams | Where-Object codec_name -eq 'h264'
$teaserSound = $teaserInfo.streams | Where-Object codec_name -eq 'aac'
if ($teaserVideo.width -ne 1920 -or $teaserVideo.height -ne 1080 -or $teaserVideo.nb_frames -ne '1140' -or $teaserVideo.r_frame_rate -ne '30/1') { throw 'Unexpected video resolution, frame rate or frame count' }
if ($teaserSound.channels -ne 2 -or $teaserSound.sample_rate -ne '48000' -or [Math]::Abs([double]$teaserInfo.format.duration-38) -gt .04) { throw 'Unexpected audio or duration' }
Write-Output "Rendered: $teaserOutput"
