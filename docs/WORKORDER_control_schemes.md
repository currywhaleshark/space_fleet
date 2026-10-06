# 작업지시서: 함종별 조작 체계 분리 + 라디얼 명령 메뉴

작성 2026-10-06 · 기준 커밋 `44e08cc` · 대상 저장소 `space_fleet/`

## 0. 목표 한 줄

> 요격함은 조종사의 게임, 호위함은 전투지휘관의 게임, 전함은 함장의 게임.

| 함종 | 조함 | 사격 | 좌클릭 |
|---|---|---|---|
| 요격함 | 마우스(현재 그대로) | 마우스 직접 조준(현재 그대로) | 주무장 발사 |
| 호위함 | 키보드 Yaw·Pitch·Roll | 선택한 적을 자동 사격통제 | 선택 |
| 전함 | 키보드(매우 느림) | 자동 사격통제 + 롤로 포각 확보 | 선택 |

실시간 명령(전력·사격 교리·편대)은 개별 단축키를 늘리지 않고 **Hold → 방향 → Release 라디얼 메뉴**로 통합한다.

복잡한 시뮬레이션은 줄이지 않는다. 플레이어가 그것을 일일이 손으로 조작하지 않게 할 뿐이다.

---

## 1. 작업 전에 읽을 것

- `CLAUDE.md`: 빌드·스크린샷·로그 위치, `src/Sim`에 Godot 노드 의존 금지, 좌표 규칙.
- `README.md`: 현재 조작·HUD·검증 명령 전체.
- `docs/PLAN.md`: 단계별 진행과 알아낸 것.

### 1.1 작업 환경 주의

- **셸**: Windows PowerShell. dotnet이 PATH에 없으면 `. .\tools\godot.ps1`을 먼저 실행한다.
- **명령**
  - 빌드: `.\tools\build.ps1`
  - 시뮬레이션 검사: `.\tools\check-sim.ps1` (현재 444개, 약 80초)
  - 화면 확인: `.\tools\shot.ps1 <이름> [인자]` → `shots/<이름>.png`
- **스크린샷 인자**: 쉼표가 든 인자는 `'--pips=3,1,4,0'`처럼 따옴표로 감싼다. 감싸지 않으면 PowerShell이 쉼표에서 배열로 쪼갠다.
- **C# 예외 로그**: `%APPDATA%\Godot\app_userdata\Space Fleet\logs\godot.log`
- **커밋 메시지**: PowerShell here-string으로 `git commit -m`에 넘기면 깨진다. 스크래치 파일에 써서 `git commit -F <파일>`로 커밋한다. 마지막 줄은 `Co-Authored-By: ...`.
- **한글 파일 작성**: bash heredoc으로 쓰지 않는다(인코딩이 깨진다). 파일 쓰기 도구나 UTF-8(BOM 없음) `Set-Content`를 쓴다.
- **사용자 선호**: HUD는 글자보다 도형·게이지로 보인다. 글자는 호출부호·거리·짧은 상태 문구만 쓴다.

---

## 2. 현재 구조: 어디가 결합되어 있나

### 2.1 시뮬레이션 (`src/Sim`)

- `ShipBody.cs`의 `ShipControl` 구조체는 `Thrust`, `Roll`, `Boost`, `FlightAssist`, `Style`, `AimForward`(Vector3?)로 되어 있다.
  - `StepRotation()` 166행: `AimForward`가 있으면 기수를 그 방향으로 돌리고(피치·요만), 없으면 현재 자세를 유지한다. 롤은 `Roll` 입력만 쓴다.
  - `StepTranslation()` 223행: 항공식 비행보조의 선회 감속(`TurnBraking`)이 `AimForward`를 읽는다.
- `SimAI.cs` `Drive()` 330행: AI가 `AimForward`로 기수를 지시한다.
- `SimAI.cs` `Engage()`: AI 레일건 자동 사격 로직(아래 조건 순서). **2차 자동 사격통제에서 그대로 재사용한다.**
  1. 잠금 단계
  2. `FireControl.Solve`(부위 조준 `localAim` 지원)
  3. 비행 시간 상한
  4. 아군 사선 확인(`FriendlyInLine`)
  5. 실드가 있거나 장갑을 뚫을 수 있는 각도(`DamageRay.PreviewArmor`)
  6. `FireRailgun`
