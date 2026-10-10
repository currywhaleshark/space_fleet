# 함선 부품 정렬 — 2026-10-10

사용자가 지적한 전함 첫 상부 주포, 호위함 하부 포탑, 요격기 미익 상단의 어긋남을 Blender 원본에서 수정하고 게임 GLB에 반영했다.

- **전함:** 첫 포탑의 외피 높이를 실제 6 m 포신 축에 맞췄다. 다른 포탑처럼 16.8 m 높이를 가정하던 외피가 원인이었다. 주포의 보이는 포신 뒤쪽을 고저 회전축까지 연장하고 축 연결부를 추가했다. 전함·호위함 주포 외피 전면/지붕에 고각 회전 공간을 가공했다.
- **호위함:** 하부 포탑과 선체 사이에 장갑 받침을 추가했다. 하부 PD와 갑판·함교 층 사이의 받침도 보완했다.
- **요격기:** 우측 미익을 기준으로 좌측 형상을 정확히 반사해 상단 보강재와 맞췄다. 등쪽 장비판, 하부 PD, 미사일 고정부의 지지부를 보완했다.
- **공통:** 갑판/하부 장갑의 안쪽 받침, 함교 연결부, PD·VLS·실드 발생기 받침, 배관 클램프, RCS 연결부를 정리했다. 방열 채널과 냉각 슬롯을 해당 표면에 밀착시켰다. 식별 문자와 띠는 단차를 가로질러 휘지 않도록 평평한 표면에 배치했다.

## 원본과 재현

`art/blender/{battleship,escort,interceptor}.blend`에 개별 부품·리그·가이드를 보존했다. `tools/align-ship-parts.py`는 기존 원본을 읽고 보정 이력별로 한 번 적용하며, 모든 기존 EMPTY의 월드 변환이 수정 전과 같은지 검사한다. 모듈·포구·미사일·PD·엔진·RCS 소켓 및 주포 리그 회전축이 여기에 포함된다.

공유 보정은 `tools/fleet_model_alignment.py`에 있고 기본 제작 스크립트에서도 호출한다. 재생성 시 같은 오류가 되살아나지 않도록 연결했으며, 저장된 수작업 원본은 `export-ship-models.py`로 내보낸다. 보정 도구를 실행하기 전 원본은 별도 백업하는 것이 좋다. 이번 작업 전 원본과 GLB는 `shots/model-align-before/source/`에 보존했다.

```powershell
$blender = 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe'
& $blender --background --python-exit-code 1 --python tools/align-ship-parts.py -- --root $PWD.Path
& $blender --background --python-exit-code 1 --python tools/export-ship-models.py -- --root $PWD.Path
& $blender --background --python-exit-code 1 --python tools/audit-ship-models.py -- --root $PWD.Path --output shots/model-align-final --check --render
```

## 검증

- Blender 5.1.1에서 세 함선의 고정 부품 연결 후보, 주포 외피 안의 포신 축 높이, 포신 뒤쪽과 고저 회전축의 연결, 요격기 미익의 정확한 좌우 대칭 검사 통과.
- AABB 후보 검사에서 세 함선 모두 단일 연결 그룹. 의도적으로 표면에서 미세하게 띄우는 평면 문자는 제외한다. 이 검사는 모든 메시의 실제 접촉을 증명하지 않으므로 상·하부/근접 렌더도 확인했다.
- 상·하부, 전함 첫 포탑, 호위함 하부 포탑, 요격기 미익, 주포 55° 고각을 포함한 최종 10개 렌더는 `shots/model-align-final/`. 수정 전 8개 렌더는 `shots/model-align-before/`.
- 새 Windows 실행본의 장면 39·모델 681·전투 효과 36·자함 음향 116·주변 음향 104, 합계 **976개** 검사 통과. 모델 검사에는 주포/PD 실제 자동 추적, 수평/고저 회전, 반동, 포구 보간 좌표와 발사 이펙트가 포함된다. 빌드 경고·오류 0.

| 모델 | 삼각형 | 메시 그룹 | 고유 재질 |
|---|---:|---:|---:|
| 전함 | 44,574 | 23 | 12 |
| 호위함 | 37,012 | 13 | 12 |
| 요격기 | 14,108 | 5 | 10 |

받침과 주포 연결부로 삼각형 수가 증가했다. 정적 선체·회전 그룹별 병합 구조와 재질 수는 유지했다. 이번 수정은 시뮬레이션 코드나 `data/ships` 수치를 변경하지 않는다. 새 외형을 사용한 별도 FPS 재계측은 하지 않았다.

실행본: `builds/SpaceFleet-20261010-model-aligned-Windows-x64/SpaceFleet.exe` 및 같은 이름의 ZIP. 기존 최적화 실행본도 보존했다.
