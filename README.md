# Space Fleet (가칭 Project Fleet)

3D 우주 함대전 프로토타입. Godot 4.7 .NET + C#.

계획과 진행 상황은 [docs/PLAN.md](docs/PLAN.md)에 있다.

![0단계 스케일 테스트](docs/stage0_flyby.png)

## 필요한 것

- Godot 4.7.2 .NET: `%LOCALAPPDATA%\Programs\Godot\Godot_v4.7.2-stable_mono_win64\` (경로는 `tools/godot.ps1`에서 관리)
- .NET 8 SDK(Godot 에디터 C# 도구가 요구), .NET 10 SDK

## 실행

```powershell
.\tools\run.ps1        # 빌드 후 실행
.\tools\edit.ps1       # 에디터로 열기
.\tools\build.ps1      # C# 빌드만
```

스크린샷 모드(자동 검증용, `shots/`에 저장):

```powershell
.\tools\shot.ps1 이름 --control=BB-01 --look-at=DD-11 --turn=30,-10 --throttle=1 --frames=240 [--far] [--fixed-origin] [--zoom=1.5]
```

## 조작 (0단계 스케일 테스트)

| 입력 | 동작 |
|---|---|
| 마우스 | 조준 방향. 함선이 그쪽으로 기수를 튼다 |
| W / S, X | 스로틀 올림·내림, 0으로 |
| A / D, Space / Ctrl | 좌우·상하 평행이동 |
| Q / E | 롤 |
| Shift | 부스트 |
| Z | 비행보조 켜기·끄기 |
| Tab | 아군 함선 전환(전함·호위함·요격함) |
| 휠 | 카메라 거리 |
| F2 | 렌더 원점: 카메라 기준 ↔ 월드 0 고정(정밀도 비교) |
| F3 | 아군 전체 1,000 km 도약 |
| Esc / 클릭 | 마우스 해제 / 다시 잡기 |

## 구조

```
src/Sim/    시뮬레이션. Godot 노드 비의존, 60Hz 고정 틱, 위치는 double
src/View/   연출. 보간, 카메라 기준 렌더링, 절차적 함선 외형, 카메라
src/Game/   씬 조립, 입력, HUD
shaders/    별 배경, 가스 행성, 엔진 화염
tools/      빌드·실행·스크린샷 스크립트
```
