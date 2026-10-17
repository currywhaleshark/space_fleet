# T4 소형함 기본형 4종 — 2026-10-17

함급별 기본형을 먼저 채운다는 사용자 결정에 따라 기존 요격함 모델을 공유하던 T4 소형함을 독립 Blender/GLB로 분리했다. 이미지 생성 참고 → 기능 배치와 기본형 → 게임 연결 순서다. 기존 20모델을 보존하며 전체 34함급은 24모델을 사용한다.

| 함급 | ID | 기본형 | PD / 미사일 / AM / 프로브 | 삼각형 / 렌더 메시 |
|---|---|---|---:|---:|
| 카노푸스 정찰함 | earth_recon_t4 | 뭉툭한 센서 선수, 넓은 가로 날개와 센서 귀, 상부 프로브 5개 | 2 / 4 / 1 / 5 | 7,524 / 5 |
| 사크르 요격함 | interceptor_t4 | 전진익, 넓게 벌어진 추진기, 높은 쌍미익 | 2 / 4 / 3 / 2 | 6,496 / 5 |
| 포보스 강습함 | mars_assault_t4 | 삼각익, 두꺼운 하부 격리실, 어뢰 저장 구획 | 1 / 4 / 5 / 0 | 5,968 / 3 |
| 하위헌스 요격함 | mars_interceptor_t4 | 좁은 동체, 앞 카나드·후퇴익, 낮게 벌어진 미익 | 1 / 4 / 3 / 2 | 6,224 / 3 |

네 함의 전방 고정포는 단일 포구이며 가속 레일 2개로 표현했다. 주포/미사일/AM/프로브의 독립 탄약, 반물질 격리 위험, 포보스의 프로브 미탑재를 유지했다. HP·모듈 크기·장갑 두께·실드·탄도·기동·센서 수치를 보존했다. 외피·모듈 중심·포구·PD 위치는 새 형상과 함께 바뀌므로 실전 피탄 면적과 사선 가림의 밸런스는 후속 확인이 필요하다.

## 제작물

- `art/reference/20261017_t4_small/`: 내장 image_gen 참고 원본과 정확한 프롬프트. 그림의 잘못된 전방 추진구는 배제했다.
- `art/blender/<ID>.blend`: 편집 가능한 원본. 부품 128 / 114 / 100 / 108개, 별도 모듈 가이드 및 발사구 마커.
- `assets/ships/<ID>.glb`와 manifest: 정적 외피 병합, 회전·고각 PD 리그, 주포·미사일·AM·프로브·엔진·RCS 연결.
- `data/model_layouts/<ID>.json`: 기본형의 링·날개 꼭짓점과 피해 구획, 모듈/장비 좌표. tier variant 생성과 연결.
- `assets/ui/ships/<ID>.png`: 실제 Godot로 렌더한 격납고·연구 이미지. 이 폴더의 `*-hangar.png`는 1600×900 실제 격납고 캡처.
- `*_hero/underside/top/rear.png`: 최종 모델 검토 렌더. 패널선·볼트·최종 텍스처와 수납 애니메이션은 미포함.

## 검증

- `preservation-audit.json`: 기존 파일 90개 해시 일치, 대상 variant 4개와 manifest만 변경 허용. 이전 20종 Blender/GLB/초상화와 다른 함급 데이터를 보존했다. 성능 수치 및 모듈 크기 비교 통과.
- 별도 디렉터리에서 tier variant 22개를 재생성하여 현재 정의와 일치함을 확인했다. 새 T4 형상이 기존 T3/T5에 섞이지 않는다.
- `sim-tier-branches.log`: 398개 통과. T4 실제 모듈 관통 32경로, 큰 double 좌표에서 회전한 카노푸스의 5개 사출구와 지도 목적지, 네 함의 미사일/AM 발사 및 프로브 독립 소모, 격리·복구·진행·편대 검사 포함.
- `dotnet-build.log`: 경고 0 / 오류 0.
- 초기 카노푸스의 프로브 받침대와 카트리지 앞면이 탐색기 면을 가리는 문제를 찾아 받침대 높이 및 헤드 위치를 수정했다. `geometry-audit.json`: 91개 좌표 오차 0, 함별 연결 그룹 1개, 핀 좌우 대칭, 주기관 8개 및 프로브 9개 발사면 광선 가시성 검사 통과. AABB 연결 검사는 틈 후보 탐지이므로 최종 상·하·후방 렌더 검토로 보완했다.
- 최종 좌표 검토에서 실드 방출기 외형과 피해 모듈의 앞뒤 위치를 일치시켰다. 원본 좌표 검사와 실제 실드 모듈 관통 검사를 추가했다.
- `release-verification.json`: 실제 배포 EXE 15묶음/7,938개 통과(모델 6,664개), ZIP CRC·SHA-256 및 최종 ExportRelease DLL 일치 확인. 네 함의 1600×900 실제 격납고 캡처도 최종 모델로 갱신해 확인했다. 최종 빌드 이름은 `SpaceFleet-20261017-t4-small-v2-Windows-x64`다. 실행파일을 옮길 때는 ZIP 전체를 풀어 실행한다.

모델의 실전 승률·직접 조종 장시간 플레이·240척 FPS는 이번 차수에서 측정하지 않았다. 다음 기본형 대상은 T5 10종이며, 표면 마무리는 전체 기본형 이후다.

## 재작업

`tools/author-t4-small-layouts.py` → Blender에서 `tools/build-t4-small-models.py -- --root <저장소> --render` → `tools/audit-t4-small-models.py -- --root <저장소>` → `tools/export-menu-art.ps1 -Models earth_recon_t4,interceptor_t4,mars_assault_t4,mars_interceptor_t4` 순서.

Blender 수동 수정 이후에는 생성기로 덮어쓰지 않고 `tools/export-ship-models.py -- --root <저장소> --ship <ID>`로 내보낸다. `tools/check-t4-small-assets.py`는 이전 자산·수치 보존과 tier 재생성을 검사한다. 캡처의 APPDATA는 `shots/t4-small-model-user`로 분리하여 실제 사용자 저장을 유지한다.
