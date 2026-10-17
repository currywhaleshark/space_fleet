# 티어 설계 4단계 검증 — 2026-10-15

편대 슬롯, 함급별 프리셋 3개, 실제 모델 격납고, 로컬 저장, 편대원별 세팅 출격·인계. 계약은 `../../DESIGN_progression.md` §15를 따른다.

## 재현

저장소 루트에서 .NET SDK와 기존 GodotSharp 참조가 있는 환경:

```powershell
dotnet build SpaceFleet.csproj --no-restore
dotnet build tests/SimChecks/SimChecks.csproj --no-restore
dotnet tests/SimChecks/bin/Debug/net8.0/SimChecks.dll --squad-loadouts
dotnet tests/SimChecks/bin/Debug/net8.0/SimChecks.dll
dotnet build tests/SimChecks/SimChecks.csproj -c Release --no-restore
dotnet tests/SimChecks/bin/Release/net8.0/SimChecks.dll --equipped-battles
```

Godot 4.7.2 .NET 인자(실제 렌더러):

```text
--path <repo> --fixed-fps 60 -- --hangar-test --seed=1
--path <repo> --fixed-fps 60 -- --flow-test --seed=1
--path <repo> --fixed-fps 60 -- --match-flow-test --seed=1
--path <repo> --fixed-fps 60 -- --model-test --seed=1
--path <repo> --fixed-fps 60 -- --menu=hangar --hangar-hull=battleship --hangar-fit --frames=24 --shot=<absolute-path.png>
--path <repo> --fixed-fps 60 -- --menu=hangar --hangar-hull=mars_interceptor --hangar-fit --frames=24 --shot=<absolute-path.png>
--path <repo> --fixed-fps 60 -- --menu=hangar --hangar-hull=earth_air_defense_cruiser --hangar-exterior --frames=24 --shot=<absolute-path.png>
```

`--hangar-fit`은 화면 검증용 센서 중심 프리셋이다. 검사/스크린샷 인자는 실제 사용자 저장을 읽거나 덮어쓰지 않는다. 저장 검사는 테스트 바이너리 옆의 임시 GUID 폴더에서 수행하고 자신이 만든 파일만 정리한다. sandbox TEMP의 rename 제한을 피하기 위한 위치이며 실제 게임 저장 위치는 `user://hangar.json`이다.

## 결과

- 전체 기능 5,606,560개. 신규 487개 줄만 제외하면 3단계 로그와 동일. 기존 난전 목표 2/6 미달 경고도 동일하다.
- 새 검사: 10개 함급의 AI 프리셋 3종, 슬롯 수·진영·역할·보유 제한, 편집 실패 시 원상 유지, 저장 왕복·중복 필드/손상 거부·이전 선택·백업 보존, 같은 시드 전체 세팅 재현, 4개 시드의 실제 스폰/틱.
- 격납고 22 / 기존 장면 51 / 매칭 30 / 모델 1,475개 통과. 격납고 검사는 베이 좌표 투영·클릭·회전, 혼성 순양함 호위 슬롯, 출격 스냅샷 고정, 기함 격침 후 해당 편대원의 실제 장비와 피해 상태 유지까지 확인한다.
- 빌드 경고·오류 0. `hangar-*.png`는 장비 장착 전함/화성 요격함의 투명 보기와 시러스급 외형 보기다. 긴 장비명이 다른 베이 버튼을 침범하지 않도록 줄임 처리하고 툴팁에 전체 이름을 남겼다.
- `equipped-battles.log`: 시드 1 우주전 80척, 783.85초 Red 승리, 16척 격침. 시드 2 소행성전 78척, 888.4초 Red 승리, 25척 격침. 양팀 주포/미사일 교전·정상 좌표·완주 검사 8개 통과.
- 각 팀은 지구·화성 혼성이다. 2판은 통합 검사이며 진영 승률이나 FPS/틱 성능 측정으로 해석하지 않는다. 이전 화성 우세는 해결됐다고 주장하지 않는다.
- 연구·해금·티어 매칭과 신규 배포 EXE는 이번 범위에 포함하지 않았다. 현재 10개 함급은 시험 운용 상태다.

`manifest.json`에 검사 합계·로그 비교·검사 대상 소스 SHA-256을 기록했다. 과거 1~3단계 검증 폴더는 해당 시점 결과를 보존한다.
