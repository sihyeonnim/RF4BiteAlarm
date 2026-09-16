# RF4 Overlay 구조

## 프로젝트 경계

App → Features → Core, App → Infrastructure → Core, App → Core.

Core/Features는 net10.0, App/Infrastructure는 net10.0-windows10.0.19041.0이다.
Feature들은 한 어셈블리 안의 독립 namespace/폴더이며 서로 참조하지 않는다.
규모에 맞게 별도 plugin loader, DI framework, Feature별 assembly는 도입하지 않았다.
WindowsTests는 실제 Windows API를 사용하며 순수 Tests와 분리한다.

## Feature runtime

동기 Execute 계약으로는 장기 실행/취소를 표현할 수 없어 IFeature.RunAsync로 변경했다.
Runtime이 Feature별 semaphore, 실행 Task, CancellationTokenSource, 불변 상태 snapshot을 소유한다.

- 같은 Feature 명령은 직렬화, 다른 Feature 실행은 독립.
- 명령 토큰은 명령 대기/실행 전 취소에 사용. 시작된 실행은 Stop 또는 shutdown이 취소.
- 중복 Start/Stop 안전 처리. Toggle은 활성 실행 유무를 기준으로 결정.
- Faulted를 UI에 전달하고 개별 실패/상태 구독자의 예외를 격리.
- 실행 최종 결과를 보관하여 완료와 Stop 경합에서 오류/종료 상태가 사라지지 않도록 처리.
- shutdown은 새 명령을 거절하고 모든 실행의 finally 정리를 기다림.
- WPF, Tray, Hotkey 모두 같은 FeatureCommandDispatcher.ExecuteAsync 사용.
- WPF ViewModel은 dispatcher에 상태 변경을 전달받으며 Windows API를 직접 호출하지 않음.

## Global Hotkey / Player Activity

전용 메시지 루프 thread에서 WH_KEYBOARD_LL / WH_MOUSE_LL을 관찰한다.
Hook callback은 bounded queue에 넣고 즉시 반환하며 입력은 원래 앱에 전달한다.
Worker가 injected flags를 제외하고 실제 사용자 활동과 key event를 전달한다.
GetLastInputInfo는 출처를 구분할 수 없어 사용하지 않는다.

Pure HotkeyMatcher는 down/up, 눌린 키 집합, 정확한 modifier, 시간 순서와 최대 간격을 처리한다.
OS auto-repeat은 눌린 키 집합으로 제외한다. 입력 queue 포화 시 matcher를 reset한다.

설정 정책:

- 동일/prefix sequence는 등록 거절. A A와 A A A를 동시에 등록할 수 없음.
- 비-prefix suffix가 겹쳐 같은 시각에 완성되면 가장 긴 매칭을 선택.
- 매칭 시 history를 소비하여 같은 입력에서 중복 실행하지 않음.
- Hook 관찰 → matcher → bounded 명령 queue → 공통 runtime.
- 실제 키 기록 UI에서는 전역 명령을 일시 중단하고 최대 8개 키를 기록.
- 입력 내용은 디스크/네트워크에 기록하지 않음. 저장되는 것은 사용자가 지정한 binding뿐.
- 사용자 입력 관찰과 IInputAutomation 계약은 분리. 자동 입력 구현 없음.

공식 근거 (2026-09-15 확인):
[LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc),
[KBDLLHOOKSTRUCT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct),
[MSLLHOOKSTRUCT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-msllhookstruct).

## Audio / Metronome

NAudio 2.2.1의 WaveOutEvent와 작은 sample provider를 사용한다.
IAudioService가 Feature 수명별 voice를 만들고 Play/Stop/Volume을 제공한다.
침묵 사이에 감쇠 envelope의 tick/alarm 파형을 한 번 재생하며 자동 반복은 하지 않는다.
반복 정책은 각 Feature에 있다. 장치 오류는 Feature runtime으로 전달한다.

Metronome은 Stopwatch의 목표 시각을 누적한다. 지연 시 놓친 박자를 건너뛰며
소리를 한꺼번에 재생하지 않는다. 소리 주기(0.2–60초)를 기준값으로 사용하고 BPM은
`60 / 주기`로 양방향 변환한다. 주기/BPM/음량은 thread-safe 설정에서 다음 박자에 읽는다.
기존 BPM만 저장된 설정 파일은 `60 / BPM`으로 주기를 계산해 자동 이관한다.

