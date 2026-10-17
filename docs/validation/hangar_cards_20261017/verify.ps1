param([string]$Mode='capture',[string]$Only='')
$ErrorActionPreference='Stop'
$projectRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$env:APPDATA=Join-Path $projectRoot 'shots\hangar-cards-user'
New-Item -ItemType Directory -Force $env:APPDATA | Out-Null
$engine=Join-Path $env:USERPROFILE 'AppData\Local\Programs\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe'
$cases=if($Mode -eq 'checks') {
    @('flow','match-flow','hangar','progression','audio','fleet-audio') | ForEach-Object {
        @{Name="$_-test";Resolution='1280x720';Args=@("--$_-test",'--seed=1');Check=$true}
    }
} else {
    @(
        @{Name='hangar';Resolution='1600x900';Args=@('--menu=hangar')},
        @{Name='hangar-small';Resolution='1280x720';Args=@('--menu=hangar','--hangar-hull=battleship')},
        @{Name='equipment';Resolution='1280x720';Args=@('--menu=hangar','--hangar-hull=escort','--hangar-bay=utility-port')},
        @{Name='research';Resolution='1600x900';Args=@('--menu=research')},
        @{Name='research-mars';Resolution='1280x720';Args=@('--menu=research','--hangar-hull=mars_battleship')},
        @{Name='details';Resolution='1280x720';Args=@('--menu=hangar','--hangar-hull=battleship','--menu-details=component-main-triple')}
    )
}
foreach($case in $cases){
    if($Only -and $Only -ne $case.Name){continue}
    $stem=Join-Path $PSScriptRoot $case.Name
    $arguments=@('--path',"`"$projectRoot`"",'--resolution',$case.Resolution,'--log-file',"`"$stem.log`"",'--fixed-fps','60','--')+$case.Args
    if(-not $case.Check){$arguments+=@("--shot=`"$stem.png`"",'--frames=180')}
    $process=Start-Process -FilePath $engine -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if(-not $process.WaitForExit(300000)){Stop-Process -Id $process.Id -Force;throw "Timed out: $($case.Name)"}
    $result=Get-Content -Encoding UTF8 -LiteralPath "$stem.log"
    if($process.ExitCode -ne 0 -or ($result -match '^ERROR:') -or ($case.Check -and -not($result -match 'PASS:'))){throw ($result -join "`n")}
    if($case.Check){$result | Select-String 'PASS:' | ForEach-Object {$_.Line}}
    else {Write-Output "Captured $($case.Name)"}
}
