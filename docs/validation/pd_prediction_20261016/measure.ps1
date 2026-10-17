$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$build = Join-Path $repo 'tools/ScaleBench/bin/Release/net8.0'
$runners = Join-Path $repo 'shots/pd-perf-runners'
# Both assemblies have the identity ScaleBench. A shared deps.json beside the
# current build can resolve the renamed baseline to the current DLL. Keep each
# version in its own directory, under its original assembly file name.
foreach ($version in @('before', 'after')) {
    $runner = Join-Path $runners $version
    New-Item -ItemType Directory -Force -Path $runner | Out-Null
    foreach ($name in @('GodotSharp.dll', 'ScaleBench.deps.json', 'ScaleBench.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $build $name) -Destination (Join-Path $runner $name) -Force
    }
    $assembly = if ($version -eq 'before') { 'ScaleBench.before.dll' } else { 'ScaleBench.dll' }
    Copy-Item -LiteralPath (Join-Path $build $assembly) -Destination (Join-Path $runner 'ScaleBench.dll') -Force
}
Push-Location $repo
try {
    foreach ($field in @('DeepSpace', 'AsteroidCluster')) {
        for ($repeat = 1; $repeat -le 3; $repeat++) {
            $order = if ($repeat % 2 -eq 1) { @('before', 'after') } else { @('after', 'before') }
            foreach ($version in $order) {
                $runner = Join-Path $runners $version
                $output = Join-Path $PSScriptRoot "$field-$version-$repeat.json"
                Write-Output "Measuring $field $version $repeat"
                & dotnet "$runner/ScaleBench.dll" --groups 10 --warmup 15 --seconds 30 --repeats 1 --battlefield $field --field-seed 2 --output $output
                if ($LASTEXITCODE -ne 0) { throw "Benchmark failed: $field $version $repeat" }
                $row = @(Get-Content -LiteralPath $output -Raw | ConvertFrom-Json)[0]
                $hasSolutions = $null -ne $row.PSObject.Properties['pdSolutions']
                if ($hasSolutions -ne ($version -eq 'after')) { throw "Wrong assembly loaded: $version" }
            }
        }
    }
} finally { Pop-Location }
