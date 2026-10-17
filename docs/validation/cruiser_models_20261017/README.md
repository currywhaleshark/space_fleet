# T3 순양함 역할별 모델 기본형

사용자 선택에 따라 레퍼런스 이미지 → Blender 기본형 → 게임 검증 순서로 T3 4종을 분리했다. 최종 외장 디테일·재질 및 장기 밸런스 확정 전 단계다.

| 함급 ID | 외형 | 기존 전투 장비 |
| --- | --- | --- |
| earth_gun_cruiser | 두꺼운 선수와 양측 장갑 어깨 | 연장 주포 2기, PD 4기, 미사일 16발 |
| earth_air_defense_cruiser | 전후 4개 방공 갑판과 드론 크래들 | 연장 주포 2기, PD 6기, 미사일 20발, 드론 4기 |
| mars_gun_cruiser | 가는 선수, 긴 포신, 골조와 수직 방열판 | 연장 주포 2기, PD 2기, 미사일 16발 |
| mars_missile_cruiser | 좌우 8개 카세트에 32개 셀 | 연장 주포 2기, PD 2기, 미사일 32발 |

## 연결 및 보존

- 독립 `.blend`·`.glb`·manifest·함급 modelId·격납고/연구소 초상화 4세트. 주포 yaw/pitch/recoil, PD yaw/pitch, 실제 포구, 모듈/베이, 메인 엔진 2개 및 RCS 14개 소켓을 연결했다.
- 지구권 센서탑을 모듈 위치에 맞춰 낮추고, 시러스 PD를 넓어진 갑판으로 옮겼다. 새 장갑 어깨·갑판·미사일 카세트는 기존 AABB 구획 피해 체계에 포함했다. 모듈의 중심·크기·HP 및 무장/비행/방어 성능 수치는 보존했다.
- 보이는 셀 수는 탑재량을 표현하는 기본 외형이다. 미사일은 기존 중앙 발사구에서 발사하며 개별 셀 도어 애니메이션은 구현하지 않았다. 크래들에 수납된 드론 표현도 정적인 기본형이다.
- `baseline.json`은 작업 시작 당시 데이터·원본·GLB·카드의 해시와 변형 함선 정의다. `preservation-audit.json`은 기존 60개 파일 보존, 4종의 비형상 수치 동일, 상위 순양함 8종을 다시 생성해도 정의가 동일함을 확인한다.
- 상위 티어 생성기는 T3의 새 형상을 상속하지 않고 이전 형상을 복원한 뒤 티어 성능을 적용한다. T4/T5 전용 모델은 후속 작업이다.

## 검증

- Blender 소스의 모듈·포탑·포구·PD 마커 120개: 오차 최대 약 0.000002m. 네 함급 모두 분리된 메시 AABB 후보 그룹 없음. 이 검사는 실제 접촉의 완전한 증명은 아니므로 정면 사선·하부·상면·후면 렌더도 함께 확인했다. 엔진 덮개가 발광면을 가리던 구조를 수정하고 후방 광선으로 노즐 8개의 발광면 가시성을 별도 검사한다.
- 실제 Godot 모델 검사는 독립 modelId 전체로 확대했다. 두 팀의 식별색, 메인/보조포 회전·고각·포구·반동, 교체 주포, 손상 엔진 이펙트, 모듈 및 드론 연결을 검사한다. `model-test.log` 및 격납고/연구 검사 로그 참고.
- `sim-cruisers.log`: 145개 통과. 28개 센서·엔진·주포·방열판 관통 경로, 순양함 편대 명령, 실제 포격, 4발 미사일 일제사격, 방공 요격 포함. 통제 방공 시험에서 12발 중 기본 호위함 3발, 시러스 6발을 요격했다. 이는 고정 조건의 기능 확인이며 함대전 승률을 의미하지 않는다.
- `sim-tier-branches.log`, `sim-components.log`: 티어 저장·임무 및 교체 장비 회귀 검사.
- `*-hangar.png`는 실제 Godot 격납고 캡처다. `*_hero.png` / `*_underside.png` / `*_top.png`는 Blender 스튜디오 렌더다. 레퍼런스 시안과 혼동하지 않는다.

## 재작업 방법

1. 형상 계약 변경: `data/model_layouts/<id>.json` 수정 후 `python -X utf8 tools/author-cruiser-layouts.py`.
2. **기본형을 처음부터 재생성할 때만** Blender에서 `tools/build-cruiser-models.py -- --root <저장소> [--ship <id>] --render`. 이 명령은 선택한 신규 4종 원본을 덮어쓰므로 수작업 수정 뒤에는 실행하지 않는다.
3. Blender에서 수작업 수정한 뒤에는 `tools/export-ship-models.py -- --root <저장소> --ship <id>`로 저장된 원본을 읽어 GLB만 갱신한다.
4. `tools/audit-cruiser-models.py`, `tools/check-cruiser-assets.py`, Godot `--model-test`로 연결·보존·런타임을 확인한다.
5. `tools/export-menu-art.ps1 -Models earth_gun_cruiser,earth_air_defense_cruiser,mars_gun_cruiser,mars_missile_cruiser`로 이 4개 초상화만 갱신한다.

새 형상은 피탄 면적·가림·실드 외피에 영향을 줄 수 있다. 장기 혼성 함대전 승률, 240척 렌더 성능 및 사람이 직접 조종한 조작감은 이번 기본형 검증의 완료 주장에 포함하지 않는다.

## 실행본

`builds/SpaceFleet-20261017-cruisers-Windows-x64/SpaceFleet.exe` 및 같은 이름의 ZIP. 노즐 덮개 수정까지 포함한 최종 기본형 빌드다. 다른 곳으로 옮길 때는 실행파일이 들어 있는 폴더 전체 또는 ZIP을 사용한다.

배포 EXE 자체의 15묶음/6,094개 검사 통과. ZIP CRC·SHA-256 정상, 배포 DLL이 최종 ExportRelease DLL과 일치한다. .NET 런타임 포함. `release-verification.json`에 검증 결과 및 새 Blender/GLB/카드/형상 데이터 20개 해시를 기록했다.
