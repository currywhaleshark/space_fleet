# 격납고 교체·연구 확인창 (2026-10-17)

## 변경 범위

- 부품 교체/기본형 복귀, 베이 장비 장착/해제, 함급 연구, 영구 개량은 확인 후에만 적용한다.
- 부품 창은 현재 장착 상태에서 다음 상태로 계산한 성능, 발열, 선회, 전력, 질량과 RP 비용/잔액을 표시한다.
- 공유 프리셋의 현재 편대 적용 대상과 영구 개량의 3개 프리셋별 수치를 표시한다.
- 연구 점수 환불 불가를 확인 전에 안내한다. 이미 연구한 부품과 베이 장비에는 비용이 없음을 표시한다.
- 취소에 기본 포커스를 두고 Esc는 확인창만 닫는다. 뒤쪽 메뉴 클릭, 포커스 이탈, 키 반복으로 인한 오작동과 중복 적용을 막는다. 긴 내용은 휠 또는 Page Up/Down·Home/End로 읽을 수 있다.
- 확정 시 기존 데이터 검증을 다시 수행한다. 확인창을 열거나 취소하는 동안 장비/해금/RP/저장 파일은 수정하지 않는다.
- 전투 및 밸런스 규칙, 저장 형식은 변경하지 않는다.

## 검증

별도 테스트 프로필을 사용하며 실제 플레이어 저장은 읽거나 쓰지 않는다.

```powershell
dotnet build SpaceFleet.csproj --no-restore -nologo -v q
powershell -ExecutionPolicy Bypass -File docs/validation/confirmations_20261017/verify.ps1 -Mode checks
powershell -ExecutionPolicy Bypass -File docs/validation/confirmations_20261017/verify.ps1
powershell -ExecutionPolicy Bypass -File tools/export-windows.ps1 -Name SpaceFleet-20261017-confirmations-Windows-x64
```

실제 Godot Forward+ 렌더: [부품 1600×900](component.png), [부품 1280×720](component-small.png), [장비 교체](equipment.png), [함급 연구](research.png), [영구 개량](upgrade.png).

격납고 자동검사는 실제 입력 이벤트로 배경 클릭 차단, Tab 포커스 순환, Esc 취소를 검증한다. 추가로 미확정 상태/취소 시 전체 프로필 불변, 확인 시 잔액 재검증, 두 번 확인해도 한 번만 차감, 기본형 복귀·장비 해제 취소를 검사한다. 기존 연구→출격→전투 보상 및 편대 승계 검사도 유지한다.

배포본 검사 및 무결성 결과는 `release-verification.json`에 기록한다.
