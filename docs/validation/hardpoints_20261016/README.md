# 하드포인트·주포 분리 — 2026-10-16

`DESIGN_progression.md` §7.7.2/§18의 구조 선행 작업이다. 기존 34함급의 무장·함체 데이터, 모델과 전투 성능을 보존하고, 새 소켓/무장 정의 및 선택을 시뮬레이션에 연결했다.

## 변경 범위

- 선체 소유 소켓/피벗/하우징/구동 한계와 공통 무장/포신 수/탄도/비용을 분리했다. 포구 위치와 탄창은 소켓별 설치 데이터다. 같은 함선의 주포 계통·포신 수는 통일한다.
- 기존 `Railgun` JSON을 계속 읽고 쓴다. 새 `MainBattery` 입력은 로딩 때 기존 실행용 포탑/주포 뷰로 변환한다. 기본 무장의 명시 선택도 기존 JSON과 세팅 해시를 보존한다.
- 선택 API는 `ShipLoadout.MainWeapon`과 `ShipVariants.Resolve(..., mainWeapon)`이다. 질량/무장 전력은 설치 수만큼 교체 전후 차액을 반영하며, 기존 연구의 절대 탄도 패치와 베이 장비가 이후 합성된다. 재파싱은 비용을 중복 부과하지 않는다.
- 특대 고정 축포의 포각 0을 지원한다. 함체 전방의 로컬 좌표 변환 오차로 발사가 막히는 사례를 재현해 1e-6 rad 허용치로 보완했다. 현재 34함급의 포각은 이 값보다 크므로 기존 제한은 같다.

## 검증 내용

| 항목 | 결과 |
|---|---|
| 전체 SimChecks | 60묶음 / 5,675,168개 통과 |
| 신규 하드포인트 검사 | 67,127개 통과 |
| 기존 회귀 출력 | 신규 PASS 한 줄을 제외한 전체 출력 동일 |
| 기존 함급 정의·세팅 ID | 34/34 동일 |
| 실제 렌더러 모델 검사 | 2,484개 통과 |
| 게임 빌드 | 경고 0 / 오류 0 |

`verification.json`은 보존 로그/정의의 자동 대조 결과다. `verify.py`는 기존 전체 회귀 출력에서 새 검사 묶음 한 줄만 제외한 뒤 완전히 같아야 통과한다. 전투 시간·명중·격침 등 결과를 별도로 무시하지 않는다.

- `before-definitions.json` / `after-definitions.json`: 34함급의 `ToJson()` 문자열과 기본 `LoadoutId`. 변경 전 검사 DLL을 별도 폴더에 보존하고 동일한 `snapshot-source.cs.txt` 프로그램으로 전후 수집했다.
- `MainBatteryChecks`: 34함급을 새 형식으로 변환해 120틱 선회/포구 좌표와 480틱 실제 탄환·탄약·재장전·열을 직접 대조한다. 큰 좌표(1e12), 단장/3연장 실제 일제사격, 비용/가속/연구/장비 합성, 선택 순서 정규화, 모듈 파괴, 무장 제거, 고정 축포 24자세를 검사한다.
- 잘못된 크기·중복 소켓·누락 모듈·포신 수·탄창 합산 오버플로·잘못된 포구·과도한 비용·미구현 레이저·두 형식 동시 저작 등을 거부한다.
- 전체 SimChecks 로그는 `before-sim-checks.log`와 `after-sim-checks.log`, 게임 빌드는 `game-build.log`, 실제 렌더러의 모델/리그 검사 2,484개는 `model-checks.log`다.
- `model-checks-headless.log` / `godot-model-engine.log`는 실패한 헤드리스 실행 기록이다. 실제 렌더러의 MultiMesh 변환을 검사하므로 해당 환경에서 드론 메시 제거 확인이 실패했다. 같은 게임 코드로 Vulkan Forward+를 사용한 숨긴 검증 창에서 모델 검사를 통과했다. 검사 조건을 완화하거나 게임 전투 수치를 바꾸지 않았다.

## 재현

```powershell
dotnet build tests/SimChecks/SimChecks.csproj -c Release --no-restore
dotnet tests/SimChecks/bin/Release/net8.0/SimChecks.dll --hardpoints
dotnet tests/SimChecks/bin/Release/net8.0/SimChecks.dll
dotnet build SpaceFleet.csproj --no-restore
python docs/validation/hardpoints_20261016/verify.py
```

모델 검사는 Godot 4.7.2 .NET의 실제 렌더러에서 `--path <repo> --fixed-fps 60 -- --model-test`로 실행한다. 이번 검사는 `APPDATA`를 `shots/hardpoint-model-user/`로 지정해 플레이어의 실제 진행 파일과 분리했다. 샌드박스 내부 엔진 시작 지연과 로그 경로 오류가 있어 검증용 실행 환경을 조정했으며, 최종 프로세스 종료 코드와 PASS를 함께 확인했다.

전후 스냅샷 프로그램은 별도의 .NET 8 콘솔 프로젝트에서 해당 버전 `SimChecks.dll`과 `GodotSharp.dll`을 참조해 빌드한 뒤 출력 JSON 경로를 인자로 받는다. 저장한 변경 전 JSON은 과거 상태의 증거이고, 현재 소스 재빌드로 과거 바이너리가 복원되는 것은 아니다.

## 남은 범위

실제 교체 무장 카탈로그·연구 가격·격납고 선택/진행 저장·포탑 메시 교환·레이저·부유포대 함급은 후속이다. 시험용 단장/3연장 및 축포를 플레이어 장비로 노출하지 않았다. 기존 주포의 별도 계상 비용은 0이므로 실제 카탈로그 제작 때 기본 무장의 질량/전력도 함께 정해야 한다. 계약과 한계는 설계 문서 §18에 있다.

새 작업은 정의 로딩 시 수행하며 새로운 매 틱 탐색·탄체 종류를 추가하지 않았다. 이번 검사만으로 FPS 개선이나 새 무장 밸런스를 주장하지 않는다. 배포 실행파일·커밋·푸시는 갱신하지 않았다.
