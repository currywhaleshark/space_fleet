$ErrorActionPreference='Stop'
$projectRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$env:APPDATA=Join-Path $projectRoot 'shots\ui-audit-user'
New-Item -ItemType Directory -Force $env:APPDATA | Out-Null
$engine=Join-Path $env:USERPROFILE 'AppData\Local\Programs\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe'
foreach($check in @('hangar','progression','weapon-input')) {
    $log=Join-Path $PSScriptRoot "$check-checks.log"
    $gameArgs=@('--path',"`"$projectRoot`"",'--log-file',"`"$log`"",'--fixed-fps','60','--',"--$check-test",'--seed=1')
    $proc=Start-Process -FilePath $engine -ArgumentList $gameArgs -WindowStyle Hidden -PassThru
    $timer=[Diagnostics.Stopwatch]::StartNew()
    while(-not $proc.HasExited -and $timer.Elapsed.TotalSeconds -lt 300){Start-Sleep -Milliseconds 500;$proc.Refresh()}
    if(-not $proc.HasExited){Stop-Process -Id $proc.Id -Force;throw "Check timed out: $check"}
    $result=Get-Content -LiteralPath $log -Encoding UTF8
    $result | Select-String 'PASS:|ERROR:' | ForEach-Object {$_.Line}
    if($proc.ExitCode -ne 0 -or -not($result -match 'PASS:')){throw "Check failed: $check (exit $($proc.ExitCode))"}
}