- `SimBallistics.cs` `FireRailgun(ship, dir)`이 막는 경우와 실패 사유 문구:

  | 막는 경우 | 실패 사유(`FireAttempt.Reason`) |
  |---|---|
  | 재장전·손상·과열 | 상태별 문구 |
  | 포각(`TraverseDegrees`, 기수 중심 원뿔) 밖 | "주포 사각 밖 · 기수 정렬 필요" |
  | 자기 선체가 포구를 가림 | "자함 선체가 포구를 가림" |

  - **전함 "롤로 포각 확보"는 이 선체 가림 판정으로 이미 성립한다.** 전함 주포 포구는 상부 `[0,94,-410]`, 포각 120°다. 그래서 바로 아래 표적은 선체에 가리고, 180° 롤하면 쏠 수 있다. 새 판정을 만들지 말고 이 판정과 상태 표시를 연결만 한다.
- `FireControl.Solve(shooter, target, time, sensorError, track, localAim)`, `Subsystems.Pick(target, AimSubsystem, from)`, `DamageRay.PreviewArmor(...)`가 이미 있다.
- `ShipPower.SetPips(e, s, w, sens, ecm)`: 합 8, 채널당 0~4.

### 2.2 게임 층 (`src/Game`, `src/View`)

- `ScaleTest.cs` `StepOnce()` 313행: 플레이어 `ShipControl`을 만든다. 각 항목의 입력원은 이렇다.

  | 항목 | 입력 |
  |---|---|
  | `Thrust.x` | A/D |
  | `Thrust.y` | Space/Ctrl |
  | `Thrust.z` | `Throttle`(W/S, X) |
  | `Roll` | Q/E |
  | `AimForward` | `Camera.AimForward` |
- `ScaleTest._UnhandledInput()`
  - 199행: 마우스가 Captured 상태면 마우스 이동을 카메라로 보낸다.
  - 202행: 좌클릭하면 마우스를 Captured로 잡는다.
  - 225행: Esc를 누르면 마우스를 Visible로 푼다.
  - 266행: 우클릭하면 미사일을 쏜다.
- `CombatTest.cs` `StepCombat()`
  - 147행: 좌클릭 유지면 레일건을 쏜다.
  - 151행: 사격보조 ON이면 선행 방향, OFF면 `ManualDirection`(카메라 중심 광선)으로 쏜다.
  - `FireTarget`, `AimPart`(Y), `AimModule`도 여기 있다.
- `ChaseCamera.cs`: 마우스가 `_aim` 기저를 돌린다. 수평선은 함선 롤을 따라간다. `Follow(cls, pos, orient, dt)`, `ResetAim`, `Turn`, `Zoom`이 있다.
- `InputSetup.cs`: 액션을 코드에서 등록한다(`Bind(action, Key)`, PhysicalKeycode 기준).
- `FleetCommand.cs`: 편대 명령. `IssueOrder(OrderKind)`, `ApplySquadOrder()`, `PlayerSquadron`이 있고 G/H/J로 내린다.
- HUD는 `Hud` partial 클래스로 나뉘어 있다: `Hud.cs`, `CombatHud.cs`(조준선·선행), `InstrumentHud.cs`(칩), `DamageHud.cs`(표적 도면 `DrawSchematic`), `PowerHud.cs`, `MissileHud.cs`, `FleetHud.cs`, `SquadronHud.cs`, `AimPartHud.cs`, `MotionHud.cs`.
  - `Hud`는 `MouseFilter = Ignore`이므로 입력은 모두 `ScaleTest`에서 처리한다.

### 2.3 현재 사용 중인 키

W S X A D Space Ctrl Q E Shift Z V Tab Esc R T C Y G H J [ ] 0~5 F1~F8, 좌클릭(레일건), 우클릭(미사일), 휠(줌).

**새로 쓸 키**

| 키 | 용도 |
|---|---|
| F (Hold) | 전력 라디얼 |
| B (Hold) | 사격 교리 라디얼, 호위함·전함만 |
| N (Hold) | 편대 라디얼 |
| Alt + 이동키 | 평행추력(호위함·전함) |
| 중클릭 드래그 | 자유 관찰 |

---

## 3. 단계별 작업

단계마다 빌드 → `check-sim` → 스크린샷 확인 → 커밋 → README·PLAN 갱신 순서로 진행한다. 한 단계를 끝내고 다음으로 넘어간다.

### 1차: 함종별 조함 분리

#### 3.1.1 Sim: `ShipControl` 역할 분리

