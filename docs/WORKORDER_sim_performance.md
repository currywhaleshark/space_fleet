# 작업지시서: 시뮬레이션 성능 최적화

작성 2026-10-10 · 기준: 이 지시서가 들어간 커밋 · 대상 저장소 `space_fleet/`

## 0. 목표 한 줄

> 시뮬레이션 한 틱(`SimWorld.Step`)을 7단계 수준(Release 평균 0.17 ms)으로 되돌려, 개발 실행(Debug)에서도 ×4 배속 전면전이 끊기지 않게 한다.
> **전투 결과는 바꾸지 않는다.** 결과가 바뀌는 최적화는 따로 표시하고, 바뀐 폭을 숫자로 보인다.

배속 예산 제한(프레임당 시뮬레이션 시간을 묶어 배속을 낮추는 방식)은 하지 않는다. 증상만 가리는 미봉책이다.

---

## 1. 지금 무슨 일이 일어나나

### 1.1 증상

| 같은 전투(시드 1, 전 함선 AI, ×4 자동 관전) | 평균 FPS | 10분 무렵 순간 FPS | 판정 무렵 순간 FPS |
|---|---:|---:|---:|
| 개발 실행 `tools/run.ps1` (Debug 빌드) | 47 | **6** | **6** |
| 내보낸 실행본 `builds/SpaceFleet-20261010-fleet-audio-…` (Release) | 120 | 44 | 91 |
| 7단계 기록(`docs/BALANCE_stage7.md`, 개발 실행) | 83 | 76 | 76 |

같은 Debug라도 ×1 배속은 40~48 FPS로 버틴다.

### 1.2 원인 구조

1. **화면 쪽은 정상이다.** `--perf-trace` 측정(10분 무렵, Debug ×4)에서 한 프레임 약 180 ms 중 HUD 4 ms, 탄도 표시 1~10 ms, 함선 화면 동기 0.8 ms였다. 그리기 호출은 약 700, 노드는 약 3,350개다. **약 160 ms가 `World.Step`이다.**
2. **Debug 빌드는 시뮬레이션이 약 5배 느리다.** `BattleBatch`로 같은 전투(시드 1)를 쟀다.

   | 빌드 | 전체 평균 | 난전 평균 | 난전 p99 |
   |---|---:|---:|---:|
   | Release | 0.357 ms | 0.415 ms | 0.92 ms |
   | Debug | 1.944 ms | 2.280 ms | 4.39 ms |

3. **악순환.** ×4 배속은 물리 틱마다 `World.Step`을 4번 부른다(`ScaleTest._PhysicsProcess`). 프레임이 늦으면 Godot이 밀린 물리 틱을 한 프레임에 최대 8번까지 몰아서 실행한다(`physics/common/max_physics_steps_per_frame` 기본 8). 그래서 한 프레임에 최대 32번이 돌고, 느릴수록 더 많이 돈다.
4. **틱 비용이 7단계 이후 약 2배가 됐다.** Release 평균이 0.173 ms(7단계 기록)에서 0.357 ms가 됐다. 그 사이 근접방어 포탑, 방어 드론, 드론 교전, 실드 외피, 쌍열 포탑 등이 추가됐다. 이 증가가 Debug ×4를 악순환 문턱 너머로 밀었다.
5. **미해결 의문.** 게임 안의 Debug 틱(프레임당 sim 시간 ÷ 실행 횟수 ≈ 5~7 ms)이 헤드리스 Debug(2.3 ms)보다 약 3배 크다. 후보는 렌더 스레드와의 CPU 경합, GC, 게임에만 있는 상태 차이다. 2차에서 확인한다.

### 1.3 시스템별 틱 비용 (Release, µs/틱, 시드 1·3)

`src/Sim` 사본에 구간 시간 측정을 넣어 쟀다. 저장소 코드는 바꾸지 않았다. 재현 방법은 3.1에 있다. 괄호 안은 그 구간 안쪽의 세부 항목이다.

