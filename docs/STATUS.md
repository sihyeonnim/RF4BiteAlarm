# 현재 구현 상태

최종 갱신: 2026-09-21 (Asia/Seoul)

## 2026-09-21 포획 아이콘 원형 경계 검증 강화

- 새 보트 오작동 화면과 실제 포획 전체 화면에서 감지 위치의 44×44 ROI만 테스트 자료로 추가.
  전체 화면의 채팅/개인 내용은 테스트 자산에 포함하지 않음.
- 흰 픽셀 비율 조건에 원형 경계 조건 추가: 16방향 중 12방향 이상에서 원 둘레가 안팎보다
  25 이상 밝아야 함. 밝기 절대값만 높이지 않아 기존 어두운 포획 아이콘도 유지.
- 기존/새 포획 ROI는 16/16, 보트 문구 ROI는 2/16 방향 통과. 제공된 보트 정지 화면은 기존
  외곽 흰색 상한으로도 거부되므로, 오작동 순간의 정확한 원인이 이 한 장으로 재현됐다고 보지는 않음.
  새 조건은 문구/밝은 면이 원형 픽셀 비율을 통과하는 경우를 추가로 차단함.
- 기존 음영 포획/새 수면 포획 검출, 5개 음성 샘플 거부, 1280×720/2560×1440 배율,
  원본 보트 크기 1919×1079의 30프레임 반복 거부, 밝은 원판 거부 회귀 테스트 통과.
- 빌드 경고/오류 0. 순수 47개 + Windows 20개 통과. 기존 활성 창 의존 SendInput 테스트 1개는 제외.
- 실행 파일: `.artifacts/bite-ring/bin/RF4Overlay.App/debug/Betterscreen.exe`.
- 다음 확인: 실제 RF4에서 다양한 조명/배경의 포획과 비포획 장면을 검증. 강화된 경계 조건은
  아이콘이 흐리거나 일부 가려진 상태에서는 감지를 보류할 수 있음. 유효한 Git HEAD가 없어 커밋 불가.

## 2026-09-21 compact UI 및 RF4 전면 창 제한

- 승인된 밝은 회색 compact UI를 실제 WPF 화면에 적용. 설명/진단/상태/키 사이 제한은 UI에서 숨기고
  기능 행, 설정 톱니바퀴, 단축키 아이콘, 시작 버튼 중심으로 정리.
- 기능 순서: Bite Alarm → Auto Pilking → Double Click to Holding → RF4 PIP → Metronome.
- Bite Alarm 소리/음량/Test, Auto Pilking 전체 키 목록과 누름/떼기, Double Click의 Shift,
  Metronome 주기/BPM 단일 선택과 음량을 접이식 설정으로 제공.
- 음성 안내를 종료 버튼 바로 왼쪽에 배치하고 표시 기본값을 50%로 변경. 표시 50%를 기존 SAPI 33과
  같은 출력으로 변환하며, 기존 ON·33 설정 파일은 ON·50으로 한 번 이관.
- Auto Pilking 기본값을 우클릭 1.0초 누름/3.5초 떼기로 변경. 이전 버전의 기본 3.0/3.0 설정은
  1.0/3.5로 한 번 이관하고, 다른 사용자 지정 값은 유지.
- Auto Pilking과 Double Click to Holding은 native RF4가 전면 창일 때만 입력. 다른 앱으로 전환하면
  Running 상태는 유지하면서 현재 입력을 해제하고 대기하며, RF4 복귀 후 자동 재개.
- RF4 공식 이용약관을 2026-09-21 재확인. 탐지 회피나 은폐는 추가하지 않음.
- 검증: 별도 산출물 빌드 경고/오류 0, 순수 테스트 47개 통과. Windows 테스트 17개 통과,
  음량 50→33 회귀 테스트 통과. 기존 SendInput 활성 창 테스트 1개는 잠금 해제 데스크톱 조건 불충족으로 실패.
- 실제 새 실행 파일 UI Automation: 기능 순서, 음성 안내 50%, 종료 버튼 정상 동작 확인.
- 실행 파일: `.artifacts/compact-focus/bin/RF4Overlay.App/debug/Betterscreen.exe`.

## 2026-09-21 음성 안내 ON/OFF 및 음량

- 기능 시작·중지 영어 음성 안내의 기본 음량을 33%로 낮춤.
- 메인 화면에 음성 안내 ON/OFF 체크박스와 0–100% 음량 슬라이더 추가.
- 기본값 및 기존 설정 이관값은 ON·33%. 변경값은 settings.json에 저장·복원.
- OFF 상태에서는 새 안내와 이미 queue에 대기 중인 안내를 모두 건너뛰고 슬라이더를 비활성화.
- 실행 중 음량 변경은 다음 안내부터 즉시 적용.
- `.artifacts/voice-controls` 빌드: 경고/오류 0. 순수 테스트 43개, Windows 테스트 16개 통과.
  기존 SendInput 테스트 1개는 테스트 창 활성화 환경 문제로 제외.
