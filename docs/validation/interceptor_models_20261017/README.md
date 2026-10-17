# T3 팔케·허셜 기본형 — 2026-10-17

사용자 결정: 함급별 기본 실루엣과 무장·모듈 위치를 먼저 완성하고, 표면 디테일과 최종 재질은 뒤로 미룬다. 이 차수는 T3에서 원형 요격함 외형을 공유하던 두 함급을 분리한다. T3 8개 함급 모두 독립 모델을 가지며 전체 34개 함급은 이제 16개 모델을 사용한다.

| 함급 | ID | 기본형 | 기존 길이 | PD | 삼각형 / 렌더 메시 |
|---|---|---|---:|---:|---:|
| 팔케 | interceptor_t3 | 넓은 후퇴익·장갑 어깨, 외측 쌍발 추진기, 상·하부 자위포 | 30m | 2 | 6,140 / 5 |
| 허셜 | mars_interceptor_t3 | 가는 선수·좁은 연결 날개, 외측 쌍발 추진기, 상부 자위포 | 26m | 1 | 5,924 / 3 |

두 함 모두 기존 전방 고정포 1문, 일반 미사일 4발, AM 3발, 프로브 2개를 유지한다. 데이터의 비행·무장·장갑 두께·실드·HP·센서 수치는 변경하지 않았다. 피해 구획, 주기관·냉각기·주포·격리실과 발사구 위치는 새 형상에 맞춰 바뀌므로 실제 피탄 면적과 가림의 밸런스는 이후 혼성전에서 확인해야 한다.

## 제작물

- 레퍼런스: `art/reference/20261017_t3_interceptors/falke-herschel.png`와 정확한 생성 프롬프트. 이미지의 임의 길이·앞을 향한 추진구·여분 포구는 채택하지 않았다.
- 원본: `art/blender/interceptor_t3.blend`, `art/blender/mars_interceptor_t3.blend`. 편집 가능한 103 / 99개 부품과 별도 숨김 모듈 가이드.
- GLB: `assets/ships/`의 같은 ID. 회전·고각 PD 리그, 주포·미사일·AM·프로브 포구, 엔진·RCS 마커 포함.
- 피해/장비 좌표: `data/model_layouts/`의 같은 ID. 함급 JSON과 생성기에 연결.
- 실제 Godot 초상화: `assets/ui/ships/`의 같은 ID. `*-hangar.png`는 실제 1600×900 격납고 캡처.
- `*_hero/underside/rear/top.png`: 최종 Blender 검토 렌더. 패널선·볼트·문 작동·최종 텍스처는 보류.

## 검증

- `preservation-audit.json`: 기존 파일 74개 해시 일치. 변경 허용 범위는 대상 variant 2개와 manifest. 기존 Blender/GLB/초상화 14종과 다른 함급 데이터 보존.
- 모든 티어 variant 22개를 별도 디렉터리에서 재생성해 현재 정의와 동일함을 확인. 레이아웃 재적용도 동일 결과.
- `geometry-audit.json`: 모듈·발사구·PD 좌표 45개 오차 0, 양쪽 핀 대칭, 주기관 4개 발광면 후방 가시성, 두 함 각각 연결 그룹 1개. AABB 인접 검사는 후보 탐지이므로 상·하·후방 렌더로 보완했다. 초기 노즐 외곽의 0.105m 틈을 발견해 하우징 접합부 수정.
- `sim-tier-branches.log`: 291개 통과. 기존 편대·진행·세이브·프로브·격리 검사와 신규 함급의 실제 미사일/AM/프로브 발사·독립 잔량 소모, 모듈 관통 경로 14개 포함.
- `dotnet-build.log`: 경고 0 / 오류 0.
- 실제 배포 EXE의 15묶음 / 6,288개 통과. 모델 5,014개(포구·PD 회전/고각·교체 주포·엔진 손상 FX 포함), 격납고 59개, 연구 36개 및 나머지 공통 입력·전투·음향·환경 검사. `builds/SpaceFleet-20261017-interceptors-Windows-x64/SpaceFleet.exe`와 전체 폴더 ZIP 제공. 패키지/해시 증빙은 `release-verification.json`.
- 대규모 전투 승률·직접 조종 장시간 플레이·240척 FPS는 이 차수에서 측정하지 않았다.

## 재작업

`tools/author-interceptor-layouts.py`로 좌표 적용 → Blender에서 `tools/build-interceptor-models.py -- --root <저장소> --render` → `tools/audit-interceptor-models.py` → `tools/export-menu-art.ps1 -Models interceptor_t3,mars_interceptor_t3`.

Blender에서 수동 수정한 뒤에는 **생성기를 다시 실행해 덮어쓰지 말고** 기존 `tools/export-ship-models.py -- --root <저장소> --ship <ID>`로 내보낸다. `tools/check-interceptor-assets.py`는 기존 파일 보존과 수치/재생성을 검사한다. 캡처의 APPDATA는 저장소 `shots/interceptor-model-user`로 분리되어 실제 사용자 저장을 건드리지 않는다.
