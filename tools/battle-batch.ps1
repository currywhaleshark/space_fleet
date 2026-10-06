param([int[]]$Seeds = (1..20), [switch]$Mirror, [int]$Minutes = 25, [switch]$Parallel,
      [int]$Jobs = 4, [string]$Label = 'baseline', [switch]$Profile, [int]$PostSeconds = 0)
. "$PSScriptRoot\godot.ps1"
$batchArgs = @('--seeds', ($Seeds -join ','), '--minutes', "$Minutes", '--jobs', $(if ($Parallel) { "$Jobs" } else { '1' }),
    '--output', (Join-Path $ProjectDir "shots/batch/$(Get-Date -Format 'yyyyMMdd_HHmmss')_$Label.csv"))
if ($Mirror) { $batchArgs += '--mirror' }
if ($Profile) { $batchArgs += '--profile' }
if ($PostSeconds -gt 0) { $batchArgs += @('--post-seconds', "$PostSeconds") }
dotnet run --project (Join-Path $PSScriptRoot 'BattleBatch/BattleBatch.csproj') --configuration Release --verbosity quiet -- @batchArgs
exit $LASTEXITCODE
