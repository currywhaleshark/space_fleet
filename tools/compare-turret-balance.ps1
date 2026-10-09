# Isolated 2x2 comparison: baseline, twin guns only, PD tracking only, current.
param(
    [string]$BaseRef = 'e9b2765',
    [string]$OutputDirectory = 'shots/turret-balance-20261008',
    [string]$Seeds = '0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19'
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\godot.ps1"
$root = [IO.Path]::GetFullPath((Join-Path $ProjectDir $OutputDirectory))
if (Test-Path -LiteralPath $root) { throw "Use a fresh output directory: $root" }
$commit = (& git -C $ProjectDir rev-parse --verify "${BaseRef}^{commit}").Trim()
if ($LASTEXITCODE -ne 0) { throw 'Invalid baseline ref' }
New-Item -ItemType Directory -Path $root | Out-Null
$zip = Join-Path $root 'baseline-source.zip'
& git -C $ProjectDir archive --format=zip --output=$zip $commit src/Sim data/ships
if ($LASTEXITCODE -ne 0) { throw 'Baseline snapshot failed' }
$baseline = Join-Path $root 'baseline'
Expand-Archive -LiteralPath $zip -DestinationPath $baseline
function Copy-Sources([string]$source, [string]$destination) {
    New-Item -ItemType Directory -Path (Join-Path $destination 'src'), (Join-Path $destination 'data') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $source 'src/Sim') -Destination (Join-Path $destination 'src/Sim') -Recurse
    Copy-Item -LiteralPath (Join-Path $source 'data/ships') -Destination (Join-Path $destination 'data/ships') -Recurse
}
Copy-Sources $ProjectDir (Join-Path $root 'current')
Copy-Sources $baseline (Join-Path $root 'guns_only')
Copy-Sources $ProjectDir (Join-Path $root 'pd_only')
foreach ($file in @('Railgun.cs','SimBallistics.cs','Gunnery.cs')) {
    Copy-Item -LiteralPath (Join-Path $ProjectDir "src/Sim/$file") -Destination (Join-Path $root "guns_only/src/Sim/$file")
    Copy-Item -LiteralPath (Join-Path $baseline "src/Sim/$file") -Destination (Join-Path $root "pd_only/src/Sim/$file")
}
foreach ($ship in @('battleship','escort')) {
    Copy-Item -LiteralPath (Join-Path $ProjectDir "data/ships/$ship.json") -Destination (Join-Path $root "guns_only/data/ships/$ship.json")
    $path = Join-Path $root "pd_only/data/ships/$ship.json"
    $data = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    if ($ship -eq 'battleship') { $data = $data.Replace('"rounds": 720','"rounds": 360').Replace('"rounds": 240','"rounds": 120') }
    else { $data = $data.Replace('"rounds": 640','"rounds": 320').Replace('"rounds": 320 }','"rounds": 160 }') }
    [IO.File]::WriteAllText($path, $data)
}
$godotSharp = [Security.SecurityElement]::Escape((Join-Path $ProjectDir '.godot/mono/temp/bin/Debug/GodotSharp.dll'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup>
    <Reference Include="GodotSharp"><HintPath>$godotSharp</HintPath></Reference>
    <EmbeddedResource Include="data/ships/*.json" LogicalName="SpaceFleet.data.ships.%(Filename)%(Extension)" />
  </ItemGroup>
</Project>
"@
$names = @('baseline','guns_only','pd_only','current')
$hashes = foreach ($name in $names) {
    $path = Join-Path $root $name
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BattleBatch/Program.cs') -Destination (Join-Path $path 'Program.cs')
    [IO.File]::WriteAllText((Join-Path $path 'Balance.csproj'), $project)
    & dotnet build (Join-Path $path 'Balance.csproj') --configuration Release --nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $name" }
    Get-ChildItem -LiteralPath (Join-Path $path 'src/Sim'), (Join-Path $path 'data/ships'), (Join-Path $path 'Program.cs') -File | ForEach-Object {
        [pscustomobject]@{ Variant=$name; File=[IO.Path]::GetRelativePath($path,$_.FullName); Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
}
$hashes | Export-Csv -LiteralPath (Join-Path $root 'source-hashes.csv') -NoTypeInformation -Encoding UTF8
$runs = foreach ($name in $names) {
    $path = Join-Path $root $name
    $dll = Join-Path $path 'bin/Release/net8.0/Balance.dll'
    $csv = Join-Path $root "$name.csv"
    $proc = Start-Process -FilePath (Get-Command dotnet).Source -WindowStyle Hidden -PassThru -WorkingDirectory $ProjectDir `
        -ArgumentList @(('"'+$dll+'"'),'--seeds',$Seeds,'--mirror','--minutes','25','--jobs','1','--output',('"'+$csv+'"')) `
        -RedirectStandardOutput (Join-Path $root "$name.log") -RedirectStandardError (Join-Path $root "$name.err.log")
    [pscustomobject]@{ Variant=$name; ProcessId=$proc.Id; Csv=$csv }
}
[pscustomobject]@{ Baseline=$commit; Seeds=$Seeds; Mirror=$true; LimitMinutes=25; Runs=@($runs) } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'manifest.json') -Encoding UTF8
$runs | Format-Table -AutoSize
