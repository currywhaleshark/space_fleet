param([int[]]$Seeds=(1..4), [int[]]$DuelSeeds=(1..2), [string]$Output='shots/mars-balance', [switch]$Duels)
. "$PSScriptRoot\godot.ps1"
New-Item -ItemType Directory -Force $Output | Out-Null
dotnet build (Join-Path $PSScriptRoot 'BattleBatch/BattleBatch.csproj') -c Release -nologo -v q
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
$runner=Join-Path $PSScriptRoot 'BattleBatch/bin/Release/net8.0/BattleBatch.dll'
foreach($pair in @(@('Earth','Earth'),@('Mars','Mars'),@('Earth','Mars'),@('Mars','Earth'))) {
    $name="$($pair[0])-$($pair[1])"
    & dotnet $runner --seeds ($Seeds -join ',') --mirror --jobs 2 --minutes 25 --blue-design $pair[0] --red-design $pair[1] --output "$Output/$name.csv" *> "$Output/$name.log"
    if($LASTEXITCODE -ne 0){throw "Fleet matrix failed: $name"}
}
if($Duels) {
    foreach($kind in @('Battleship','Escort','Interceptor')) {
        $ranges=if($kind -eq 'Interceptor') {@(4000,12000,30000)} elseif($kind -eq 'Escort') {@(12000,45000,80000)} else {@(20000,70000,130000)}
        foreach($range in $ranges) {
            foreach($pair in @(@('Earth','Earth'),@('Mars','Mars'),@('Earth','Mars'),@('Mars','Earth'))) {
                $name="duel-$kind-$range-$($pair[0])-$($pair[1])"
                & dotnet $runner --seeds ($DuelSeeds -join ',') --mirror --jobs 2 --minutes 25 --duel $kind --distance $range --blue-design $pair[0] --red-design $pair[1] --output "$Output/$name.csv" *> "$Output/$name.log"
                if($LASTEXITCODE -ne 0){throw "Duel matrix failed: $name"}
            }
        }
    }
}
