# Space Fleet — 작업 메모

- 빌드: `.\tools\build.ps1` (PowerShell). 셸 PATH에 dotnet이 없으면 `tools/godot.ps1`이 보정한다.
- Debug에서도 240Hz 수치 계산은 별도 `SpaceFleet.Sim` 어셈블리로 최적화한다. 게임·입력·렌더 코드는 기존 Debug, 두 어셈블리의 portable PDB는 유지한다. 계산부 로컬 변수를 최적화 없이 추적하려면 `dotnet build SpaceFleet.csproj -p:SimOptimize=false`, 정상 성능으로 돌아오려면 `-p:SimOptimize=true`로 다시 빌드한다. `--perf-trace` 첫 줄에 실제 두 어셈블리의 JIT 최적화 상태가 기록된다.
- 화면 확인: `.\tools\shot.ps1 <이름> [게임 인자]` → `shots/<이름>.png`를 읽어서 본다. 첫 실행은 셰이더 컴파일로 느리다.
- 게임 로그: `%APPDATA%\Godot\app_userdata\Space Fleet\logs\godot.log` (C# 예외는 여기 찍힌다).
- `src/Sim`에는 Godot 노드·씬 의존을 넣지 않는다(나중에 서버에서 이 층만 돌린다). Godot 수학 타입(Vector3, Quaternion)은 허용.
- 시뮬레이션 위치는 `Vec3d`(double). 렌더할 때만 `RenderOrigin`을 빼서 float로 내린다.
- 카메라 Far는 1,000 km. 더 멀리 키우면 라이트 컬링이 깨진다. 먼 천체는 `ScaleTest.PlaceBackdrop`처럼 당겨 그린다.
- 함선 로컬 축: 전방 -Z, 위 +Y, 우 +X. `MeshKit.AddTaper`는 시계 방향 앞면 기준으로 감기 순서를 맞춘다.
- 계획·진행 상황: `docs/PLAN.md`.
- 실행본: `builds/SpaceFleet-20261012-defense-scaling-Windows-x64/SpaceFleet.exe`와 같은 이름의 ZIP. 독립 .NET 런타임 및 `SpaceFleet.Sim.dll` 포함. 배포 EXE 2,286개 검사 통과. 240전 대조·규모별 부하·FPS·해시는 `docs/validation/defense_ai_scaling_20261012/verification.json`과 `evidence.zip`; 성능 해석은 `docs/BALANCE_perf.md`.
