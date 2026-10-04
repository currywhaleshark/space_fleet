# Godot 실행 파일 경로를 한 곳에서 관리한다. 다른 스크립트가 dot-source로 불러 쓴다.
$GodotDir = Join-Path $env:LOCALAPPDATA 'Programs\Godot\Godot_v4.7.2-stable_mono_win64'
$Godot = Join-Path $GodotDir 'Godot_v4.7.2-stable_mono_win64.exe'
$GodotConsole = Join-Path $GodotDir 'Godot_v4.7.2-stable_mono_win64_console.exe'
$ProjectDir = Split-Path -Parent $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:PATH = "$env:ProgramFiles\dotnet;$env:PATH"
}
if (-not (Test-Path $GodotConsole)) {
    throw "Godot not found: $GodotConsole"
}