- `AimForward`를 `HelmForward`로 이름을 바꾼다. 의미는 "기수를 이 월드 방향으로 돌려라"이며, 요격함과 AI가 쓴다.
- 추가: `public float Pitch;`, `public float Yaw;` (-1..1, **목표 각속도 비율**)
  - `Pitch` +1은 기수 위로, 로컬 +X 축 양의 회전이다.
  - `Yaw` +1은 기수 오른쪽으로, 로컬 +Y 축 음의 회전이다. `Roll`의 부호 규칙(+1 = 우롤)과 맞춘다.
- `StepRotation()` 우선순위
  1. `HelmForward`가 있으면 지금 로직 그대로 동작한다.
  2. 없으면 `desired.X = Pitch * maxPitchYaw`, `desired.Y = -Yaw * maxPitchYaw`로 둔다.
  3. 두 입력이 모두 0이면 desired는 0이다. 지금과 같이 각가속 한계 안에서 회전을 멈추고 자세를 유지한다.
  - 함종별 최대 각속도·각가속도와 손상(`ManeuverFraction`), 전력(추진 채널) 제한은 기존 계산을 그대로 탄다. 자세를 순간이동시키지 않는다.
- `StepTranslation()`의 항공식 선회 감속은 `HelmForward`가 있을 때만 동작한다.
- 이름 변경 대상(검색 결과)
  - `src/Sim/ShipBody.cs` 3곳
  - `src/Sim/SimAI.cs` 1곳
  - `src/Game/ScaleTest.cs` 1곳(`AimForward = Camera.AimForward`)
  - `tests/SimChecks/*.cs` 13곳(Program 8, Collision 2, Power 2, Damage 1)
  - `ChaseCamera.AimForward`(카메라 조준 방향)는 카메라 속성이므로 이름을 그대로 둔다. 헷갈리면 `ChaseCamera.AimDirection`으로 바꾸고 `CombatTest`·`ScaleTest`의 사용처를 함께 고친다.

#### 3.1.2 Game: 조작 방식(ControlScheme)

- `enum ControlScheme { Pilot, Helm }`
  - 요격함은 `Pilot`, 호위함·전함은 `Helm`이다.
  - 지금은 `HullKind`로 정한다. 나중에 JSON에 `controlScheme`를 둘 수 있게 판정을 함수 하나(`SchemeFor(ShipBody)`)로 모은다.
- `StepOnce()` 플레이어 입력
  - **Pilot**: 지금과 동일하다(`HelmForward = Camera.AimForward`, A/D·Space/Ctrl은 평행추력).
  - **Helm**

    | 입력 | 동작 |
    |---|---|
    | W/S, X | 스로틀(지금 `Throttle` 로직 그대로) |
    | A/D | `Yaw` |
    | Space/Ctrl | `Pitch` |
    | Q/E | `Roll` |
    | Shift | 부스트 |
    | Alt + A/D | 좌우 평행추력(`Thrust.x`) |
    | Alt + Space/Ctrl | 상하 평행추력(`Thrust.y`) |

    - Alt를 누르고 있으면 A/D·Space/Ctrl이 Yaw·Pitch로 가지 않는다.
    - Alt 판정은 `Input.IsPhysicalKeyPressed(Key.Alt)`로 한다. 액션을 하나 추가해도 된다.
    - `HelmForward = null`이다. 마우스는 조함에 관여하지 않는다.
- 마우스 모드
  - Pilot은 Captured(지금처럼)다.
  - Helm은 Visible(자유 커서)이다.
  - Tab 전환(`SelectControl` → `HandOverControl`) 때 전환한다.
  - Esc 해제는 Pilot에서만 의미가 있다.
  - 202행의 "좌클릭하면 Captured" 처리는 Pilot일 때만 한다.

#### 3.1.3 View: 카메라 모드

- `ChaseCamera`에 `Mode { MouseAim, ShipFollow }`를 추가한다.
  - **MouseAim**: 지금 그대로.
  - **ShipFollow**
    - 카메라 기저는 보간된 함선 자세에 자유 관찰 오프셋(요·피치)을 곱한 것이다.
    - 목표 기저로 부드럽게 따라간다(slerp, 시상수 약 0.25초. 전함의 느린 회전이 화면에서 튀지 않게).
    - 롤은 함선을 따라간다. 화면의 위·아래는 자함 기준이다.
    - 자유 관찰: 중클릭 드래그로 오프셋을 누적한다(요 ±170°, 피치 ±80°).
    - 놓으면 오프셋이 지수 감쇠로 0으로 돌아간다(시상수 약 0.6초).
    - 마우스 이동(드래그 아님)은 카메라를 돌리지 않는다.
  - 거리·높이는 함종 데이터(`CameraDistance`, `CameraHeight`)를 그대로 쓴다. 휠 줌도 유지한다.