| 구간 | 0~5분 (접근) | 5~9분 (미사일) | 9분~ (포격·난전) | 위치 |
|---|---:|---:|---:|---|
| **합계** | 264~292 | 345~380 | 384~388 | `SimWorld.Step` |
| 근접방어 `StepPointDefense` | 58~69 | **119~135** | 66~70 | `SimMissiles.cs` |
| 함선 하위 틱 `ShipBody.Step` ×4 | **85~98** | 78~82 | 71~72 | `ShipBody.cs` |
| 충돌 `ShipCollision.Resolve` ×4 | 55~66 | 32~33 | 31~36 | `ShipCollision.cs` |
| 레일건 탄 `StepProjectiles` ×4 | 0 | 0~5 | **111~128** | `SimBallistics.cs` |
| AI `StepAI` | 29~39 | 60~64 | 65~66 | `SimAI.cs` |
| (그중 교전 `Engage`) | 6~12 | 36~39 | 41~43 | |
| (그중 조종 `Drive`) | 13~16 | 13 | 13 | |
| 미사일 갱신 | 8~10 | 28~31 | 8~14 | `SimMissiles.cs` |
| 방어 드론 `StepDefenseDrones` | 10~12 | 22~24 | 8~12 | `DefenseDrones.cs` |
| 센서·기록·판정 | 3~4 | 2~3 | 2~3 | |

- **할당**: 틱당 23~28 KB. 12분 전투에서 세대 0 GC 184회, 세대 1 2회, 세대 2 1회. 할당보다 계산량이 문제다.
- `×4`는 240 Hz 하위 틱이라 한 틱에 4번 도는 구간이다.

---

## 2. 작업 전에 읽을 것

- `CLAUDE.md`: `src/Sim`은 Godot 노드에 의존하지 않는다(나중에 서버에서 이 층만 돈다). 위치는 `Vec3d`.
- `docs/PLAN.md`: 마지막 항목 "포탑 추적 앞당김·전함 주포 선회·티저 폭발 연출 이식".
- `docs/BALANCE_stage7.md`: 배치 실행기와 기존 성능 기록.

### 2.1 작업 환경

- **셸**: PowerShell. dotnet이 PATH에 없으면 `. .\tools\godot.ps1`.
- **빌드**: `.\tools\build.ps1`.
- **검사**
  - 전체: `.\tools\check-sim.ps1`
  - 부분: `dotnet run --project tests/SimChecks -c Release -- --battle` (`--turrets`, `--tracking` 등)
- **배치 실행기**: `.\tools\battle-batch.ps1 -Seeds (1..10) -Mirror -Parallel -Jobs 6 -Label <이름> [-Profile]`. 결과는 `shots/batch/*.csv`이고, 콘솔에 요약과 틱 평균/p99가 나온다.
- **게임 안 프레임 측정**: `.\tools\run.ps1 --autoplay --seed=1 --advance=540 --perf-trace --perf-seconds=40`
  - 1초마다 FPS와 구간별 프레임당 평균/최대 ms를 찍는다(physics·sim·hudDraw·ballistics…).
  - `--autoplay-speed=1`로 배속을 바꾼다.
- **Release 게임 FPS**: 내보낸 실행본으로 잰다(`tools/export-windows.ps1`). 실행 파일은 창 프로그램이라 `Start-Process -Wait`로 기다리고 `--log-file`로 로그를 받는다.
- **커밋 메시지**: 스크래치 파일에 써서 `git commit -F`. 한글 파일은 bash heredoc으로 쓰지 않는다.

### 2.2 시작 전 확인

- 기준 커밋에는 포탑 추적 앞당김, `BattleFx`, `PerfTrace`, 난전 통계 검사가 들어 있다.
- 난전 발생은 대표 전투 한 판이 아니라 `BattleChecks.CheckBrawlStatistics`(시드 1~6 중 절반 이상)로 검사한다. 결과가 바뀌지 않았다는 판단은 **요약 문자열이 이전과 같은지**로 한다.

---

## 3. 단계별 작업

### 1차: 측정을 저장소에 넣는다

#### 3.1.1 시뮬레이션 구간 측정기 (`src/Sim/SimProfiler.cs`)

1.3 표를 만든 방법을 저장소에 정식으로 넣는다. Godot 의존 없이 `System.Diagnostics.Stopwatch`만 쓴다.

```csharp
public static class SimProfiler
{
    public static bool Enabled;                       // 꺼져 있으면 비용 0에 가깝게
    public static long Begin();                       // Enabled 아니면 0
    public static void End(SimSection section, long start);
    public static IReadOnlyDictionary<SimSection, (double TotalMs, long Calls)> Snapshot();
    public static void Reset();
}
public enum SimSection { AI, AICommand, AIThink, AIDrive, AIEngage, Gunnery, ShipStep, Collision, Projectiles, Missiles, PointDefense, Drones, Sensors, Post }
```

