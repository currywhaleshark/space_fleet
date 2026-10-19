# 티저 한 장면을 한 장으로 확인한다. 예: .\tools\cine-still.ps1 impact 3.0  →  shots/cine/impact_3.0.png
param([Parameter(Mandatory)][string]$Shot, [double]$At = 0, [string]$Size = '1920x1080', [ValidateSet('v3','v4')][string]$Cut = 'v3')
. "$PSScriptRoot\godot.ps1"
$dir = Join-Path $ProjectDir 'shots\cine'
New-Item -ItemType Directory -Force $dir | Out-Null
$name = '{0}{1}_{2}' -f $(if ($Cut -eq 'v3') { '' } else { "$Cut-" }), $Shot, $At.ToString('0.0', [Globalization.CultureInfo]::InvariantCulture)
$out = (Join-Path $dir "$name.png") -replace '\\', '/'
$log = Join-Path $dir "$name.log"
$argList = @('--path', "`"$ProjectDir`"", '--resolution', $Size, '--fixed-fps', '30', '--log-file', "`"$log`"", '--',
    "--cine-still=$Shot@$($At.ToString([Globalization.CultureInfo]::InvariantCulture))", "--cine-out=`"$out`"", "--cine-cut=$Cut")
$proc = Start-Process -FilePath $Godot -ArgumentList $argList -WindowStyle Hidden -PassThru
if (-not $proc.WaitForExit(240000)) { Stop-Process -Id $proc.Id -Force; Write-Error "timed out: $name"; exit 1 }
Get-Content $log | Select-String 'cine|ERROR|Exception|  [A-Z]' | Select-Object -Last 20
if (Test-Path $out) { Write-Output "saved $out" } else { Write-Error "no still: $name"; exit 1 }