- 실제 UI에서 ON·33%, OFF 시 슬라이더 비활성화, ON 복원과 설정 저장 확인.
- 실행 파일: `.artifacts/voice-controls/bin/RF4Overlay.App/debug/Betterscreen.exe`.
- 현재 폴더는 Git 저장소가 아니므로 커밋 불가.

## 2026-09-21 기능 시작·중지 영어 음성 안내

- UI, Tray, 단축키가 공유하는 FeatureCommandDispatcher에 상태 변경 음성 알림을 연결.
- 실제 Running/Stopped 전환에만 Starting/Stopping 영어 문장을 한 번 재생하며 중복 시작·정지는 제외.
  PIP 창 X처럼 기능이 자체 종료되는 경로도 중지 안내 대상.
- Bite Alarm, Metronome, Auto Pilking, Picture in Picture, Left-click Hold에 자연스러운 영어 이름 적용.
- Windows SAPI 음성을 전용 STA worker에서 순서대로 재생하여 기능 명령과 UI thread를 막지 않음.
- Windows에 설치된 en-US 음성(Language 409)을 우선 선택하여 한국어 기본 음성 설정에서도 영어로 안내.
- 앱 종료 시 worker와 COM voice를 정리하며, 앱 종료에 따른 일괄 정지는 별도 음성으로 읽지 않음.
- `.artifacts/feature-voice-english` 빌드: 경고/오류 0. 순수 테스트 42개 및 Windows 테스트 16개 통과.
  기존 SendInput 테스트 1개는 테스트 창 활성화 실패로 통과하지 못함.
- 실제 수정본에서 Metronome 시작/정지와 UI 상태 전환 확인.
- 실행 파일: `.artifacts/feature-voice-english/bin/RF4Overlay.App/debug/Betterscreen.exe`.
- Microsoft Zira(en-US)로 10개 시작·중지 문장을 합친 `feature-announcements-preview.wav` 생성.
- 현재 폴더는 Git 저장소가 아니므로 커밋 불가.

## 2026-09-21 알람 이름 및 Test 버튼

- Default 유지, 추가 소리 이름을 순서대로 알람 1–4로 변경. 저장된 소리 ID와 매핑은 유지.
- Bite Alarm 설정에 Test 버튼 추가: 현재 선택한 소리/음량으로 한 번 재생.
  별도 preview voice를 사용하고 재클릭 시 이전 preview를 정지한 뒤 재생하며 앱 종료 시 해제.
- `.artifacts/alarm-test` 빌드: 경고/오류 0. 테스트: 52개 통과, 기존 Windows 입력 창 활성화 테스트 1개 실패.
- 실행 파일: `.artifacts/alarm-test/bin/RF4Overlay.App/debug/Betterscreen.exe`.
- 다음 확인: 실제 UI에서 선택별 Test 청취. Git 저장소가 아니므로 커밋 불가.

## 2026-09-21 설정 UI 및 Bite Alarm 소리 변경

- Metronome/Auto Pilking/Bite Alarm 카드별 톱니바퀴 설정 펼침·접힘 추가. 초기 상태는 접힘.
- Bite Alarm: Default(기존 소리), sound8, sound0, sound9, 체결수신1 및 별도 음량 저장/복원.
- 제공된 WAV 4개를 embedded resource로 포함하여 Downloads 경로 없이 배포 가능.
- 입력 확인 시 재생 중인 소리는 유지하고 다음 반복만 취소. 명시적 정지/종료 정리는 유지.
- 긴 소리의 반복 trigger가 현재 재생을 끊지 않도록 보호.
- 별도 출력 `.artifacts/ui-settings`에서 build 성공(경고/오류 0).
- 최종 test: 순수 로직 41개 통과, Windows 11개 통과/1개 실패.
  실패는 기존 SendInput 테스트 창 활성화 실패이며 이번 소리 디코딩/재생 테스트는 통과.
- 실제 수정본 실행: 초기 접힘, 3개 톱니바퀴, Bite Alarm 설정 펼침과 음량 70% 확인.
- 기존 실행 앱이 기본 출력 DLL을 잠그므로 수정 실행 파일은
  `.artifacts/ui-settings/bin/RF4Overlay.App/debug/Betterscreen.exe`에 생성.
- 현재 폴더는 Git 저장소가 아니므로 커밋하지 못함.
- 다음 확인: 실제 포획 시 5개 소리 청취 및 입력 이후 현재 소리 완료/다음 반복 중단.

## 2026-09-21 테스트 오류 수정 및 검증

- Bite Alarm 검출에 원 바깥 배경의 흰색 비율 상한을 추가하여 ROI 백색 급증 오검출을 차단.
- 알람 확인 이후의 사용자 입력이 UI 소멸 판정을 초기화하도록 하여 Steam 오버레이를
  닫은 뒤 같은 물고기 UI가 중복 알람을 일으키는 경로를 차단. 제품 소멸 확인 시간도
  0.5초에서 2초로 늘려 오버레이 닫힘 애니메이션과 프레임 지연을 흡수.
