# 구현 상태

최종 갱신: 2026-09-21 (Asia/Seoul)

## 완료

- RF4Overlay의 Bite Alarm 캡처·감지·상태 머신·알람·입력 확인 로직을 독립 프로젝트로 분리.
- 상태, 알람 소리, 음량, Test, 전역 단축키, 시작/정지만 남긴 compact WPF UI.
- RF4 창 자동 연결 및 재연결, 시스템 트레이 열기/Toggle/종료.
- `%LOCALAPPDATA%\RF4BiteAlarm\settings.json` 설정 저장.
- 곰/알람시계 이미지를 투명 배경과 최소 여백으로 편집하고 16–256px 멀티사이즈 ICO 생성.
- Windows x64 self-contained 단일 EXE 게시 구성.

## 검증

- `dotnet build RF4BiteAlarm.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test RF4BiteAlarm.slnx --no-build --no-restore`: 15개 통과.

## 알려진 제한

- 감지기는 기존 RF4Overlay와 동일하게 제공된 RF4 포획 UI 표본 및 normalized ROI를 사용한다.
- 실제 RF4의 추가 해상도·날씨·UI 배율은 실물 검증이 필요하다.