- Helm에서 화면 중앙은 기수 방향이다. 기존 기수 표시(○)와 이동 방향 단서(◇, 먼지)는 그대로 둔다.

#### 3.1.4 1차 검증

**SimChecks** (`tests/SimChecks/Program.cs`나 새 `HelmChecks.cs`)

- 호위함에 `Yaw=+1`을 2초 주면 기수가 오른쪽(+X)으로 돈다. 각속도는 `PitchYawRateDeg`를 넘지 않는다.
- 입력을 떼면 각속도가 0으로 수렴하고, 그 뒤 자세가 유지된다(5초 동안 1° 이내).
- 전함에 `Roll=+1`을 계속 주면 180°까지 약 20초가 걸린다(현재 수치 기준 ±25% 안).
- `Pitch=+1`이면 기수가 위로 간다(부호 확인).
- 이름 변경 뒤 기존 검사 444개가 그대로 통과한다. **AI 동작이 바뀌지 않았는지** 확인한다(`Fleet attack`과 `Full battle`의 출력 수치가 커밋 전과 같아야 한다. 같은 시나리오는 결정적이다).

**스크린샷**

- 다음 인자를 추가한다.
  - `--yaw=±1`, `--pitch=±1`: Helm 입력 대체. 기존 `--roll`, `--strafe`처럼 `ShotRequest`에 추가한다.
  - `--scheme`은 필요 없다(함선으로 결정된다).
- 확인 장면
  - `--control=DD-12 --yaw=1 --frames=120`: 호위함이 오른쪽으로 돌고 카메라가 따라간다.
  - `--control=BB-01 --roll=1 --frames=600`: 전함이 천천히 구르고 화면 수평선이 따라 돈다.

**커밋 예**: `Helm controls for escorts and battleships; pilot controls stay on interceptors`

---

### 2차: 호위함·전함 사격통제 분리 (자동 사격)

#### 3.2.1 Sim: `GunneryOrder` (새 파일 `src/Sim/Gunnery.cs`)

```csharp
public enum FireDoctrine { Free, Focus, Disable, Hold, Manual }

public sealed class GunneryOrder
{
    public FireDoctrine Doctrine { get; set; } = FireDoctrine.Free;
    public ShipBody? Target { get; set; }            // 선택한 적(집중·무력화의 대상)
    public string? PriorityModuleId { get; set; }    // 도면 클릭으로 정한 우선 구획
    // 읽기 전용 상태(HUD·검증용)
    public ShipBody? Engaged { get; internal set; }
    public ModuleState? EngagedModule { get; internal set; }
    public GunneryStatus Status { get; internal set; }
    public FiringSolution? Solution { get; internal set; }
}
```

- `ShipBody`에 `public GunneryOrder? Gunnery { get; set; }`를 추가한다.
  - 게임이 플레이어의 호위함·전함에만 설정한다. 요격함·AI 함선은 null이다.
- `SimWorld.Step()`에서 `StepAI()` 다음, 하위 틱 이전에 `StepGunnery()`를 부른다.
  - 대상은 `Gunnery != null`이고 AI 브레인이 꺼져 있거나 없는 함선이다.
- **자동 사격 공용화**
  - `SimAI.Engage()`의 레일건 부분을 `TryAutoFire(ship, target, localAim, maxFlightSeconds, out GunneryStatus)`로 추출한다.
  - AI와 Gunnery가 같이 쓴다. **추출 후 AI 결과가 바뀌면 안 된다**(결정성 검사로 확인한다).
- 교리별 표적과 조준점

  | 교리 | 표적 | 조준점 | 비고 |
  |---|---|---|---|
  | Free | 잠금된 적 중 위협도 최고. 위협도 = 함종 가중(20 km 안 요격함 > 호위함 > 전함) + 거리. `Target`이 잠겨 있으면 우선 | 중심. `PriorityModuleId`가 있으면 그 모듈 | |
  | Focus | `Target`만. 잠금 전이면 `Status = 잠금 대기` | 위와 같음 | |
  | Disable | `Target`, 없으면 Free 선택 | `PriorityModuleId` → 없으면 추진기 → 주포 → 센서 → 방열판 순서로, 살아 있고 지금 위치에서 `PreviewArmor`가 관통 가능한 첫 모듈 → 없으면 가장 가까운 추진기 | 초기 구현은 "함체 중심 외 기능 모듈 우선"이면 충분하다 |
  | Hold | 없음 | — | 자동 발사하지 않음. `Status = 사격 중지` |
  | Manual | 없음 | — | 자동 발사하지 않음. 게임이 직접 쏜다(아래 3.2.3) |

