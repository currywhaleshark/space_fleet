# 티어 설계 6단계 검증 — 2026-10-15

정찰함·강습함·상위 개량형 22종을 추가해 연구 트리의 34함급을 모두 연결했다. 구현 계약은 `../../DESIGN_progression.md` §17이다.

## 재현

저장소 루트, 기존 .NET SDK/GodotSharp 참조:

```powershell
dotnet build SpaceFleet.csproj --no-restore
dotnet build tests/SimChecks/SimChecks.csproj -c Release --no-restore
dotnet tests/SimChecks/bin/Release/net8.0/SimChecks.dll --tier-branches
dotnet tests/SimChecks/bin/Release/net8.0/SimChecks.dll
dotnet build tools/BattleBatch/BattleBatch.csproj -c Release --no-restore
dotnet tools/BattleBatch/bin/Release/net8.0/BattleBatch.dll --branch-trials --output <output>/branch-trials.json
dotnet tools/BattleBatch/bin/Release/net8.0/BattleBatch.dll --blue-design Earth --red-design Mars --blue-wing earth_recon_t3 --red-wing mars_assault_t3 --seeds 1 --mirror --output <output>/branches.csv
dotnet tools/BattleBatch/bin/Release/net8.0/BattleBatch.dll --blue-wing earth_recon_t3 --red-wing earth_recon_t3 --seeds 1 --mirror --output <output>/recon-symmetric.csv
dotnet tools/BattleBatch/bin/Release/net8.0/BattleBatch.dll --blue-design Mars --red-design Mars --blue-wing mars_assault_t3 --red-wing mars_assault_t3 --seeds 1 --mirror --output <output>/assault-symmetric.csv
dotnet tools/BattleBatch/bin/Release/net8.0/BattleBatch.dll --tier-hull earth_recon_t5 --seeds 1 --output <output>/tier5.csv
```

`--blue-wing`/`--red-wing`는 기존 24척 편성의 소형 5척만 해당 함급으로 바꾼다. 중·대형은 각 진영의 기존 T2 호위함/T4 전함이다. `--tier-hull`은 10:10 참가자, 실제 티어 매칭 풀과 장비를 사용한다. 원자료 재현 시 이 폴더 대신 새 출력 경로를 사용한다.

Godot 4.7.2 .NET 실제 Vulkan 렌더러:

```text
--path <repo> --fixed-fps 60 -- --model-test
--path <repo> --fixed-fps 60 -- --progression-test --seed=1
--path <repo> --fixed-fps 60 -- --hangar-test --seed=1
--path <repo> --fixed-fps 60 -- --match-flow-test --seed=1
--path <repo> --fixed-fps 60 -- --flow-test --seed=1
--path <repo> --fixed-fps 60 -- --menu=research --hangar-hull=earth_recon_t5 --frames=30 --shot=<absolute-output.png>
--path <repo> --fixed-fps 60 -- --menu=research --hangar-hull=mars_assault_t5 --frames=120 --shot=<absolute-output.png>
--path <repo> --fixed-fps 60 -- --menu=hangar --hangar-hull=earth_recon_t5 --hangar-exterior --frames=30 --shot=<absolute-output.png>
--path <repo> --fixed-fps 60 -- --menu=hangar --hangar-hull=mars_assault_t5 --hangar-exterior --frames=30 --shot=<absolute-output.png>
```

화면/흐름 검사는 사용자 진행을 읽거나 저장하지 않는다. 연구 화면은 T1만 해금된 예시이며 격납고는 전체 보유 검사 프로필이다. 화성 연구 화면의 최초 30프레임 캡처에서 일부 글리프가 누락되어, 단독 실행 120프레임 이후 다시 촬영했고 정상 표시를 확인했다. 실제 흐름 검사는 양 갈래 T5까지 구매하고 플레이어 기체로 출격한다.

## 자동검사

- 전체 기능 **5,607,574개** 통과. 신규 갈래 224개, 연구 478개, 편대 799개 포함. 이 세 줄을 제외하면 5단계 전체 로그와 동일하다. 기존 정규 24척 전투 수치와 난전 목표 2/6 경고도 동일하다.
- 신규 검사는 22개 전체 함급의 좌표·연구·해금 직후 기본 편대 출격·티어/점수 대칭, 기존 v2 저장 확장과 RP/연구/백업 보존, 집결/공동 공격/직접 명령/탄약 소진원 복귀, 관측되지 않은 위치 변화 무시, 정찰 유지·회피·프로브 관측, 6개 정찰/강습 변형의 격리 실패·투기·재보급 차단을 포함한다.
- 실제 모델 **2,296개**: 기존 모델/소켓/실제 포탑 추적 검사와 모든 새 변형의 모듈·PD 연결. 연구 흐름 30개, 격납고 22개, 매칭·관전 30개, 기존 장면 51개. 합계 **2,429개**. 모든 종료 코드 0, 렌더 오류 로그 비어 있음.
- 게임 Debug, SimChecks/BattleBatch Release 빌드 경고·오류 0. `git diff --check` 통과(기존 작업 파일 CRLF 정규화 안내만 출력).

