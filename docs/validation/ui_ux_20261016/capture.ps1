param([string]$Phase='before', [string]$Only='')
$ErrorActionPreference='Stop'
$projectRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$env:APPDATA=Join-Path $projectRoot 'shots\ui-audit-user'
New-Item -ItemType Directory -Force $env:APPDATA | Out-Null
$engine=Join-Path $env:USERPROFILE 'AppData\Local\Programs\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe'
$cases=@(
    @{Name='hangar'; Resolution='1600x900'; Args=@('--menu=hangar','--hangar-system=MainGun')},
    @{Name='hangar-small'; Resolution='1280x720'; Args=@('--menu=hangar','--hangar-system=Shield')},
    @{Name='hangar-pd'; Resolution='1600x900'; Args=@('--menu=hangar','--hangar-hull=escort','--hangar-system=Secondary','--hangar-component=pd-barrage','--hangar-focus')},
    @{Name='hangar-engine'; Resolution='1280x720'; Args=@('--menu=hangar','--hangar-hull=escort','--hangar-system=Engine','--hangar-component=engine-vector')},
    @{Name='hangar-sensor'; Resolution='1280x720'; Args=@('--menu=hangar','--hangar-hull=escort','--hangar-system=Sensor','--hangar-component=sensor-lowprofile')},
    @{Name='hangar-generator'; Resolution='1280x720'; Args=@('--menu=hangar','--hangar-hull=escort','--hangar-system=Generator','--hangar-component=generator-output')},
    @{Name='hangar-bay'; Resolution='1280x720'; Args=@('--menu=hangar','--hangar-hull=escort','--hangar-bay=utility-port')},
    @{Name='research'; Resolution='1600x900'; Args=@('--menu=research')},
    @{Name='research-locked'; Resolution='1280x720'; Args=@('--menu=research','--hangar-hull=battleship')},
    @{Name='combat'; Resolution='1600x900'; Args=@('--battle','--control=IC-21','--seed=1','--look-at=R-BB-01')},
    @{Name='combat-small'; Resolution='1280x720'; Args=@('--battle','--control=IC-21','--seed=1','--advance=12')},
    @{Name='am-warning'; Resolution='1280x720'; Args=@('--control=IC-21','--am-demo=risk-safe')},
    @{Name='am-ready'; Resolution='1600x900'; Frames=200; Args=@('--control=IC-21','--am-demo=armed')}
)
foreach($case in $cases) {
    if($Only -and $case.Name -ne $Only){continue}
    $stem=Join-Path $PSScriptRoot "$Phase-$($case.Name)"
    $frames=if($case.Frames){$case.Frames}else{120}
    $gameArgs=@('--path',"`"$projectRoot`"",'--resolution',$case.Resolution,'--log-file',"`"$stem.log`"",'--fixed-fps','60','--')+$case.Args+@("--shot=`"$stem.png`"","--frames=$frames")
    $proc=Start-Process -FilePath $engine -ArgumentList $gameArgs -WindowStyle Hidden -PassThru
    $timer=[Diagnostics.Stopwatch]::StartNew()
    while(-not $proc.HasExited -and $timer.Elapsed.TotalSeconds -lt 180){Start-Sleep -Milliseconds 500;$proc.Refresh()}
    if(-not $proc.HasExited){Stop-Process -Id $proc.Id -Force;throw "Capture timed out: $stem"}
    if($proc.ExitCode -ne 0 -or -not(Test-Path -LiteralPath "$stem.png")){throw "Capture failed: $stem (exit $($proc.ExitCode))"}
    Write-Output "Captured $stem.png"
}