- **측정 지점**
  - `SimWorld.Step`: AI, 사격통제, 그리고 하위 틱 안의 함선 갱신·충돌·탄·탄약(미사일, 근접방어, 드론), 센서, 마무리.
  - `StepAI` 안: 지휘·판단·조종·교전.
- **배치 실행기**: `BattleBatch --profile`이 국면별로 위 구간의 µs/틱을 CSV와 콘솔에 함께 낸다.
- **게임**: `--perf-trace`가 켜지면 `SimProfiler`도 켜고, sim 구간을 세분해서 찍는다.
- **검사**: 측정기를 켜도 꺼도 같은 시드의 `BattleLog.Summary()`가 글자 하나까지 같다.

#### 3.1.2 기준값 저장

`docs/BALANCE_perf.md`를 새로 만들고 "작업 전"으로 남긴다.

- 시드 1~10 × 뒤집기의 Release µs/틱 표(1.3과 같은 형식)
- Debug 평균 틱
- 게임 Debug ×4 자동 관전 FPS(시작·10분·판정)
- 내보낸 실행본 FPS

### 2차: 결과가 바뀌지 않는 최적화

알고리즘과 판정은 그대로 두고, 같은 결과를 더 적은 계산으로 낸다.

**공통 검증**: 바꿀 때마다 시드 1~10 × 뒤집기, 총 20판의 `BattleLog.Summary()`가 작업 전과 **완전히 같아야** 한다. 다르면 3차로 넘긴다.

후보는 아래와 같다. 순서는 비용 크기 순이다. 각 항목은 먼저 측정으로 확인한 뒤 손댄다.

1. **근접방어** `StepPointDefense` (미사일전 120~135 µs)
   - 240 Hz 하위 틱마다 모든 함선 × 모든 포대 × (미사일 + 적 요격함 + 적 드론)을 돈다.
   - 적 요격함이 살아 있으면 미사일이 없어도 항상 돈다.
   - 포대 루프 안에 람다(`Clear`, `Candidate`)가 매번 만들어진다.
   - 후보: 함선 단위 거리 사전 거름(추적 사거리 × 배수 밖이면 포대 루프 생략), 람다 제거, 함선 회전 역행렬 한 번 계산.
2. **레일건 탄** `StepProjectiles` (난전 111~128 µs)
   - 하위 틱마다 탄 × 함선 상대 이동 검사를 한다. 드론 검사(`FirstDroneHit`)는 탄마다 한다.
   - 후보: 틱 전체 구간으로 함선 경계구 1차 거름 후, 걸린 함선만 하위 틱 정밀 검사.
3. **함선 하위 틱** `ShipBody.Step` ×4 (70~98 µs)
   - 후보: 피해·전력·포탑·탄약 갱신 안의 반복 계산과 할당 제거.
   - 이것들을 60 Hz로 옮기는 것은 결과가 바뀌므로 **3차**로 넘긴다.
4. **충돌** `ShipCollision.Resolve` ×4 (31~66 µs)
   - 하위 틱마다 24척 276쌍 BroadPhase + 겹침 보정 패스다.
   - 후보: 틱 시작에 "이번 틱 안에 닿을 수 있는 쌍" 목록(거리 < 반경 합 + 상대 속도 × 틱)을 만들고 그 쌍만 검사.
5. **AI 교전** `Engage` → `TryAutoFire` (36~43 µs)
   - 포탑마다 매 틱 `FireControl.Solve` + `PreviewArmor`(관통 미리보기 광선) + `RailLineLocal`(포신 가림 광선) + `FriendlyInLine`(아군 함선 전부에 광선, 포신마다)을 한다. 재장전 중에도 매 틱 돈다.
   - 후보: 조준 방향 계산만 매 틱 하고(포탑 추적에 필요), 관통·가림·아군 사선 검사는 장전 완료 + 정렬일 때만.
   - 일제사격 판단의 `_brains.Values.Count(...)` 같은 LINQ 반복을 줄인다.
   - **주의**: `gun.Aim()` 호출 시점과 값이 바뀌면 포탑 추적 앞당김(`RailgunState._aimRate`)의 결과가 달라진다. 조준 호출은 그대로 둔다.
