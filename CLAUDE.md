# Space Fleet — 작업 메모

- 빌드: `.\tools\build.ps1` (PowerShell). 셸 PATH에 dotnet이 없으면 `tools/godot.ps1`이 보정한다.
- 화면 확인: `.\tools\shot.ps1 <이름> [게임 인자]` → `shots/<이름>.png`를 읽어서 본다. 첫 실행은 셰이더 컴파일로 느리다.
- 게임 로그: `%APPDATA%\Godot\app_userdata\Space Fleet\logs\godot.log` (C# 예외는 여기 찍힌다).
- `src/Sim`에는 Godot 노드·씬 의존을 넣지 않는다(나중에 서버에서 이 층만 돌린다). Godot 수학 타입(Vector3, Quaternion)은 허용.
- 시뮬레이션 위치는 `Vec3d`(double). 렌더할 때만 `RenderOrigin`을 빼서 float로 내린다.
- 카메라 Far는 1,000 km. 더 멀리 키우면 라이트 컬링이 깨진다. 먼 천체는 `ScaleTest.PlaceBackdrop`처럼 당겨 그린다.
- 함선 로컬 축: 전방 -Z, 위 +Y, 우 +X. `MeshKit.AddTaper`는 시계 방향 앞면 기준으로 감기 순서를 맞춘다.
- 계획·진행 상황: `docs/PLAN.md`.
