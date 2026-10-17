param([string[]]$Models=@())
$ErrorActionPreference='Stop'
$modelIds=@($Models | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique)
. "$PSScriptRoot\godot.ps1"
$logDir=Join-Path $ProjectDir 'shots\menu-art'
New-Item -ItemType Directory -Force $logDir | Out-Null
function Run-Godot([string[]]$Options,[string]$Name) {
    $log=Join-Path $logDir "$Name.log"
    $arguments=@('--path',"`"$ProjectDir`"",'--log-file',"`"$log`"")+$Options
    $process=Start-Process -FilePath $Godot -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if(-not $process.WaitForExit(180000)){Stop-Process -Id $process.Id -Force;throw "$Name timed out"}
    $result=Get-Content -Encoding UTF8 -LiteralPath $log
    if($process.ExitCode -ne 0 -or ($result -match '^ERROR:')){throw ($result -join "`n")}
    Write-Output "$Name complete"
}
Run-Godot @('--headless','--import') 'import-icons'
$portraitArgs=@('--resolution','640x240','--','--export-menu-art')
if($modelIds.Count){$portraitArgs+="--portrait-models=$($modelIds -join ',')"}
Run-Godot $portraitArgs 'portraits'
$rendered=@(Get-Content -LiteralPath (Join-Path $logDir 'portraits.log') -Encoding UTF8 | Where-Object { $_ -match '^Portrait: ' } | ForEach-Object { $_.Substring(10).Trim() })
if(-not $rendered.Count){throw 'No ship portraits were rendered.'}
foreach($modelId in $modelIds){if($modelId -notin $rendered){throw "Requested portrait was not rendered: $modelId"}}
Run-Godot @('--headless','--import') 'import-portraits'
