# 탄막형 재검증 수치

통제된 근거리 유입 시험이다. 조건과 한계는 README.md를 참고한다.

| 형식 | 시험 수 | 투입 | 요격 | 함선 명중 | 방어함 격침 |
|---|---:|---:|---:|---:|---:|
| stock | 192 | 2304 | 420 | 1733 | 50 |
| barrage-before | 192 | 2304 | 192 | 1961 | 50 |
| pd-barrage | 192 | 2304 | 478 | 1683 | 49 |
| pd-longrange | 192 | 2304 | 297 | 1856 | 50 |

함선 명중은 BattleLog에 피해 표적이 기록된 명중이다. 파괴된 함체 등에 무효 폭발한 탄을 포함하지 않아 요격+명중이 투입과 같지 않을 수 있다.

## 함체·속도·탄종별 요격 수

각 행은 시드 1~8 × 12발 = 96발. 기본형/수정 전/후/원거리형 순서.

| 함체 | 탄종 | 속도(m/s) | 기본 | 탄막 이전 | 탄막 수정 | 원거리 |
|---|---|---:|---:|---:|---:|---:|
| battleship | heavy | 650 | 21 | 16 | 22 | 15 |
| battleship | heavy | 1800 | 0 | 0 | 0 | 0 |
| battleship | light | 650 | 94 | 52 | 95 | 84 |
| battleship | light | 1800 | 31 | 6 | 34 | 14 |
| escort | heavy | 650 | 20 | 15 | 22 | 14 |
| escort | heavy | 1800 | 1 | 0 | 2 | 0 |
| escort | light | 650 | 84 | 44 | 91 | 71 |
| escort | light | 1800 | 33 | 7 | 39 | 24 |
| interceptor | heavy | 650 | 0 | 0 | 0 | 0 |
| interceptor | heavy | 1800 | 0 | 0 | 0 | 0 |
| interceptor | light | 650 | 3 | 0 | 6 | 0 |
| interceptor | light | 1800 | 3 | 0 | 5 | 0 |
| mars_battleship | heavy | 650 | 7 | 6 | 7 | 5 |
| mars_battleship | heavy | 1800 | 0 | 0 | 0 | 0 |
| mars_battleship | light | 650 | 44 | 21 | 58 | 26 |
| mars_battleship | light | 1800 | 10 | 1 | 11 | 3 |
| mars_escort | heavy | 650 | 7 | 4 | 10 | 3 |
| mars_escort | heavy | 1800 | 0 | 0 | 0 | 0 |
| mars_escort | light | 650 | 36 | 18 | 46 | 25 |
| mars_escort | light | 1800 | 26 | 2 | 30 | 13 |
| mars_interceptor | heavy | 650 | 0 | 0 | 0 | 0 |
| mars_interceptor | heavy | 1800 | 0 | 0 | 0 | 0 |
| mars_interceptor | light | 650 | 0 | 0 | 0 | 0 |
| mars_interceptor | light | 1800 | 0 | 0 | 0 | 0 |

## 단일 경량 미사일 — 500m 정지 노출, 시드 1~4

| 함체 | 형식 | 요격/4 | 평균 완료(초) | 평균 발수 |
|---|---|---:|---:|---:|
| battleship | stock | 4/4 | 1.008 | 5.5 |
| battleship | pd-barrage | 4/4 | 0.954 | 5.5 |
| battleship | pd-longrange | 4/4 | 1.212 | 5.5 |
| escort | stock | 4/4 | 1.262 | 5.5 |
| escort | pd-barrage | 4/4 | 1.217 | 5.5 |
| escort | pd-longrange | 4/4 | 1.508 | 5.5 |
| interceptor | stock | 4/4 | 2.233 | 8.8 |
| interceptor | pd-barrage | 4/4 | 1.942 | 8.8 |
| interceptor | pd-longrange | 4/4 | 2.771 | 8.8 |
