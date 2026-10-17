param([string]$Only = '')
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$env:APPDATA = Join-Path $projectRoot 'shots\component-render-user'
New-Item -ItemType Directory -Force -Path $env:APPDATA | Out-Null
$engine = Join-Path $env:USERPROFILE 'AppData\Local\Programs\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe'
$cases = @(
    @{Name='model-checks'; Args=@('--model-test')},
    @{Name='hangar-flow'; Args=@('--hangar-test','--seed=1')},
    @{Name='progression-flow'; Args=@('--progression-test','--seed=1')},
    @{Name='hangar-triple'; Args=@('--menu=hangar','--hangar-hull=escort','--hangar-exterior','--hangar-component=main-triple,generator-output,shield-recharge')},
    @{Name='hangar-fixed'; Args=@('--menu=hangar','--hangar-hull=mars_interceptor','--hangar-exterior','--hangar-component=main-precision')},
    @{Name='hangar-generator'; Args=@('--menu=hangar','--hangar-hull=escort','--hangar-exterior','--hangar-component=generator-output','--hangar-system=Generator')},
    @{Name='hangar-shield'; Args=@('--menu=hangar','--hangar-hull=escort','--hangar-exterior','--hangar-component=shield-recharge','--hangar-system=Shield')}
)
foreach($case in $cases) {
    if($Only -and $case.Name -ne $Only) { continue }
    $log = (Join-Path $PSScriptRoot ($case.Name+'.log')) -replace '\\','/'
    $engineArgs = @('--path', ('"'+$projectRoot+'"'), '--log-file', ('"'+$log+'"'), '--fixed-fps','60','--') + $case.Args
    if($case.Name -notlike '*-flow' -and $case.Name -ne 'model-checks') {
        $png = (Join-Path $PSScriptRoot ($case.Name+'.png')) -replace '\\','/'
        $engineArgs += '--shot="'+$png+'"'
    }
    $process = Start-Process -FilePath $engine -ArgumentList $engineArgs -WindowStyle Hidden -PassThru
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while(-not $process.WaitForExit(1000)) {
        if($timer.Elapsed.TotalSeconds -gt 180) { Stop-Process -Id $process.Id -Force; throw "Timed out: $($case.Name)" }
    }
    Get-Content -LiteralPath $log -Encoding UTF8
    if($process.ExitCode -ne 0) { throw "Failed: $($case.Name)" }
}
