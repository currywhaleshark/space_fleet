param([ValidateSet('debug','release')][string]$Configuration, [switch]$Headless)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../tools/godot.ps1"
$sfLabel = if ($Headless) { "$Configuration-headless" } else { $Configuration }
$sfPerfLog = Join-Path $PSScriptRoot "game-$sfLabel.log"
$sfArguments = @('--log-file',('"'+$sfPerfLog+'"'),'--','--autoplay','--seed=1','--perf-trace','--perf-seconds=600','--autoplay-quit','--post-seconds=0')
if ($Configuration -eq 'debug') {
    $sfExecutable = $GodotConsole
    $sfWorking = $ProjectDir
    $sfArguments = @('--path',('"'+$ProjectDir+'"')) + $sfArguments
} else {
    $sfWorking = Join-Path $ProjectDir 'builds/SpaceFleet-20261010-optimized-Windows-x64'
    $sfExecutable = Join-Path $sfWorking 'SpaceFleet.exe'
}
if ($Headless) { $sfArguments = @('--headless','--fixed-fps','60') + $sfArguments }
$sfProcess = Start-Process -FilePath $sfExecutable -WorkingDirectory $sfWorking -WindowStyle Hidden -PassThru -ArgumentList $sfArguments
$sfWatch = [Diagnostics.Stopwatch]::StartNew()
while (!$sfProcess.WaitForExit(1000)) {
    if ($sfWatch.Elapsed.TotalSeconds -gt 900) { Stop-Process -Id $sfProcess.Id; throw 'Performance run timed out.' }
}
Get-Content $sfPerfLog -Encoding UTF8 | Select-String 'autoplay start:|autoplay 10min:|autoplay outcome:|autoplay summary:|ERROR:|Exception'
exit $sfProcess.ExitCode
