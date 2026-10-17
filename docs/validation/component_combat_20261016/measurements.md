# 교체 무장 측정표

자동 생성: `python docs/validation/component_combat_20261016/analyze.py`

시험 조건과 해석의 한계는 README.md를 함께 읽는다.

## 연속 사격 — 300초, 무장 기본 2 PIP

| 함체 | 형식 | 발수 | 발사 에너지 합 | 잔탄 | 소진 시각(초) | 과열(초) |
|---|---|---:|---:|---:|---:|---:|
| battleship | main-single | 285 | 638400 | 435 | — | 0.0 |
| battleship | main-triple | 720 | 772800 | 0 | 250.2 | 0.0 |
| battleship | stock | 570 | 798000 | 150 | — | 0.0 |
| escort | main-single | 400 | 384000 | 240 | — | 0.0 |
| escort | main-triple | 640 | 294400 | 0 | 173.0 | 18.5 |
| escort | stock | 640 | 384000 | 0 | 238.5 | 0.0 |
| interceptor | main-precision | 200 | 57200 | 0 | 162.5 | 0.0 |
| interceptor | main-rapid | 200 | 35200 | 0 | 92.9 | 0.0 |
| interceptor | stock | 200 | 44000 | 0 | 122.7 | 0.0 |
| mars_battleship | main-single | 116 | 371200 | 364 | — | 0.0 |
| mars_battleship | main-triple | 348 | 533600 | 132 | — | 0.0 |
| mars_battleship | stock | 232 | 464000 | 248 | — | 0.0 |
| mars_escort | main-single | 300 | 316800 | 340 | — | 0.0 |
| mars_escort | main-triple | 640 | 323840 | 0 | 212.0 | 0.0 |
| mars_escort | stock | 600 | 396000 | 40 | — | 0.0 |
| mars_interceptor | main-precision | 200 | 52000 | 0 | 189.1 | 0.0 |
| mars_interceptor | main-rapid | 200 | 32000 | 0 | 106.1 | 0.0 |
| mars_interceptor | stock | 200 | 40000 | 0 | 142.6 | 0.0 |

## 장갑 — 새 표적의 모듈 중심을 향한 고정 광선, 실드 제외

| 함체 | 형식 | 관통 경로/전체 | 발당 모듈 피해 | 포탑 일제 모듈 피해 환산 | 발당 구조 피해 |
|---|---|---:|---:|---:|---:|
| battleship | main-single | 105/105 | 605.9 | 605.9 | 288.1 |
| battleship | main-triple | 105/105 | 313.7 | 941.2 | 126.7 |
| battleship | stock | 105/105 | 411.3 | 822.5 | 172.0 |
| escort | main-single | 105/105 | 350.2 | 350.2 | 146.3 |
| escort | main-triple | 104/105 | 142.3 | 426.9 | 55.1 |
| escort | stock | 105/105 | 201.4 | 402.7 | 79.6 |
| interceptor | main-precision | 86/90 | 116.3 | 116.3 | 61.2 |
| interceptor | main-rapid | 76/90 | 68.0 | 68.0 | 30.4 |
| interceptor | stock | 86/90 | 90.0 | 90.0 | 42.5 |
| mars_battleship | main-single | 120/120 | 801.3 | 801.3 | 233.9 |
| mars_battleship | main-triple | 120/120 | 382.3 | 1146.8 | 101.9 |
| mars_battleship | stock | 120/120 | 514.2 | 1028.4 | 138.2 |
| mars_escort | main-single | 120/120 | 390.0 | 390.0 | 107.5 |
| mars_escort | main-triple | 102/120 | 149.9 | 449.8 | 41.3 |
| mars_escort | stock | 102/120 | 212.7 | 425.5 | 58.7 |
| mars_interceptor | main-precision | 83/95 | 114.9 | 114.9 | 39.5 |
| mars_interceptor | main-rapid | 79/95 | 63.1 | 63.1 | 18.8 |
| mars_interceptor | stock | 83/95 | 84.6 | 84.6 | 26.8 |

## 실드 — 5km, 실제 포탑 하나/고정포

