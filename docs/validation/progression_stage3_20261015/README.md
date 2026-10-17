# 티어 설계 3단계 검증 — 2026-10-15

기존 6종 이름 변경, T3 순양함 노드 4종, 갈래별 AI와 기존 무장·모델 연결. 결과 설명은 `../../DESIGN_progression.md` §14에 있다. `manifest.json`은 소스 SHA-256, 기존 함체 전투 데이터 보존, 전체 검사 수와 함대전 집계를 담는다.

## 재현

저장소 루트, .NET 8 이상과 기존 GodotSharp 참조가 있는 환경에서 실행한다.

```powershell
dotnet build tests/SimChecks/SimChecks.csproj -c Release --no-restore
dotnet tests/SimChecks/bin/Release/net8.0/SimChecks.dll --cruisers
dotnet tests/SimChecks/bin/Release/net8.0/SimChecks.dll
dotnet build tools/BattleBatch/BattleBatch.csproj -c Release --no-restore
dotnet tools/BattleBatch/bin/Release/net8.0/BattleBatch.dll --cruiser-trials --output shots/role-trials.json
dotnet tools/BattleBatch/bin/Release/net8.0/BattleBatch.dll --blue-design Earth --red-design Mars --seeds 1,2 --mirror --minutes 25 --jobs 2 --output shots/base-vs-base.csv
dotnet tools/BattleBatch/bin/Release/net8.0/BattleBatch.dll --blue-design Earth --red-design Mars --blue-escort earth_gun_cruiser --red-escort mars_gun_cruiser --seeds 1,2 --mirror --minutes 25 --jobs 2 --output shots/gun-vs-gun.csv
dotnet tools/BattleBatch/bin/Release/net8.0/BattleBatch.dll --blue-design Earth --red-design Mars --blue-escort earth_air_defense_cruiser --red-escort mars_missile_cruiser --seeds 1,2 --mirror --minutes 25 --jobs 2 --output shots/aa-vs-missile.csv
dotnet build SpaceFleet.csproj --no-restore
```

Godot 4.7.2 .NET 실행 인자(실제 렌더러, 헤드리스 아님):

```text
--path <repo> --fixed-fps 60 -- --model-test --seed=1
--path <repo> --fixed-fps 60 -- --flow-test --seed=1
--path <repo> --fixed-fps 60 -- --match-flow-test --seed=1
--path <repo> --fixed-fps 60 -- --fx-preview=rail --fx-ship=earth_air_defense_cruiser --shot=<absolute-path.png>
--path <repo> --fixed-fps 60 -- --menu=select --shot=<absolute-path.png>
--path <repo> --fixed-fps 60 -- --match --seed=1 --roster-preview --frames=160 --shot=<absolute-path.png>
```

## 결과·제한

- `sim-checks.log`: 5,606,073개 통과. 신규 116개 검사 한 줄을 제외하면 2단계 전체 로그와 동일하다. 기존 난전 빈도 목표 2/6 미달은 유지된다.
- `model-test.log`, `flow-test.log`, `match-flow-test.log`: 각각 1,475 / 51 / 30개 통과. 모든 빌드 경고·오류 0.
- `role-trials.json`: 고정 표적/접근 파형의 실제 발사·명중·요격 수와 시험 세팅 해시. 주포 시험은 16km·60초, 미사일은 25km·120초, 표적의 무장·PD·ECM·디코이를 제거하고 큰 실드를 부여했다. 방공은 2km/s 관성 접근 미사일 12발·35초, 보호 전함의 자체 PD·드론은 없다. `CounterKills`는 요격 미사일 격추만, `Intercepts`는 PD·드론을 포함한 총계다. 시러스 총 12회 = 요격탄 1 + PD 9 + 드론 2.
- `*.csv`, `*.ships.csv`, `*.summaries.json`, `*.log`: 24척 전장, 조건별 시드 2개 × 위치 미러 2개. 호위 6척을 갈래로 바꾸며 전함·요격함은 해당 진영 기본형이다. `mirror`는 시작 위치 교환이며 Blue/Red 설계는 바꾸지 않는다.
- 세 조건 모두 화성 4/4승. 기본 함대에도 있던 우세와 신규 갈래의 접근/잠금/방공 기여 문제를 기록했으며, 이 소표본으로 균형이 검증됐다고 보지 않는다. 병렬 실행의 벽시계 시간은 성능 비교 근거로 쓰지 않는다.
- 그림 `class-names.png`, `class-roster.png`에서 새 함급 이름이 잘리지 않음을 확인했다. `cirrus-cruiser.png`는 기존 호위 모델을 이용한 시러스급 발사 장면이다.
- 기본 메뉴·무작위 매칭 후보는 기존 6종이다. 명시적 변형 ID로 시뮬레이션/모델을 생성할 수 있으며 플레이어 세팅·출격 UI·저장은 4단계다. 배포 EXE는 갱신하지 않았다.
