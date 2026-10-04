# 빌드 후 게임 실행. 인자는 그대로 게임에 넘긴다(예: .\tools\run.ps1 --control=BB-01).
. "$PSScriptRoot\godot.ps1"

& "$PSScriptRoot\build.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($args.Count -gt 0) {
    & $GodotConsole --path $ProjectDir -- @args
} else {
    & $Godot --path $ProjectDir
}