- 비행 시간 상한은 `AiProfile.For(kind).RailFlightSeconds × 1.5`다(전함 12초, 호위함 7.5초).
- `GunneryStatus`(한 줄 문구는 HUD에 쓴다)
  - 사격 중 / 재장전
  - 잠금 대기
  - 사거리 밖
  - 포각 밖 · 기수 정렬 필요
  - **선체 가림 · 롤 필요** (`FireRailgun` 실패 사유 "자함 선체가 포구를 가림"을 이 상태로 연결한다)
  - 사선에 아군
  - 장갑 관통 불가
  - 표적 없음
  - 사격 중지
  - 수동

#### 3.2.2 Game: 마우스 자유화와 선택 (Helm 전용)

- 좌클릭(Manual이 아닐 때)은 **선택**이다.
  1. **표적 패널 도면 모듈 클릭**
     - `DamageHud.DrawSchematic`이 그리는 모듈 사각형을 프레임마다 `Hud`에 캐시한다(`List<(Rect2, ShipView, ModuleState)>`).
     - 클릭이 그 안이면 `Gunnery.PriorityModuleId`를 설정한다.
     - 교리가 Free면 Focus로 바꾸고 `Target`도 그 함선으로 둔다.
  2. **3D 표지 클릭**
     - 화면의 함선 표지 중 클릭 지점 36 px 안에서 가장 가까운 것을 고른다.
     - 표지 위치는 `Hud.DrawBrackets`와 같은 규칙으로 구한다. 적은 추정 위치(`SimPosition + track.Offset`), 아군은 실제 위치다. 브래킷 함수에서 위치 목록을 캐시해 재사용한다.
     - 적: `InspectTarget`과 `Gunnery.Target`을 그 함선으로 둔다.
     - 아군: `SelectedFriendly`(새 속성)로 둔다. 5차 편대 라디얼에서 쓴다.
  3. 빈 곳 클릭은 아무것도 하지 않는다(선택 해제하지 않음).
- R(다음 표적)과 Y(조준 부위)는 그대로 둔다.
  - Y는 Helm에서도 `AimPart`를 바꾼다.
  - Gunnery 조준 모듈 우선순위: `PriorityModuleId` → `AimPart` 선택(`Subsystems.Pick`) → 교리 기본값.
- 우클릭 미사일은 유지한다(선택한 적에게 발사).
- 좌클릭 유지로 반복 발사하는 동작은 Helm에서 제거한다. Pilot은 그대로 둔다.

#### 3.2.3 수동 사격 (Helm, Doctrine = Manual)

- 좌클릭 유지면 발사한다.
- 조준 광선은 `Camera.ProjectRayOrigin/ProjectRayNormal(마우스 커서 위치)`다.
- 기존 `CombatTest.ManualDirection`을 광선을 인자로 받도록 바꿔 재사용한다(광선이 맞는 첫 선체 지점 → 포구에서 그 점 방향).
- 사격통제 보정이 없으므로 ECM 상황에서도 쓸 수 있다.
- 포각과 선체 가림 제한은 그대로 받는다.

#### 3.2.4 HUD (Helm)

- 요격함용 조준선 호(탄약·재장전)는 Pilot에서만 그린다.
- Helm 사격 표시(도형 중심)
  - 교전 표적 위: 선행점 사각형과 조준 모듈 마름모(기존 `AimPartHud` 재사용). 관통 가능하면 초록, 아니면 빨강.
  - 하단 중앙 계기판 옆 작은 패널
    - 교리 칩: 자유·집중·무력화·중지·수동
    - 재장전 호, 탄약 호
    - 상태 한 줄: `GunneryStatus` 문구. "선체 가림 · 롤 필요"는 빨강으로 눈에 띄게.
- 선택한 아군은 브래킷을 굵게 그린다.
- 커서는 OS 커서를 그대로 쓴다.

#### 3.2.5 2차 검증

**SimChecks** (`GunneryChecks.cs`)

