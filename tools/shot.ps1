# 스크린샷 모드로 한 컷 찍는다. 예: .\tools\shot.ps1 bb_view --control=BB-01 --turn=30,-10
# 결과는 shots/<name>.png. 셰이더 첫 컴파일이 길 수 있어 120초까지 기다린다.
param(
    [Parameter(Mandatory)][string]$Name,
    [Parameter(ValueFromRemainingArguments)][string[]]$GameArgs
)
. "$PSScriptRoot\godot.ps1"

$shots = Join-Path $ProjectDir 'shots'
New-Item -ItemType Directory -Force $shots | Out-Null
$out = (Join-Path $shots "$Name.png") -replace '\\', '/'

$log = (Join-Path $shots "$Name.log") -replace '\\', '/'
$argList = @('--path', "`"$ProjectDir`"", '--log-file', "`"$log`"", '--fixed-fps', '60', '--', "--shot=`"$out`"") + $GameArgs
$proc = Start-Process -FilePath $Godot -ArgumentList $argList -WindowStyle Hidden -PassThru
if (-not $proc.WaitForExit(120000)) {
    Stop-Process -Id $proc.Id -Force
    Write-Error "timed out: $Name"
    exit 1
}
if ($proc.ExitCode -ne 0) {
    Get-Content -LiteralPath $log -Encoding UTF8 | Select-Object -Last 15
    Write-Error "Godot exited with $($proc.ExitCode): $Name"
    exit 1
}
if (Test-Path $out) { Write-Output "saved $out" } else { Write-Error "no shot: $Name"; exit 1 }
