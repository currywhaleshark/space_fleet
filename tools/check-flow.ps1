# Actual renderer is required: headless Godot cannot capture the Pilot mouse.
param([string]$RenderingMethod = 'forward_plus')
. "$PSScriptRoot\godot.ps1"
$log = Join-Path $ProjectDir 'shots/s7_flow_checks.log'
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
$arguments = @('--path', "`"$ProjectDir`"", '--rendering-method', $RenderingMethod,
    '--fixed-fps', '60', '--log-file', "`"$log`"", '--', '--flow-test', '--seed=1')
$proc = Start-Process -FilePath $Godot -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $proc.WaitForExit(60000)) {
    Stop-Process -Id $proc.Id -Force
    throw 'Scene flow checks timed out'
}
$output = Get-Content -LiteralPath $log -Encoding UTF8
$output | Write-Output
if ($proc.ExitCode -ne 0 -or -not ($output -match '^PASS: \d+ scene flow checks$') -or $output -match 'ERROR|Exception') {
    throw "Scene flow checks failed (exit=$($proc.ExitCode))"
}