## App / Tray / 저장

App이 구현체를 조립한다. Tray는 Infrastructure의 WinForms NotifyIcon이다.
창 X는 숨김, Tray 열기는 복원, 명시적 종료는 서비스 초기화를 기다린 뒤 정리한다.
작은 화면은 작업 영역 높이에 맞추고 Feature 목록을 스크롤할 수 있다.

설정은 LocalApplicationData/RF4Overlay/settings.json에 저장한다.
단일 비동기 writer가 미처리 변경을 최신값으로 합치고 임시 파일을 원자적으로 교체한다.
종료 시 최신 저장까지 기다린다. 손상된 설정은 UI 오류를 표시하고 기본값을 사용한다.
수동 진단 PNG는 동일 사용자 경로의 diagnostics 하위에 저장하며 저장 작업은 UI 밖에서 실행한다.

## Windows Graphics Capture

RF4 본체 프로세스 이름과 HWND/PID로 창을 찾는다. 스트리밍 제목이 실제 게임과 다른 사례를
확인했으므로 Steam streaming_client는 자동 연결 대상에서 제외한다.

WgcWindowCapture의 iterator가 D3D11 device, WGC free-threaded pool/session과 각 frame을 소유한다.
SoftwareBitmap으로 BGRA8 CPU 복사본을 만든다. 프레임은 dispose 후 pool을 재생성한다.
CaptureMonitor는 최신 프레임 하나만 유지하고 연결 상태를 UI에 전달한다.

- 크기 변경: pool.Recreate.
- 창 종료/최소화/5초 무프레임/장치 오류: 자원 정리 후 2초 간격 재탐색 및 재연결.
- Dispose: 취소 후 iterator 자원 해제 완료까지 대기.
- 캡처 실패는 detector의 UI 소멸 신호로 변환하지 않음.
- HDR 및 실제 RF4 전체화면 호환성은 실물 검증 대기.

공식 근거 (2026-09-15 확인):
[Screen capture](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture),
[CreateFreeThreaded](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded),
[CreateForWindow](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow).

## Bite Alarm

제품 Feature는 실제 자료 대기로 Unavailable이며 임의 영상 detector는 없다.
IBiteDetector와 BiteAlarmSession을 통한 fake 기반 테스트는 가능하다.

상태: Inactive → Monitoring → Alerting → WaitingForDisappearance → Monitoring.

- Present 최초 감지 즉시 1회, 이후 설정 간격 반복.
- 사용자 활동 확인 시 반복/현재 소리 중단.
- UI 소멸만으로 사용자가 확인한 것으로 취급하지 않음.
- 확인 후 같은 UI가 남아 있으면 새 입질로 처리하지 않음.
- 연속된 안정적 Absent 뒤에만 재무장. Present/CaptureFailed는 소멸 확인 근거를 초기화.
- 세션은 관찰/타이머/입력 확인과 오디오를 직렬화하고 종료 시 구독/소리 정리.
- 프레임 스트림 실패는 세션을 중단시키며 정상 소멸로 간주하지 않음.

영상 자료 도착 후에만 실제 detector를 작성하고 catalog에 연결한다.
현재 반복/소멸 확인 시간은 호출자가 제공하는 옵션이며 제품 기본값으로 확정하지 않았다.

실제 detector 구현에 필요한 외부 자료:

- 입질 트리거로 삼을 UI가 나타난 전체 RF4 캡처와 나타나지 않은 비교 캡처.
- 각 캡처의 해상도, 창 모드, UI 배율.
- UI 검색 영역의 `x, y, width, height` 또는 캡처 크기로 나눈 상대좌표.
- 해상도/UI 배율에 따라 모양이나 위치가 달라지면 각 조합의 자료.

이 자료로 검색 영역과 영상 판정 방식을 결정한다. 자료 전에는 좌표, 템플릿,
색상 및 유사도 임계값을 제품값으로 가정하지 않는다.

## Auto Pilking

현재 구조와 안내만 유지한다. 실제 자동화 시작 전 RF4 최신 공식 정책의 허용 범위를 확인하고
확인일/출처를 기록한다. 정책상 불허하거나 허용 여부가 확인되지 않으면 입력 자동화를 구현하지 않는다.
탐지 회피나 자동화 은폐는 구현하지 않는다.