- Focus: 정지한 호위함이 잠금된 적 호위함(20 km)에 자동으로 레일건을 쏘고 피해를 준다.
- Hold면 0발이다. Manual이면 자동 0발이다.
- Free: 적 둘(가까운 요격함 15 km, 먼 전함 60 km) 중 요격함을 고른다.
- Disable: `EngagedModule`이 기능 모듈(추진기 등)이고, 그 모듈이 손상된다.
- **전함 롤**
  1. 적 호위함을 전함 바로 아래 8 km에 둔다(잠금).
  2. 처음 3초 동안 발사가 0발이고 상태가 "선체 가림 · 롤 필요"인지 확인한다.
  3. `Roll=+1`을 주어 약 180° 구르게 한다.
  4. 롤 완료 뒤 30초 안에 자동 발사가 1발 이상인지 확인한다.
- AI 공용화 후 AI 결정성: `Fleet attack` 결과가 2차 이전 커밋과 같아야 한다.

**스크린샷**

- 필요한 인자
  - `--doctrine=focus|free|disable|hold|manual`
  - `--below=<km>`: 연습 표적을 내 아래에 배치한다. 기존 `SetupPractice`에 위치 옵션을 추가한다.
- 확인 장면
  - `--control=DD-12 --ballistics-test=DD-X1 --test-distance=20000 --target-speed=0 --doctrine=focus --frames=300 --no-ai`: 호위함이 자동 사격한다.
  - `--control=BB-01 ... --below=8 --frames=200`: "선체 가림 · 롤 필요" 표시.
  - 같은 조건 + `--roll=1 --frames=1600`: 롤 후 사격.

---

### 3차: 기본 사격 명령 정리

- 2차의 다섯 교리를 개발용 단축키로 빠르게 바꿀 수 있게 한다.
  - 예: Helm에서 T를 누르면 교리를 순환한다. Pilot에서 T는 지금처럼 사격보조 ON/OFF다.
- 실제 플레이 입력은 4차 라디얼로 간다.
- 교리를 바꾸면 `Notify`로 짧게 알린다.

---

### 4차: 공용 라디얼 메뉴

#### 3.4.1 구조

- **순수 계산 부분**: `src/Shared/RadialMath.cs`(새 폴더)
  - Godot 수학 타입만 쓴다.
  - `tests/SimChecks/SimChecks.csproj`의 Compile에 `../../src/Shared/*.cs`를 추가해 검사한다.
  - 내용
    - `RadialItemSpec(float AngleDeg)`: 0° = 위, 시계 방향 +.
    - `static int Pick(Vector2 offset, IReadOnlyList<float> anglesDeg, float deadZone)`: 오프셋이 데드존(36 px) 안이면 -1. 아니면 오프셋 각도와 가장 가까운 항목. 항목 사이 경계는 이웃 항목 각도의 중간이다.
- **화면 부분**: `src/Game/RadialMenu.cs` (`Control`, HudLayer에 추가)

```csharp
public sealed record RadialItem(string Label, float AngleDeg, Action Execute, Func<bool>? Enabled = null);

public partial class RadialMenu : Control
{
    public bool IsOpen { get; }
    public int Highlighted { get; }
    public void Open(string title, IReadOnlyList<RadialItem> items, Vector2 center, bool virtualPointer);
    public void FeedMotion(Vector2 relative);   // 가상 포인터(Pilot, 마우스 Captured)
    public void SetPointer(Vector2 screen);     // 실제 커서(Helm)
    public void Release();                      // 강조 항목 실행, 데드존이면 취소
    public void Cancel();
}
```

#### 3.4.2 입력 흐름

- 라디얼 키를 누르면 메뉴를 연다.
  - 중심: Helm은 커서 위치, Pilot은 화면 중앙.
  - Pilot은 마우스가 Captured이므로 상대 이동을 누적한 가상 포인터를 쓴다(반경 140 px로 제한).
- 열려 있는 동안
  - 마우스 이동은 메뉴에만 간다. Pilot 카메라 조준을 돌리지 않는다.
  - 좌클릭 사격과 선택은 막는다.
- 키를 떼면 `Release()`한다. Esc는 `Cancel()`이다.
- 한 번에 메뉴 하나만 연다. 다른 라디얼 키는 무시한다.
- `_UnhandledInput`에서 `IsActionPressed`/`IsActionReleased`로 처리하고, 이벤트를 소비한다(`GetViewport().SetInputAsHandled()`).
- **제스처 사용**: 키 누름 → 바로 오른쪽으로 휙 → 떼기만으로 실행되어야 한다. 메뉴가 열리는 애니메이션이나 지연을 두지 않는다.

