# 티어 설계 5단계 검증 — 2026-10-15

진영 연구 트리, T1 두 함급, 영구 부품 개량, 진행 저장/이관, 편대 기준 티어 매칭. 구현 계약은 `../../DESIGN_progression.md` §16.

## 재현

저장소 루트에서 기존 .NET SDK/GodotSharp 참조를 사용한다.

```powershell
dotnet build SpaceFleet.csproj --no-restore
dotnet build tests/SimChecks/SimChecks.csproj -c Release --no-restore
dotnet tests/SimChecks/bin/Release/net8.0/SimChecks.dll --progression
dotnet tests/SimChecks/bin/Release/net8.0/SimChecks.dll --squad-loadouts
dotnet tests/SimChecks/bin/Release/net8.0/SimChecks.dll
dotnet tests/SimChecks/bin/Release/net8.0/SimChecks.dll --starter-battle
```

실제 렌더러 Godot 4.7.2 .NET:

```text
--path <repo> --fixed-fps 60 -- --model-test
--path <repo> --fixed-fps 60 -- --progression-test --seed=1
--path <repo> --fixed-fps 60 -- --hangar-test --seed=1
--path <repo> --fixed-fps 60 -- --match-flow-test --seed=1
--path <repo> --fixed-fps 60 -- --flow-test --seed=1
--path <repo> --fixed-fps 60 -- --menu=research --frames=24 --shot=<absolute-path.png>
--path <repo> --fixed-fps 60 -- --menu=research --hangar-hull=mars_starter_escort --frames=24 --shot=<absolute-path.png>
--path <repo> --fixed-fps 60 -- --menu=hangar --hangar-hull=earth_starter_escort --hangar-exterior --frames=24 --shot=<absolute-path.png>
--path <repo> --fixed-fps 60 -- --menu=hangar --hangar-hull=mars_starter_escort --hangar-exterior --frames=24 --shot=<absolute-path.png>
--path <repo> --fixed-fps 60 -- --menu=title --progression-test --seed=1 --frames=260 --shot=<absolute-path.png>
```

연구/흐름/스크린샷 검사는 사용자 진행을 읽거나 저장하지 않는다. 연구 화면 스크린샷은 두 T1을 보유하고 지구 RP 450을 준 예시다. T1 격납고 스크린샷은 전체 함급을 보유한 검사 프로필이다. `research-result.png`는 적 함대를 검사 코드로 파괴하여 보상 레이아웃을 확인한 화면이며, 자연 전투 통계가 아니다. 실제 저장 검사는 테스트 바이너리 옆 GUID 폴더에서 파일 왕복·백업·손상 보존을 검사하고 자신의 파일만 지운다.

## 검증 범위

- 전체 기능 **5,606,976개** 통과(연구 390개·편대 513개 포함). 이전 단계 로그의 비교는 `manifest.json`에 기록한다. 연구·편대 검사의 두 줄을 제외하면 4단계 로그와 동일하다. 기존 난전 목표 2/6 미달 경고도 동일하다.
- 연구 검사: 34노드/12함급, T6 데이터 확장, 순환/잘못된 선행 관계 거부, T1 물리 모듈 좌표, 부품 합성, 중복 구매/지급 방지, 잠긴 함급/부품 출격 거부, 저장 왕복/손상 거부, v1/기존 선택 이관, 원본 백업 보존.
- 매칭 검사: 6개 대표 편성 × 시드 8개. 편대원 전원의 체급·티어 분포와 연구 반영 점수 대칭, ±1 범위, T1 전용 전장, 전함 없는 기함 규칙, 실제 틱, 호위 티어에 따른 편대 점수 차이. 같은 체급의 T1→T2 점수 배율을 중복 적용하지 않는 검사도 포함한다.
- 렌더/흐름: 모델 1,769 / 연구 구매·출전·결과·재출격 16 / 격납고 22 / 매칭·관전 30 / 기존 장면 51, 합계 1,888개. T1 모델도 기존 6종과 같은 포탑 yaw·pitch·포구·실제 추적·RCS·손상 효과 검사를 받는다.
- T1 80척 섬멸전의 자연 완주 및 양팀 주포/미사일 교전·유한 좌표 검사(4개): `starter-battle.txt`: 시드 1, 982.8초 Red 승리, 45척 격침. 단일 시드의 통합 검사다. 혼성 팀의 Red 승리를 화성 승리로 해석하지 않는다.
- 지구·화성 연구 트리, 두 T1 격납고, 결과 보상 화면을 렌더해 확인했다. 긴 함급명은 함급명/역할/상태 세 줄로 배치했다.

## 보존과 한계

- 4단계 manifest 대비 기존 함체 6종·순양함 4종·물리 장비 4종은 동일하다. 기존 6종 Blender/GLB 및 manifest 항목은 HEAD와 동일하며, 두 T1 자산만 추가했다.
- 새 모델 재생성: `python tools/generate-progression-data.py` 후 Blender 5.1에서 `--factory-startup --background --python tools/build-starter-models.py -- --root <repo>`. 이 도구는 T1 파생 원본/GLB를 다시 쓰므로 T1을 수작업 편집한 뒤에는 덮어쓰기 여부를 먼저 판단한다. 기존 원본 6종은 읽기만 한다. 검사한 GLB를 저장소에 포함하므로 게임 실행에 Blender는 필요 없다.
- RP 비용/획득량, 티어 전투력, AI 연구·장착 비율은 임시값이다. 이번 검사는 승률 균형·경제 속도·FPS/틱 비용 개선을 입증하지 않는다. 기존 화성 우세와 난전 빈도 부족은 남아 있다.
- 미구현 22개 트리 함급·별도 저장 복구 UI·온라인 경제/매칭은 포함하지 않는다. 현재 배포 EXE도 갱신하지 않았다.
- 소스/데이터/신규 모델과 원자료 SHA-256은 `manifest.json`. 과거 단계 검증 자료는 변경하지 않는다.
