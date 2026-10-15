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
- 실행본: `builds/SpaceFleet-20261015-am-structure-Windows-x64/SpaceFleet.exe`와 같은 이름의 ZIP. 독립 .NET 런타임 및 `SpaceFleet.Sim.dll` 포함. 기본 출격은 10 대 10 편대 AI 매칭(총 76~84척), Tab 명단, 내 편대 내 인계·전멸·관전·메인 복귀. 선택 함체는 `user://fleet-choice.cfg`에 저장. 기존 24척 검증은 `--battle`, 새 편성 검증은 `--match --seed=1`; `--match-flow-test`는 실제 렌더 환경에서 실행한다(헤드리스는 마우스 캡처 미지원). 기능 SimChecks 5,594,867개, 배포 검사 13묶음/2,373개 통과. 별도 `--brawl-balance`의 과거 난전 빈도 목표는 현재 미달(2/6)이므로 기능 통과와 구분한다. 매칭 원자료: `docs/validation/matchmaking_20261015/`.
- 반물질 구조 보너스: 양쪽 요격함 `antimatter.structuralDamageMultiplier=2`. 관통 경로·감쇠·기본 상한 적용 뒤 구조 피해만 2배, 일반탄 기본 1배. 모듈 피해·실드·장갑·비행 성능 유지. 실제 발사 1배/2배 비교 및 만료/요격 무해함 검증: AM 271·구조 52개, `docs/validation/am_structure_20261015/`. 아래 구조 피해 8판 통계는 이 보너스 적용 전이다.
- 구조 내구도: 실드·입구 장갑을 뚫은 실제 선체 경로에 영구 피해 누적. 파괴된 구획에도 적용하며 응급복구로 회복하지 않음. 지구 BB/DD/IC 7200/2700/720, 화성 5400/2100/540. 0이면 기존 두 조각 잔해, 좌하단 구조 % 표시. 잔해 피격의 반복 격침 알림도 수정. 같은 8판 평균 격침 15.625→18, 무력화 합계 77→10, 복구 806→525. 원자료: `docs/balance/structure_20261015/`. 수치 조정에 사용한 소표본으로 최종 승률 균형을 주장하지 않는다.
- 핵심부 유폭: 탄약고 28~35%, 반응로 12~20%, 함종별 에너지 문턱. 응급복구 후 재파괴 위험 1.5배, 두 번 이상 복구 시 최대 2배. 일반 모듈·AM·승패 규칙 유지. 같은 매칭 시드 1~4/미러 8판씩 비교에서 평균 격침 10.75→15.625척. 수치·소표본 한계: `docs/BALANCE_stage7.md`, 증빙: `docs/balance/lethality_20261015/`. 배포 검사에서 드러난 입력 검사의 사망 표적 재사용과 잔해 메시 배열 수명도 보완했다.
- 소행성 명암/요철 변경은 `docs/ENVIRONMENT_stage2.md` 마지막 절 참고. 이전 분할 경로 탐색은 사용자 허용을 받은 결과 변경이다. 240전/추가 시드 대조·규모별 부하·FPS·해시는 `docs/validation/navigation_sliced_20261012/verification.json`과 `evidence.zip`; 성능 해석과 남은 지연은 `docs/BALANCE_perf.md`.