#### 3.4.3 그리기 (도형 중심)

- 반경 120 px 원판을 반투명 배경으로 깐다.
- 항목마다 섹터 호와 라벨(반경 90 px)을 그린다.
- 강조 섹터는 채우고, 사용 불가 항목은 흐리게 한다.
- 데드존 원을 그리고, 가운데에 제목과 "취소"를 작게 쓴다. 포인터 방향은 짧은 선으로 표시한다.
- 항목은 4~6개를 권장한다. 8개 이상은 만들지 않는다.

#### 3.4.4 전력 라디얼 (F, 모든 함종)

| 방향 | 항목 | 핍(추진/실드/무장/센서/ECM) |
|---|---|---|
| 위 0° | 센서/EW | 1/1/1/3/2 |
| 오른쪽 90° | 화력 | 1/1/4/2/0 |
| 아래 180° | 기동 | 4/2/1/1/0 |
| 왼쪽 270° | 방어 | 1/4/2/1/0 |
| 오른쪽 위 45° | 균형 | 2/2/2/2/0 |

- 실행은 `ShipPower.SetPips(...)`로 한다. 기존 1~5·0 핍 단축키는 개발용으로 남긴다. F1 도움말의 "개발용" 구역으로 옮긴다.
- 값은 밸런스용 초기값이다. 프리셋 표 하나(`PowerPresets`)에 모아 둔다.

#### 3.4.5 4차 검증

- SimChecks(`RadialMath`)
  - 데드존 안이면 -1이다.
  - 네 방향 각각이 해당 항목을 고른다.
  - 45° 항목과 0°·90° 항목 사이 경계가 정확하다.
  - 항목이 5개일 때 각 섹터 폭이 이웃 중간값으로 정해진다.
- 스크린샷
  - 인자 `--radial=power --radial-dir=<도> --radial-hold-frames=N`: N 프레임 동안 메뉴를 열고 그 방향으로 포인터를 둔 채 찍는다.
  - `--radial-release`가 있으면 떼고 결과를 로그로 남긴다.
  - 확인 1: 열린 메뉴에서 오른쪽 섹터가 강조된다.
  - 확인 2: 떼면 핍이 1/1/4/2/0이 된다(로그).
  - 확인 3: 데드존에서 떼면 핍이 바뀌지 않는다.

---

### 5차: 같은 라디얼 재사용

#### 3.5.1 사격 라디얼 (B, Helm만. Pilot에서는 열지 않음)

| 방향 | 항목 |
|---|---|
| 위 0° | 무력화 (Disable) |
| 오른쪽 90° | 집중사격 (Focus, 선택한 적. 없으면 사용 불가로 흐리게) |
| 아래 180° | 자유사격 (Free) |
| 왼쪽 270° | 사격중지 (Hold) |
| 왼쪽 아래 225° | 수동사격 (Manual) |
| 오른쪽 위 45° | 즉시 일제사격: 선택한 적에게 미사일 1발 + 주포가 장전돼 있으면 비행 시간 상한을 무시하고 1발 |

#### 3.5.2 편대 라디얼 (N, 모든 함종. 내 편대 = `PlayerSquadron`)

| 방향 | 항목 | 시뮬레이션 명령(내 편대의 다른 함선) |
|---|---|---|
| 왼쪽 270° | 호위 | `EscortOf(나, fireAt: 나의 교전 표적)` |
| 위 0° | 요격 | 2초마다 다시 계산: 내 20 km 안의 식별된 적 요격함을 나눠 `AttackOn`, 없으면 호위 |
| 오른쪽 90° | 집중공격 | 선택한 적에게 `AttackOn`(없으면 사용 불가) |
| 아래 180° | 복귀 | `EscortOf(나)`, fireAt 없음(대형 재집결) |
| 오른쪽 아래 135° | 위치유지 | `HoldAt(각자 현재 위치)` |

- `FleetCommand.cs`의 `SquadOrder`(OrderKind)를 게임 쪽 `SquadCommand { Escort, Intercept, Focus, Return, Hold }`로 넓힌다.
  - 기존 G/H/J는 각각 Focus/Escort/Hold로 남긴다(개발용).
  - "요격"은 `_PhysicsProcess`에서 2초마다 다시 적용한다.
- 편대 현황 HUD(`SquadronHud.cs`)의 내 편대 행동 문구를 새 명령 이름으로 바꾼다.
- 아군을 클릭해 고른 `SelectedFriendly`가 다른 편대이고 그 편대에 명령을 내리는 기능은 이번 범위가 아니다. 내 편대만 다룬다.

