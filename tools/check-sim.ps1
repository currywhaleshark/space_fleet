# Godot 노드 없이 기동·충돌·피해·탄도·선행 조준·데이터를 검증한다. 먼저 build.ps1을 실행한다.
. "$PSScriptRoot\godot.ps1"
dotnet run --project (Join-Path $ProjectDir 'tests\SimChecks\SimChecks.csproj') --configuration Release --verbosity quiet
exit $LASTEXITCODE
