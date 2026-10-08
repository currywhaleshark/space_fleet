# C# 빌드와 변경된 리소스 임포트. 기존 .godot가 있어도 pull/export로 바뀐 GLB를 갱신한다.
. "$PSScriptRoot\godot.ps1"

$captureDir = Join-Path $ProjectDir 'shots'
New-Item -ItemType Directory -Force $captureDir | Out-Null
if (-not (Test-Path (Join-Path $captureDir '.gdignore'))) {
    New-Item -ItemType File -Path (Join-Path $captureDir '.gdignore') | Out-Null
}
& $GodotConsole --headless --path $ProjectDir --import
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build (Join-Path $ProjectDir 'SpaceFleet.csproj') -nologo -v q
exit $LASTEXITCODE
