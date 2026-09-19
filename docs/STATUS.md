# 현재 구현 상태

최종 갱신: 2026-09-19 (Asia/Seoul)

## 완료

- Phase 1: 모듈형 WPF 솔루션 및 개발환경.
- Feature runtime: 비동기 실행, Feature별 명령 직렬화, 중복 시작/정지,
  취소, 종료 대기, 상태 통지, Faulted 오류 격리.
- Global Hotkey: 실제 Windows keyboard hook, down/up·repeat 억제,
  정확한 modifier 조합, 반복/혼합 sequence, 최대 key 간격, prefix/중복 충돌 거절.
- Player Activity: keyboard/mouse hook을 공유하며 모든 injected 입력을 실제 활동에서 제외.
- UI/Tray/Hotkey: 같은 FeatureCommandDispatcher 경로.
- Tray: 열기, 세 Feature Toggle, Exit.
- Audio: NAudio 2.2.1, 일회성 tick/alarm, 음량, 정지/재생, Feature별 voice 정리.
- Metronome: 소리 주기 0.2–60초와 BPM 1–300 양방향 연동, 소수 주기 보존,
  실행 중 설정 변경, 목표 시각 기반 스케줄, 밀린 박자 건너뛰기, Start/Stop/Toggle.
- 설정: 실제 키 기록 UI, 최대 8-stroke, 간격 100–5000ms, JSON 저장/복원.
  비동기 단일 writer가 최신 설정을 저장하며 앱 종료 시 저장 완료를 기다림.
- WGC: D3D11/WinRT, HWND/PID 탐색, 프레임 CPU 복사, resize pool 재생성,
  최소화/창 종료/프레임 중단 및 오류 후 재연결, 자원 정리.
- 진단 UI: 연결 상태, 프레임 크기/수, 수동 PNG 저장. 저장 작업은 UI thread 밖에서 수행.
- Bite Alarm: 제공된 normalized ROI만 검사하는 흰색 원형/내부 물고기 특징 감지기,
  기본 3개 연속 프레임 판정, 공용 WGC 프레임 구독, 반복 알람 세션과 Feature runtime 연결.
  사용자 입력 확인, 안정적 UI 소멸 후 재무장, 구독/오디오/취소 정리.
- Auto Pilking: 기본 우클릭 또는 단일 keyboard key를 0.1–60초 범위의 누름/해제 시간으로 반복.
  기본값은 3.0초 누름/3.0초 해제이며 UI spinner는 0.1초 단위로 조절한다.
  중지/종료가 누름 중 발생해도 `SendInput` key/button-up을 `finally`에서 전송하고,
  injected 입력은 기존 사용자 활동/Hotkey 관찰에서 제외한다.

## 최신 검증

- `dotnet build`: 성공, 경고 0 / 오류 0.
- `dotnet test`: **43개 통과** (순수 로직 33, Windows 통합 10), 실패/건너뜀 0.
- Runtime: 중복 start/stop, 동시 Toggle, 대기 명령 취소, 취소 callback 오류,
  실행 완료/실패와 Stop 경합, shutdown, observer 오류 격리.
- Hotkey: 반복/혼합 sequence, repeat 억제, modifier, timeout, prefix/중복,
  injected 제외, immutable 설정 및 JSON round-trip.
- Metronome: 목표 시각 10,000박자 계산, 지연 건너뛰기, 시작/정지/재시작,
  주기/BPM 변환, 잘못된 범위 거절, 기존 BPM 설정 이관, voice dispose.
- Bite: 즉시/반복 알람, acknowledge, 동일 UI 중복 억제, 안정적 소멸,
  캡처 실패, 실제 fake detector 세션, 취소/구독 해제. 제공된 POSITIVE 검출,
  NEGATIVE 4장 미검출, 1280×720/2560×1440 ROI 변환, 3-frame debounce.
- Windows: 실제 파란 WPF 창을 빨간 창으로 가린 상태의 WGC 픽셀 확인,
  resize, 창 종료, Hook 중복 시작/정리, 실제 audio voice(음량 0), 설정 최종 저장.
- computer-use: Metronome 시작/정지/재시작 UI, 입력 모니터 초기화,
  NumPad1 세 번 기록·저장, BPM 150 적용과 설정 파일 저장 확인.
- computer-use: 주기 입력 UI 배치, 0.7초 입력 시 85.714 BPM 연동 및
  기존 0.5초/120 BPM 복원, 앱 정상 종료 확인.
- computer-use: RF4 부재 상태 표시, 진단 PNG 저장, 실행 중 앱 정상 종료 및
  프로세스 제거 확인. 테스트 변경은 BPM 120 / Ctrl+F8로 복원.
- computer-use: Bite Alarm 제품 UI 활성화와 프레임 대기 상태의 시작/정지,
  의존 서비스 종료 순서 및 앱 정상 종료 확인.
