# C# 빌드. 처음 한 번은 Godot 임포트(.godot 폴더 생성)도 같이 돌린다.
. "$PSScriptRoot\godot.ps1"

if (-not (Test-Path (Join-Path $ProjectDir '.godot'))) {
    & $GodotConsole --headless --path $ProjectDir --import
}
dotnet build (Join-Path $ProjectDir 'SpaceFleet.csproj') -nologo -v q
exit $LASTEXITCODE