- Auto Pilking 누름 시간에 매 사이클 ±0.5초 난수를 적용하고 결과를 0.1–60초로 제한.
- `UnknownAndUnavailableCommandsFail`: LeftClickHold 추가 후 오래된 사용 불가 개수(3) 검증을
  전체 FeatureId 등록, Metronome 준비 상태, 나머지 각 기능의 Unavailable/시작 거절 검증으로 교체.
- `SendInputHoldsAndReleasesRightMouseAndSingleKey`: WPF 초기 작업 완료 후 활성화/포커스와
  mouse capture를 확인하여 포인터 이동에 따른 입력 누락을 방지. 실패 경로에서도 hold 취소와
  button-up 완료를 기다린 뒤 capture/커서/창을 정리한다.
- `dotnet build --no-restore`: 성공, 경고 0 / 오류 0.
- `dotnet test --no-restore`: 순수 로직 35개 통과, Windows 통합 9개 통과 / 2개 실패.
  현재 Codex 제한 실행 환경에서 GetCursorPos가 실패하고 WGC 서비스 접근이 실패했다.
  Windows 입력 테스트 수정의 실제 데스크톱 검증은 아직 완료하지 못했다.
- Bite Alarm 수정 후 검증: 순수 로직 36개 통과, detector 대상 Windows 테스트 5개 통과.
  실행 중인 Betterscreen 프로세스가 App 출력 DLL을 점유하여 전체 솔루션 재빌드는 생략했다.
- 일반 `dotnet build`의 패키지 복원은 사용자 NuGet.Config 읽기 권한 제한으로 실패하여
  기존 복원 결과를 사용했다. 아래 46개 통과 기록은 이전 환경의 검증 이력이다.
- 다음 작업: 잠금 해제된 일반 Windows 데스크톱에서 전체 테스트를 실행하여
  우클릭 취소 해제/F24 down-up 및 WGC 통합 테스트를 확인.

## 완료

- Phase 1: 모듈형 WPF 솔루션 및 개발환경.
- Feature runtime: 비동기 실행, Feature별 명령 직렬화, 중복 시작/정지,
  취소, 종료 대기, 상태 통지, Faulted 오류 격리.
- Global Hotkey: 실제 Windows keyboard hook, down/up·repeat 억제,
  정확한 modifier 조합, 반복/혼합 sequence, 최대 key 간격, prefix/중복 충돌 거절.
- Player Activity: keyboard/mouse hook을 공유하며 모든 injected 입력을 실제 활동에서 제외.
- UI/Tray/Hotkey: 같은 FeatureCommandDispatcher 경로.
- Tray: 열기, 네 Feature Toggle, Exit.
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
  기본값은 1.0초 누름/3.5초 해제이며 UI spinner는 0.1초 단위로 조절한다.
  중지/종료가 누름 중 발생해도 `SendInput` key/button-up을 `finally`에서 전송하고,
  injected 입력은 기존 사용자 활동/Hotkey 관찰에서 제외한다.
- RF4 PIP: 기존 CaptureMonitor 프레임을 재사용하는 480×300 항상 위 WPF 창.
  비율 보존, 크기 조절, 프레임 대기 안내, 창 X/UI/Tray/Hotkey 종료를 지원한다.
  새 기본 단축키는 Ctrl+F11이며 기존 설정에는 누락된 binding만 자동 추가한다.

## 최신 검증

- `dotnet build`: 성공, 경고 0 / 오류 0.
- `dotnet test`: **46개 통과** (순수 로직 35, Windows 통합 11), 실패/건너뜀 0.
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
- PIP: Feature가 공용 frame source와 취소를 presenter에 전달하고, 실제 WPF presenter가
  fake BGRA 프레임을 소비하며 Topmost 창을 연 뒤 취소 시 닫는 통합 테스트.
- Windows UI Automation: 실제 제품에서 RF4 PIP 열기, Topmost, 프레임 대기 메시지,
  두 번째 Toggle 닫기, 창 X 종료 후 재시작, Control+F11 표시 및 정상 종료 확인.
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
- Bite Alarm의 반복 간격/연속 프레임 수는 아직 제품 설정 UI에 노출되지 않음. 소리 종류/음량은 설정 가능.
- POSITIVE 표본이 1장이므로 다른 환경의 실제 포획 UI에서 threshold 보정 가능성이 있음.
- Auto Pilking 공식 일반 정책은 bot/macro를 금지한다. 이 빌드의 실제 게임 사용 근거는
  사용자가 밝힌 별도 허락이며 그 허락의 적용 범위는 프로그램이 독립적으로 검증하지 못한다.
- 실제 `SendInput` down/up은 임시 WPF 창에서 검증했다. RF4에서의 입력 수신과 누름 중 정지는 실물 검증 대기.
- PIP의 창 수명과 fake 프레임 표시는 검증했다. 실제 RF4 영상·resize·게임 재연결 표시는 실물 검증 대기.
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
5. 실제 RF4에서 PIP 영상 표시와 게임 창 resize/종료/재실행을 검증.