6. **방어 드론·미사일 갱신**(각 10~30 µs): 같은 방식으로 측정 후 판단한다.
7. **게임 Debug 틱이 헤드리스 Debug의 약 3배인 이유**(1.2-5)를 확인한다. 게임에서 `SimProfiler`로 구간을 나눠 보면 어디서 차이가 나는지 보인다.

### 3차: 결과가 바뀌는 최적화 (필요할 때만, 따로 표시)

2차로 목표에 못 미치면 검토한다. 각 항목은 **별도 커밋**으로 하고 커밋 메시지에 "결과 변경"이라고 쓴다.

- **검증**: 시드 1~10 × 뒤집기 20판의 다음 지표를 작업 전과 나란히 표로 보인다.
  - 승률
  - 판정 시각 중앙값
  - 국면 비율(난전 60초 이상 판 수 포함)
  - 레일건·미사일 발사/명중 수
- 차이가 잡음 범위를 넘으면 사용자 결정을 받는다. 잡음 범위는 같은 코드에서 시드 11~20으로 돌린 차이를 기준으로 삼는다.

후보:
- 함선의 피해·전력·탄약 갱신을 240 Hz에서 60 Hz로. 운동·충돌만 하위 틱에 남긴다.
- 근접방어의 표적 선택은 60 Hz로 하고, 사격·명중 판정은 하위 틱에 둔다.

### 4차: 게임 쪽 확인

- 게임 Debug ×4 자동 관전에서 10분 무렵 순간 FPS 30 이상, 판정까지 평균 60 이상.
- 내보낸 실행본은 작업 전보다 나빠지지 않는다.
- 측정에는 `--perf-trace`를 쓴다. `max_physics_steps_per_frame`를 줄이는 등 악순환만 가리는 설정 변경은 하지 않는다. 바꿔야 한다고 판단되면 먼저 보고한다.

---

## 4. 유지할 것

- **결정성**: 같은 시드, 같은 입력이면 같은 결과다. `check-sim`의 결정성 검사가 계속 통과해야 한다.
- **층 분리**: `src/Sim`은 Godot 노드·씬에 의존하지 않는다. Godot 수학 타입은 허용한다.
- **판정 정밀도**
  - 240 Hz 연속 충돌·탄도 검사, double 좌표, 실드 외피·선체 장갑·모듈 관통 계산의 정확도를 낮추지 않는다.
  - 계산을 건너뛸 때는 "건너뛰어도 같은 결과"임을 경계 조건(거리 거름의 여유 등)으로 보장한다.
- **포탑 추적 앞당김과 그 검사**(`tests/SimChecks/TrackingChecks.cs`).

## 5. 이번에 하지 말 것

- 배속 예산 제한, 물리 틱 상한 축소처럼 증상만 가리는 방식
- 밸런스 수치 변경, AI 행동 변경
- 화면 쪽 최적화(이번 측정상 원인이 아니다). 단, 2차-7 확인 중 화면 쪽 원인이 드러나면 보고한다.
- 함선 수·무장 수를 줄이는 것

## 6. 완료 기준

- **Release 평균 틱 0.2 ms 이하**, 난전 p99 0.6 ms 이하. 시드 1~10 × 뒤집기 기준이다.
- **Debug 게임 ×4 자동 관전**(시드 1): 10분 무렵 순간 FPS 30 이상.
- **결과 불변**: 2차 변경 뒤 20판 요약이 작업 전과 완전히 같다. 3차 변경은 비교표가 있다.
- **검사**: `check-sim` 전부 통과. 장면 흐름(`tools/check-flow.ps1`)과 전투 효과(`--combat-visual-test`) 검사도 통과한다.
- **문서**: `docs/BALANCE_perf.md`에 작업 전후 표, `docs/PLAN.md`에 항목 추가.

## 7. 보고 형식 (차수마다)

- 바꾼 파일과 이유
- 구간별 µs/틱 작업 전후 표(1.3 형식)
- 결과 불변 확인 방법과 결과(20판 요약 일치 여부)
- 게임 FPS(Debug ×4, 내보낸 실행본)
- 손대지 못한 것과 이유
- 사람이 직접 해 봐야 아는 것(체감 끊김 등)은 "미확인"으로 표시한다.