#### 3.5.3 드론 라디얼: 이번 범위 아님

- 함재 드론은 지금 연출용 MultiMesh(`View/DroneSwarm.cs`)뿐이고 시뮬레이션 개체가 없다.
- 드론을 시뮬레이션 개체로 만드는 작업이 먼저다. 그때 같은 `RadialMenu`로 붙인다.
  - 예정 항목: 자함 방어, 지정 아군 방어, 미사일 요격, 소형함 요격, 복귀
  - 예정 키: M

#### 3.5.4 5차 검증

- SimChecks
  - 편대 "요격" 명령으로 근처 적 요격함 2척이 내 편대에 나눠 배정된다.
  - "복귀"면 공격 명령이 해제되고 호위 대형으로 돌아온다.
- 스크린샷
  - 사격 라디얼에서 "무력화"를 고르면 Helm HUD 교리 칩이 바뀐다.
  - 편대 라디얼에서 "집중공격"을 고르면 편대 명령 선(점선)이 선택한 적을 향한다.

---

## 4. 유지할 것 (지우지 말 것)

- 개발·검증 기능
  - Tab 함선 전환
  - F1~F8(시험 사격, 모듈 표시, 복구, 이동 표적, 미사일 훈련)
  - 1~5·0 직접 핍
  - T 사격보조
  - G/H/J
  - `[` `]` 배속
  - 스크린샷 인자 전부
  - `tools/*`
  - `tests/SimChecks` 전부
- 이것들은 F1 도움말에서 "플레이 조작"과 "개발용"으로 나눠 보여 준다. 기능은 그대로 둔다.
- 요격함 조작감: 우주식 비행보조, 이동 방향 단서(먼지·◇·다이얼), 보조 추진기 연출, 사격보조 선행 표시. 지금 상태를 바꾸지 않는다.
- AI 동작: 2차에서 `TryAutoFire` 공용화를 하더라도 결정성 검사 결과가 바뀌면 안 된다.

## 5. 이번에 하지 말 것

- 신규 함종, 신규 무기, 캐리어, 테크트리
- 실제 다중 주포대(지금 주포 1문 그대로)
- 중첩 라디얼
- 멀티플레이
- 최종 UI 디자인(지금은 도형 중심의 임시 HUD)
- 드론 시뮬레이션(3.5.3 참조)
- 밸런스 조정. 수치는 프리셋 표 한곳에 모아 두기만 한다.

## 6. 완료 기준

**요격함**

- Tab으로 고르면 지금처럼 마우스로 기수를 돌리고 좌클릭으로 직접 쏜다. 사격보조 ON/OFF도 같다.

**호위함**

- 마우스를 움직여도 함선이 돌지 않는다. 커서가 보인다.
- A/D·Space/Ctrl·Q/E로 선체를 돌린다. Alt를 함께 누르면 평행추력이다.
- 좌클릭으로 적을 선택하면 주포가 자동 사격통제로 공격한다(잠금 상태).
- 표적 도면의 모듈을 클릭하면 우선 공격 구획이 된다.
- 사격 라디얼로 수동사격으로 바꾸면 커서 방향으로 직접 쏠 수 있다.

**전함**

- 호위함과 같은 입력이고, 회전과 기동은 데이터대로 매우 느리다.
- 적이 하부 사각에 있으면 HUD에 "선체 가림 · 롤 필요"가 뜬다. Q/E로 롤하면 포각이 생기고 자동으로 교전한다.

**라디얼 메뉴**

- 키를 누르는 동안 메뉴가 보이고, 마우스 방향으로 강조된다. 떼면 실행되고, 중앙에서 떼면 취소된다.
- Pilot(마우스 Captured)과 Helm(자유 커서) 양쪽에서 동작한다.
- 전력·사격·편대가 같은 컴포넌트를 쓴다.

**공통**

- `check-sim` 전부 통과(새 검사 포함).
- 단계별 스크린샷이 `shots/`에 있다.
- README 조작표와 PLAN이 갱신되어 있다.

## 7. 보고 형식 (단계마다)

- 무엇을 바꿨는지(파일), 검사 수와 통과 여부, 확인한 스크린샷 경로, 알아낸 문제와 남긴 것.
- **조작감처럼 사람이 직접 해 봐야 아는 부분은 "미확인"으로 명시한다.**
