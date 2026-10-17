$ErrorActionPreference='Stop'
$projectRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$env:APPDATA=Join-Path $projectRoot 'shots\t4-cruiser-model-user'
$engine=Join-Path $env:USERPROFILE 'AppData\Local\Programs\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe'
foreach($trial in 1..3){
    $log=Join-Path $PSScriptRoot "match-flow-handoff-$trial.log"
    $process=Start-Process -FilePath $engine -WindowStyle Hidden -PassThru -ArgumentList @('--path',"`"$projectRoot`"",'--log-file',"`"$log`"",'--fixed-fps','60','--','--match-flow-test')
    if(-not $process.WaitForExit(60000)){Stop-Process -Id $process.Id;throw "Timed out: trial $trial"}
    $result=Get-Content -LiteralPath $log -Encoding UTF8 -Raw
    if($process.ExitCode -ne 0 -or $result -match 'ERROR:|Exception' -or $result -notmatch 'PASS: 40'){throw $result}
    $result -split "`n" | Where-Object {$_ -match 'PASS:|voice handoff'} | Write-Output
}