| 함체 | 형식 | 첫 발사→실드 소진(초) | 발사/명중 | 실드 흡수 합 |
|---|---|---:|---:|---:|
| battleship | main-single | 3.6 | 2/2 | 3264.0 |
| battleship | main-triple | 3.6 | 6/6 | 3264.0 |
| battleship | stock | 3.6 | 4/4 | 3264.0 |
| escort | main-single | 5.1 | 4/4 | 3264.0 |
| escort | main-triple | 3.6 | 9/9 | 3264.0 |
| escort | stock | 3.6 | 6/6 | 3264.0 |
| interceptor | main-precision | 0.8 | 1/1 | 272.0 |
| interceptor | main-rapid | 1.3 | 3/2 | 272.0 |
| interceptor | stock | 1.4 | 3/2 | 272.0 |
| mars_battleship | main-single | 5.5 | 2/2 | 4821.7 |
| mars_battleship | main-triple | 5.5 | 6/4 | 4821.7 |
| mars_battleship | stock | 5.5 | 4/3 | 4821.7 |
| mars_escort | main-single | 8.4 | 5/5 | 4800.0 |
| mars_escort | main-triple | 6.5 | 12/10 | 4800.0 |
| mars_escort | stock | 6.4 | 8/8 | 4800.0 |
| mars_interceptor | main-precision | 1.8 | 2/2 | 400.0 |
| mars_interceptor | main-rapid | 1.9 | 4/3 | 400.0 |
| mars_interceptor | stock | 1.5 | 3/2 | 400.0 |

## 실제 AI 동종함 1:1 — 시드 1·2, 장착 진영 교환

미결은 300초 제한이며 무승부 판정과 다르다. 피해 합은 미사일·부포 등 다른 무장을 포함한다.

| 함체 | 시작 거리(km) | 형식 | 승/패/미결 | 주포 발사/명중 | 주포 미발사 판수 | 모듈 피해 합 |
|---|---:|---|---:|---:|---:|---:|
| battleship | 20 | main-single | 0/4/0 | 30/30 | 0 | 13353.6 |
| battleship | 20 | main-triple | 1/1/2 | 345/344 | 0 | 32262.3 |
| battleship | 20 | stock | 0/0/4 | 140/140 | 0 | 29494.7 |
| battleship | 100 | main-single | 0/3/1 | 104/102 | 0 | 14607.9 |
| battleship | 100 | main-triple | 0/3/1 | 117/114 | 0 | 8446.5 |
| battleship | 100 | stock | 2/2/0 | 124/118 | 0 | 16212.7 |
| escort | 6 | main-single | 1/3/0 | 11/10 | 0 | 1515.0 |
| escort | 6 | main-triple | 2/2/0 | 60/56 | 0 | 3478.0 |
| escort | 6 | stock | 2/2/0 | 28/28 | 0 | 2357.2 |
| escort | 40 | main-single | 2/2/0 | 48/10 | 0 | 907.6 |
| escort | 40 | main-triple | 2/2/0 | 150/82 | 0 | 3449.1 |
| escort | 40 | stock | 2/2/0 | 98/35 | 0 | 2456.1 |
| interceptor | 3 | main-precision | 1/3/0 | 17/14 | 0 | 280.0 |
| interceptor | 3 | main-rapid | 2/2/0 | 28/25 | 0 | 560.2 |
| interceptor | 3 | stock | 2/2/0 | 11/11 | 0 | 415.3 |
| interceptor | 15 | main-precision | 0/0/4 | 0/0 | 4 | 0.0 |
| interceptor | 15 | main-rapid | 0/0/4 | 0/0 | 4 | 0.0 |
| interceptor | 15 | stock | 0/0/4 | 0/0 | 4 | 0.0 |
| mars_battleship | 20 | main-single | 1/2/1 | 128/124 | 0 | 8992.0 |
| mars_battleship | 20 | main-triple | 3/0/1 | 564/539 | 0 | 16201.7 |
| mars_battleship | 20 | stock | 2/2/0 | 238/238 | 0 | 10705.5 |
| mars_battleship | 100 | main-single | 1/2/1 | 66/65 | 0 | 10747.9 |
| mars_battleship | 100 | main-triple | 2/2/0 | 96/92 | 0 | 11156.6 |
| mars_battleship | 100 | stock | 2/2/0 | 68/66 | 0 | 11812.3 |
| mars_escort | 6 | main-single | 1/3/0 | 22/22 | 0 | 1375.7 |
| mars_escort | 6 | main-triple | 2/1/1 | 48/48 | 0 | 2079.7 |
| mars_escort | 6 | stock | 2/2/0 | 58/57 | 0 | 1917.2 |
| mars_escort | 40 | main-single | 2/2/0 | 39/13 | 0 | 1563.1 |
| mars_escort | 40 | main-triple | 2/2/0 | 96/32 | 0 | 1658.9 |
| mars_escort | 40 | stock | 2/2/0 | 124/46 | 0 | 660.0 |
| mars_interceptor | 3 | main-precision | 2/2/0 | 42/20 | 0 | 393.0 |
| mars_interceptor | 3 | main-rapid | 2/2/0 | 32/18 | 0 | 427.8 |
| mars_interceptor | 3 | stock | 2/2/0 | 41/22 | 0 | 333.5 |
| mars_interceptor | 15 | main-precision | 0/0/4 | 0/0 | 4 | 0.0 |
| mars_interceptor | 15 | main-rapid | 0/0/4 | 0/0 | 4 | 0.0 |
| mars_interceptor | 15 | stock | 0/0/4 | 0/0 | 4 | 0.0 |

