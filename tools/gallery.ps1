# 함선 외형 검토 컷. 예: .\tools\gallery.ps1 battleship hero  ->  shots/gallery/battleship_hero.png
# 함선 ID: battleship, escort, interceptor, mars_battleship, mars_escort, mars_interceptor. 시점: hero, side, rear, front, top.
param([Parameter(Mandatory)][string]$Ship, [string]$View = 'hero', [string]$Faction = 'Blue', [string]$Tag = '')
. "$PSScriptRoot\godot.ps1"
$dir = Join-Path $ProjectDir 'shots\gallery'
New-Item -ItemType Directory -Force $dir | Out-Null
$name = "${Ship}_${View}$(if ($Faction -ne 'Blue') { '_' + $Faction.ToLower() })$Tag"
$out = (Join-Path $dir "$name.png") -replace '\\', '/'
$log = Join-Path $dir "$name.log"
$argList = @('--path', "`"$ProjectDir`"", '--resolution', '1600x900', '--log-file', "`"$log`"", '--',
    "--gallery=$Ship", "--gallery-view=$View", "--gallery-faction=$Faction", "--shot=`"$out`"")
$proc = Start-Process -FilePath $Godot -ArgumentList $argList -WindowStyle Hidden -PassThru
if (-not $proc.WaitForExit(180000)) { Stop-Process -Id $proc.Id -Force; Write-Error "timed out: $name"; exit 1 }
if (Test-Path $out) { Write-Output "saved $out" } else { Get-Content $log | Select-Object -Last 15; Write-Error "no shot: $name"; exit 1 }