## 역할 및 통합 전투 측정

`branch-trials.json`은 아군 센서 기준점, 분산된 소형 5척과 42km 거리의 무장 없는 정지 전함을 놓고 150초 실행한다. 전함 실드는 매우 크게 설정해 여러 회 공격을 관측한다. 상대 방공 돌파나 함대 승률 시험이 아니다.

| 함급 | 첫 강습 | 집결 시간 | 최소 표적 거리 | 프로브 전개 | AM 명중/발사 |
|---|---:|---:|---:|---:|---:|
| 지구 T3 요격함 | 0초 | 0초 | 0.40km | 0 | 8/11 |
| 지구 T3 정찰함 | 없음 | 0초 | 26.20km | 5 | 0/0 |
| 화성 T3 요격함 | 0초 | 0초 | 0.69km | 0 | 13/13 |
| 화성 T3 강습함 | 39초 | 39초 | 0.71km | 0 | 5/5 |
| 지구 T5 정찰함 | 없음 | 0초 | 26.20km | 5 | 0/0 |
| 화성 T5 강습함 | 39초 | 39초 | 0.76km | 0 | 5/5 |

단독 시험은 아군 기준점이 이미 표적을 잠그므로 프로브의 새 접촉 기여가 0이다. 신규 접촉/식별 공유는 별도의 센서 파괴·전방 프로브 검사로 확인했다. 강습원 5척의 첫 강습 시각 편차는 0초다.

| 24척 편성 / 시드 1 | 시작 위치 정상 | 시작 위치 교환 |
|---|---|---|
| Blue 지구 정찰 vs Red 화성 강습 | Red, 784.200초 | Red, 735.783초 |
| 지구 정찰 대칭 | Blue, 754.550초 | Red, 783.267초 |
| 화성 강습 대칭 | Blue 시간 제한 판정, 1,500초 | Blue, 695.433초 |

- 6판 모두 종료, 아군 충돌 0. 시간 제한 한 판을 섬멸 승리로 해석하지 않는다. 비대칭 2판에서 화성 우세가 유지됐다.
- 비대칭전 정찰 편대는 각 20개 프로브를 전개하고 자동 강습 0회. 화성 강습 5척은 각 판 613초/652초에 함께 강습을 시작했으나 **AM 발사 0회**로 끝났다. 나머지 대칭 시험과 T5 매칭에서도 AM 발사가 없었다. 진입/도달 조건은 후속 균형 과제다. 화성 대칭 정상 배치에서 격리 실패 2회가 기록되어 탑재 위험은 유지된다.
- T5 매칭전: 선택 함급 지구 T5 정찰함, 시드 1, **78척**, 874.8초 Red 승리, 28척 격침·1척 무력화, 아군 충돌 0. 혼성 진영 양팀이므로 Red 승리를 화성 승리로 해석하지 않는다. 실제 모델 렌더 없는 자동 전투다.
- 함급별 생존시간·피해 기여·명중·프로브·최초 행동/강습은 각 `*.ships.csv`, 프로브 비행은 `*.probes.csv`, AM은 `*.am.csv`에 남긴다. `summary.json`은 주요 결과를 모은 것이다.

## 보존과 한계

- 5단계의 기존 함체/변형/물리 장비·T1 모델·manifest 해시를 비교하고, 원본 6종 Blender/GLB는 HEAD와 비교한다. 이전 단계 원자료도 해시로 확인한다. 상세 목록은 `manifest.json`.
- 새 변형의 전용 외형, 승률/경제 최종 조정, 기존 화성 우세·지구 접근/잠금·난전 빈도 해결, 부유포대/드론모함, 배포 EXE는 이번 결과에 포함하지 않는다.
- 검증 일부를 동시에 실행했으므로 wall time을 성능 결과로 사용하지 않는다. 새 함급의 FPS/틱 비용 개선이나 240척 성능 합격을 주장하지 않는다.