## 부포 — 6초 노출, 시드 1~4 평균

| 함체 | 표적 | 노출 | 형식 | 발수 평균 | 명중 평균 | 실드 피해 평균 | 미사일 요격/4 | 요격 완료 평균(초) |
|---|---|---|---|---:|---:|---:|---:|---:|
| battleship | 요격함 | fast | pd-barrage | 113.00 | 13.00 | 234.00 | 0/4 | — |
| battleship | 요격함 | fast | pd-longrange | 69.00 | 8.25 | 189.75 | 0/4 | — |
| battleship | 요격함 | fast | stock | 103.00 | 12.00 | 240.00 | 0/4 | — |
| battleship | 요격함 | near | pd-barrage | 198.00 | 32.50 | 585.00 | 0/4 | — |
| battleship | 요격함 | near | pd-longrange | 127.00 | 20.25 | 465.75 | 0/4 | — |
| battleship | 요격함 | near | stock | 165.00 | 27.00 | 540.00 | 0/4 | — |
| battleship | 요격함 | outer | pd-barrage | 0.00 | 0.00 | 0.00 | 0/4 | — |
| battleship | 요격함 | outer | pd-longrange | 90.00 | 4.75 | 109.25 | 0/4 | — |
| battleship | 요격함 | outer | stock | 0.00 | 0.00 | 0.00 | 0/4 | — |
| battleship | 미사일 | fast | pd-barrage | 20.00 | 0.00 | 0.00 | 4/4 | 1.2 |
| battleship | 미사일 | fast | pd-longrange | 5.50 | 0.00 | 0.00 | 4/4 | 1.0 |
| battleship | 미사일 | fast | stock | 8.75 | 0.00 | 0.00 | 4/4 | 0.9 |
| battleship | 미사일 | near | pd-barrage | 18.00 | 0.00 | 0.00 | 4/4 | 1.3 |
| battleship | 미사일 | near | pd-longrange | 5.50 | 0.00 | 0.00 | 4/4 | 1.2 |
| battleship | 미사일 | near | stock | 5.50 | 0.00 | 0.00 | 4/4 | 1.0 |
| battleship | 미사일 | outer | pd-barrage | 0.00 | 0.00 | 0.00 | 0/4 | — |
| battleship | 미사일 | outer | pd-longrange | 8.75 | 0.00 | 0.00 | 4/4 | 1.7 |
| battleship | 미사일 | outer | stock | 0.00 | 0.00 | 0.00 | 0/4 | — |
| escort | 요격함 | fast | pd-barrage | 92.00 | 12.25 | 220.50 | 0/4 | — |
| escort | 요격함 | fast | pd-longrange | 63.00 | 8.25 | 189.75 | 0/4 | — |
| escort | 요격함 | fast | stock | 87.00 | 11.50 | 230.00 | 0/4 | — |
| escort | 요격함 | near | pd-barrage | 143.00 | 25.25 | 454.50 | 0/4 | — |
| escort | 요격함 | near | pd-longrange | 91.00 | 18.75 | 431.25 | 0/4 | — |
| escort | 요격함 | near | stock | 119.00 | 23.00 | 460.00 | 0/4 | — |
| escort | 요격함 | outer | pd-barrage | 0.00 | 0.00 | 0.00 | 0/4 | — |
| escort | 요격함 | outer | pd-longrange | 93.00 | 5.75 | 132.25 | 0/4 | — |
| escort | 요격함 | outer | stock | 0.00 | 0.00 | 0.00 | 0/4 | — |
| escort | 미사일 | fast | pd-barrage | 20.00 | 0.00 | 0.00 | 4/4 | 1.2 |
| escort | 미사일 | fast | pd-longrange | 5.50 | 0.00 | 0.00 | 4/4 | 0.7 |
| escort | 미사일 | fast | stock | 5.50 | 0.00 | 0.00 | 4/4 | 0.6 |
| escort | 미사일 | near | pd-barrage | 16.00 | 0.00 | 0.00 | 4/4 | 1.6 |
| escort | 미사일 | near | pd-longrange | 5.50 | 0.00 | 0.00 | 4/4 | 1.5 |
| escort | 미사일 | near | stock | 5.50 | 0.00 | 0.00 | 4/4 | 1.3 |
| escort | 미사일 | outer | pd-barrage | 0.00 | 0.00 | 0.00 | 0/4 | — |
| escort | 미사일 | outer | pd-longrange | 8.75 | 0.00 | 0.00 | 4/4 | 1.6 |
| escort | 미사일 | outer | stock | 0.00 | 0.00 | 0.00 | 0/4 | — |
| interceptor | 요격함 | fast | pd-barrage | 2.00 | 0.25 | 4.50 | 0/4 | — |
| interceptor | 요격함 | fast | pd-longrange | 2.00 | 0.25 | 5.75 | 0/4 | — |
| interceptor | 요격함 | fast | stock | 2.00 | 0.25 | 5.00 | 0/4 | — |
| interceptor | 요격함 | near | pd-barrage | 33.00 | 1.75 | 31.50 | 0/4 | — |
| interceptor | 요격함 | near | pd-longrange | 21.00 | 1.75 | 40.25 | 0/4 | — |
| interceptor | 요격함 | near | stock | 27.00 | 2.00 | 40.00 | 0/4 | — |
| interceptor | 요격함 | outer | pd-barrage | 0.00 | 0.00 | 0.00 | 0/4 | — |
| interceptor | 요격함 | outer | pd-longrange | 21.00 | 0.75 | 17.25 | 0/4 | — |
| interceptor | 요격함 | outer | stock | 0.00 | 0.00 | 0.00 | 0/4 | — |
| interceptor | 미사일 | fast | pd-barrage | 2.00 | 0.00 | 0.00 | 0/4 | — |
| interceptor | 미사일 | fast | pd-longrange | 2.00 | 0.00 | 0.00 | 0/4 | — |
| interceptor | 미사일 | fast | stock | 2.00 | 0.00 | 0.00 | 0/4 | — |
| interceptor | 미사일 | near | pd-barrage | 20.00 | 0.00 | 0.00 | 4/4 | 3.8 |
| interceptor | 미사일 | near | pd-longrange | 8.75 | 0.00 | 0.00 | 4/4 | 2.8 |
| interceptor | 미사일 | near | stock | 8.75 | 0.00 | 0.00 | 4/4 | 2.2 |
| interceptor | 미사일 | outer | pd-barrage | 0.00 | 0.00 | 0.00 | 0/4 | — |
| interceptor | 미사일 | outer | pd-longrange | 12.50 | 0.00 | 0.00 | 3/4 | 3.0 |
| interceptor | 미사일 | outer | stock | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_battleship | 요격함 | fast | pd-barrage | 56.00 | 6.50 | 117.00 | 0/4 | — |
| mars_battleship | 요격함 | fast | pd-longrange | 35.00 | 4.25 | 97.75 | 0/4 | — |
| mars_battleship | 요격함 | fast | stock | 52.00 | 6.25 | 125.00 | 0/4 | — |
| mars_battleship | 요격함 | near | pd-barrage | 98.00 | 15.75 | 283.50 | 0/4 | — |
| mars_battleship | 요격함 | near | pd-longrange | 63.00 | 9.00 | 207.00 | 0/4 | — |
| mars_battleship | 요격함 | near | stock | 81.00 | 12.75 | 255.00 | 0/4 | — |
| mars_battleship | 요격함 | outer | pd-barrage | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_battleship | 요격함 | outer | pd-longrange | 30.00 | 1.00 | 23.00 | 0/4 | — |
| mars_battleship | 요격함 | outer | stock | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_battleship | 미사일 | fast | pd-barrage | 20.00 | 0.00 | 0.00 | 4/4 | 1.7 |
| mars_battleship | 미사일 | fast | pd-longrange | 5.50 | 0.00 | 0.00 | 4/4 | 1.2 |
| mars_battleship | 미사일 | fast | stock | 5.50 | 0.00 | 0.00 | 4/4 | 1.0 |
| mars_battleship | 미사일 | near | pd-barrage | 18.00 | 0.00 | 0.00 | 4/4 | 1.8 |
| mars_battleship | 미사일 | near | pd-longrange | 5.50 | 0.00 | 0.00 | 4/4 | 1.5 |
| mars_battleship | 미사일 | near | stock | 5.50 | 0.00 | 0.00 | 4/4 | 1.2 |
| mars_battleship | 미사일 | outer | pd-barrage | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_battleship | 미사일 | outer | pd-longrange | 8.75 | 0.00 | 0.00 | 4/4 | 2.6 |
| mars_battleship | 미사일 | outer | stock | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_escort | 요격함 | fast | pd-barrage | 30.00 | 4.00 | 72.00 | 0/4 | — |
| mars_escort | 요격함 | fast | pd-longrange | 19.00 | 2.00 | 46.00 | 0/4 | — |
| mars_escort | 요격함 | fast | stock | 31.00 | 3.75 | 75.00 | 0/4 | — |
| mars_escort | 요격함 | near | pd-barrage | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_escort | 요격함 | near | pd-longrange | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_escort | 요격함 | near | stock | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_escort | 요격함 | outer | pd-barrage | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_escort | 요격함 | outer | pd-longrange | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_escort | 요격함 | outer | stock | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_escort | 미사일 | fast | pd-barrage | 18.00 | 0.00 | 0.00 | 4/4 | 2.8 |
| mars_escort | 미사일 | fast | pd-longrange | 5.50 | 0.00 | 0.00 | 4/4 | 1.7 |
| mars_escort | 미사일 | fast | stock | 5.50 | 0.00 | 0.00 | 4/4 | 1.4 |
| mars_escort | 미사일 | near | pd-barrage | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_escort | 미사일 | near | pd-longrange | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_escort | 미사일 | near | stock | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_escort | 미사일 | outer | pd-barrage | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_escort | 미사일 | outer | pd-longrange | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_escort | 미사일 | outer | stock | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_interceptor | 요격함 | fast | pd-barrage | 1.00 | 0.25 | 4.50 | 0/4 | — |
| mars_interceptor | 요격함 | fast | pd-longrange | 1.00 | 0.25 | 5.75 | 0/4 | — |
| mars_interceptor | 요격함 | fast | stock | 1.00 | 0.25 | 5.00 | 0/4 | — |
| mars_interceptor | 요격함 | near | pd-barrage | 26.00 | 1.75 | 31.50 | 0/4 | — |
| mars_interceptor | 요격함 | near | pd-longrange | 17.00 | 1.25 | 28.75 | 0/4 | — |
| mars_interceptor | 요격함 | near | stock | 22.00 | 1.25 | 25.00 | 0/4 | — |
| mars_interceptor | 요격함 | outer | pd-barrage | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_interceptor | 요격함 | outer | pd-longrange | 17.00 | 0.50 | 11.50 | 0/4 | — |
| mars_interceptor | 요격함 | outer | stock | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_interceptor | 미사일 | fast | pd-barrage | 1.00 | 0.00 | 0.00 | 0/4 | — |
| mars_interceptor | 미사일 | fast | pd-longrange | 1.00 | 0.00 | 0.00 | 0/4 | — |
| mars_interceptor | 미사일 | fast | stock | 1.00 | 0.00 | 0.00 | 0/4 | — |
| mars_interceptor | 미사일 | near | pd-barrage | 20.00 | 0.00 | 0.00 | 4/4 | 4.7 |
| mars_interceptor | 미사일 | near | pd-longrange | 8.75 | 0.00 | 0.00 | 4/4 | 3.3 |
| mars_interceptor | 미사일 | near | stock | 8.75 | 0.00 | 0.00 | 4/4 | 2.7 |
| mars_interceptor | 미사일 | outer | pd-barrage | 0.00 | 0.00 | 0.00 | 0/4 | — |
| mars_interceptor | 미사일 | outer | pd-longrange | 11.50 | 0.00 | 0.00 | 3/4 | 3.6 |
| mars_interceptor | 미사일 | outer | stock | 0.00 | 0.00 | 0.00 | 0/4 | — |

## 10:10 편대 혼성 매칭 — 시드 1, 최대 300초 기능 시험

| 대표 함체 | 형식 | 전체 척수 | 종료 시각(초) | 소유 편대 주포 발사/명중 | 잔탄 | 격침 |
|---|---|---:|---:|---:|---:|---:|
| battleship | main-single | 80 | 300.0 | 5/3 | 1995 | 0/3 |
| battleship | main-triple | 80 | 300.0 | 9/0 | 1991 | 0/3 |
| battleship | stock | 80 | 300.0 | 0/0 | 2000 | 1/3 |
| mars_interceptor | main-precision | 88 | 300.0 | 40/26 | 960 | 2/5 |
| mars_interceptor | main-rapid | 88 | 300.0 | 127/86 | 873 | 1/5 |
| mars_interceptor | stock | 88 | 300.0 | 91/82 | 909 | 1/5 |