- Windows UI Automation: Auto Pilking 기본 우클릭, 3.0초 누름/해제,
  누름/해제 spinner의 3.0→3.1→3.0 변화, A 선택→우클릭 복원,
  활성화된 Feature 버튼과 정상 종료 확인.
- Auto Pilking: 기본 설정/범위 검증, 단일 키 전달, 실행 취소 전달 및 JSON 이전/왕복 테스트.
- Windows: 임시 WPF 창에서 실제 `SendInput` 우클릭/F24의 down과 up 수신 확인.
- 사용자 실물 검증: Metronome 소리/BPM/음량, 다른 창에서 물리 단축키 시작/정지,
  Tray 열기/Toggle/종료가 정상 동작함.
- 사용자 실제 RF4 검증: 본체 실행 후 프레임 수가 안정적으로 증가하고,
  다른 창으로 가린 상태에서도 저장한 캡처 이미지가 정상임.

## 발견 후 수정한 문제

- 초기 샌드박스 MSBuild named pipe 접근 거부/NuGet 제한: 승인된 도구 실행 환경에서 검증.
- WGC 정적 테스트 창 resize 후 새 프레임이 오지 않는 타이밍 의존성:
  테스트 창을 주기적으로 갱신하여 가림/resize 검증을 안정화.
- Feature 종료 직전 Stop이 마지막 상태를 덮는 경합: 실행 종료 결과를 따로 보관하고
  Stop 완료 시 최종 상태를 확정. 정상/실패 양쪽 회귀 테스트 추가.
- Steam 스트리밍 창 제목이 RF4였지만 영상은 다른 게임인 실제 사례 확인:
  **스트리밍 창은 RF4 자동 연결에서 제외**. 해당 임시 진단 이미지는 삭제.
  1920×1080 프레임/PNG 저장은 일반 WGC 검증이며 실제 RF4 성공으로 계산하지 않음.
- 시작/종료 경합과 중복 capture dispose 처리 보강.
- 설정/PNG 디스크 작업을 UI thread 밖으로 이동.

## NEEDS_REAL_RF4_TEST

- 실제 RF4에서 Bite Alarm을 켠 뒤 포획 아이콘의 알람, 물리 입력 확인,
  동일 아이콘 중복 방지와 다음 포획 재무장을 검증.
- 다른 날씨/시간/장소의 POSITIVE 샘플과 1920×1080 외 실제 게임 해상도에서 검증.
- RF4 창 크기 변경, 게임 종료/재실행, 최소화 복원, 그래픽 device loss 복구.
- HDR/배율/독점 전체화면/권한 수준이 다른 게임 창에서의 동작.

## 알려진 제한

- 기본 자동 연결 대상 이름: rf4_x64 / rf4 / RussianFishing4. 실제 설치에서 확인 필요.
- 원격 Steam 스트리밍은 제목이 실제 게임을 증명하지 않아 자동 연결하지 않는다.
- Bite Alarm의 반복 간격/음량/연속 프레임 수는 아직 제품 설정 UI에 노출되지 않음.
- POSITIVE 표본이 1장이므로 다른 환경의 실제 포획 UI에서 threshold 보정 가능성이 있음.
- Auto Pilking 공식 일반 정책은 bot/macro를 금지한다. 이 빌드의 실제 게임 사용 근거는
  사용자가 밝힌 별도 허락이며 그 허락의 적용 범위는 프로그램이 독립적으로 검증하지 못한다.
- 실제 `SendInput` down/up은 임시 WPF 창에서 검증했다. RF4에서의 입력 수신과 누름 중 정지는 실물 검증 대기.
- Hotkey는 다른 프로그램에 키를 전달하며 다른 앱의 binding 충돌을 조회하지 않는다.
- Hook timeout에 따른 Windows의 조용한 해제는 플랫폼 제약.
- 모든 injected 입력을 제외하므로 일부 접근성 도구/원격 입력도 물리 활동으로 처리되지 않을 수 있음.
- WGC는 BGRA8 CPU 복사 기반, 약 30fps polling. 5초 무프레임 시 다시 연결하므로
  완전히 정적인 창에서도 재연결이 발생할 수 있음. HDR 전용 처리 없음.
- WindowsTests는 실제 데스크톱/그래픽·오디오 장치가 필요하며 headless 검증 대상이 아니다.
- WindowsTests 실행은 자체 테스트 창을 잠깐 표시한다.
- 프로세스 단일 인스턴스 제한은 아직 없으므로 앱은 한 번만 실행하는 것을 권장.

## 다음 작업

1. 실제 RF4 포획 상황에서 Bite Alarm을 검증하고 POSITIVE 샘플을 추가 수집해 threshold 보정.
2. 반복 간격/음량/연속 프레임 수와 소멸 확인 옵션을 사용자 설정/UI에 노출.
3. 실제 RF4에서 남은 창 resize/재실행/최소화/HDR 호환성 항목을 검증하고 수정.
4. 별도 허락 범위 안에서 실제 RF4가 우클릭/선택 키의 down/up을 수신하는지와
   누름 중 정지 시 즉시 해제되는지 검증.
