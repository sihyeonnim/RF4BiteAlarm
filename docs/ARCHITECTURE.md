# 구조와 개발 계획

## 의존성

App → Features → Core, App → Infrastructure → Core, App → Core.
Core/Features는 net10.0, App/Infrastructure는 net10.0-windows를 사용합니다.
Feature는 한 프로젝트 안의 별도 namespace/폴더로 분리하고 서로 참조하지 않습니다.
현재 규모에서는 Feature마다 별도 어셈블리나 플러그인 로더를 만들지 않습니다.

## 명령과 수명

UI 버튼은 ICommand 어댑터를 통해 FeatureCommandDispatcher로 전달됩니다.
향후 Tray/Global Hotkey도 동일한 Start/Stop/Toggle 명령을 전달합니다.
현재 Feature는 모두 Unavailable이며 호출 시 실패 결과를 반환합니다.
실제 서비스 구현 단계에 비동기 수명, 중복 실행 방지, 오류 결과 표시, UI 상태 변경 알림,
종료 시 취소/자원 해제를 추가합니다. 현재 dispatcher는 동기식이며 스레드 직렬화를 제공하지 않습니다.

## Global Hotkey

HotkeyBinding은 virtual key + modifier의 순서와 최대 키 간격을 표현합니다.
단일 키, 조합 키, NumPad1 세 번 같은 반복을 같은 모델로 표현할 수 있습니다.
현재 등록/관찰/매칭 엔진은 미구현이며 기본 단축키도 등록하지 않습니다.
다음 단계에 실제 key-down/up 기반 반복 억제, 시간 초과, prefix 충돌 처리,
포커스 독립성, injected 입력 제외, 해제 및 명령 라우팅을 구현하고 테스트합니다.
명령 호출은 하나의 실행 문맥으로 직렬화합니다.

## Bite Alarm (계획)

- Infrastructure의 Windows Graphics Capture 서비스가 선택한 RF4 창 프레임을 확보합니다.
- 가려진 창에서도 동작하는 것이 목표이며 최소화/창 종료/장치 손실은 별도 상태로 처리합니다.
- 실제 UI 이미지, 해상도, 프레임 기준 상대 좌표를 받은 뒤 감지기를 구성합니다.
- 상태: 비활성 → 감시 → 알람 중 → 입력 확인 후 UI 소멸 대기 → 감시.
- 감지 시 즉시 소리를 재생하고 사용자 입력 전까지 설정 간격으로 반복합니다.
- 입력 확인 후 같은 UI가 남아 있어도 재알람하지 않습니다. UI가 사라진 뒤 새 입질을 허용합니다.
- 캡처 실패는 UI 소멸로 간주하지 않습니다. 안정된 UI 소멸 판정과 취소 경합을 테스트합니다.
- 사용자 입력 감지와 프로그램의 injected 입력은 구분합니다.

## Auto Pilking

현재는 기능 안내만 있습니다. 자동 입력 구현 및 정책 확인은 아직 수행하지 않았습니다.
실제 구현 전에 최신 RF4 공식 정책의 자동화 허용 범위를 검토하여 출처와 확인일을 기록합니다.
정책상 불허이면 해당 자동화는 구현하지 않습니다.

## Phase

1. 개발환경, 솔루션, 계약, WPF 셸, 테스트 — 완료.
2. Feature 수명 및 상태 통지, 공통 Hotkey/Tray, Metronome 구현.
3. WGC 창 캡처 및 오류 복구 검증.
4. 실제 자료 기반 입질 감지, 상태 머신, 소리 및 입력 확인 통합.
5. 공식 정책 검토 후 허용 범위에 한해 Auto Pilking 구현 여부 결정.

## Runtime 구현 결정 (2026-09-15)

기존 동기 Execute 계약은 장기 실행/취소를 표현할 수 없어 IFeature.RunAsync로 교체했다.
FeatureCommandDispatcher가 Feature별 semaphore, 실행 Task, CancellationTokenSource를 소유한다.
서로 다른 Feature는 병행 가능하며 같은 Feature의 명령은 직렬화된다.
Shutdown은 새 명령을 거절하고 모든 실행의 finally 정리가 완료될 때까지 비동기로 기다린다.
상태는 불변 snapshot으로 전달하고 개별 Feature 오류는 Faulted로 격리한다.

## Hotkey 구현 결정

GetLastInputInfo는 입력 출처를 구분하지 못하므로 사용하지 않는다.
WH_KEYBOARD_LL/WH_MOUSE_LL의 injected flags를 사용하여 모든 synthetic 입력을 제외한다.
전용 hook thread → bounded 입력 queue → matcher → bounded 명령 queue → 공통 runtime 경로다.
입력 queue 포화 시 matcher를 reset한다. 키 내용은 디스크에 기록하지 않는다.
앱 간 단축키 충돌은 감지할 수 없으며 입력은 원래 앱에도 전달된다.
공식 참조: https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc
및 https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct
(확인 2026-09-15). Hook timeout에 따른 OS의 조용한 해제는 플랫폼 제약이다.

## Audio / UI 구현 결정

오디오는 NAudio 2.2.1의 WaveOutEvent를 사용하며 각 Feature의 실행 수명에 voice를 소유한다.
침묵을 반환하는 sample provider에 일회성 파형을 공급한다. 재생 실패는 runtime Faulted로 전달된다.
BPM은 monotonic Stopwatch의 목표 시각을 사용하고 장기 지연 시 박자를 건너뛴다.
Tray는 Infrastructure의 WinForms NotifyIcon을 사용하며 WPF UI dispatcher에 상태를 반영한다.
키 설정은 WPF PreviewKeyDown으로 실제 key/modifier를 기록한다. 기록 중에는 전역 실행을 중단한다.

## WGC 구현 결정

App/Infrastructure는 net10.0-windows10.0.19041.0이며 Core/Features는 순수 net10.0이다.
캡처 iterator 한 개가 device, frame pool, session, 프레임을 소유한다. Dispose는 취소 후 iterator
정리를 기다린다. frame pool은 CreateFreeThreaded를 사용하므로 UI 메시지 루프에 의존하지 않는다.
SoftwareBitmap으로 BGRA8 CPU 복사본을 만들며 약 30fps까지 polling한다. 최신 프레임만 유지한다.
HDR 정밀 감지는 아직 대상이 아니다. 오류를 UI 소멸로 바꾸지 않는다.
모니터는 모든 실패에서 세션/장치를 폐기하고 창을 다시 탐색하여 재연결한다.
진단 PNG는 LocalApplicationData/RF4Overlay/diagnostics에 수동 저장하며 리포지터리에 넣지 않는다.
공식 참조: https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture
및 https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded
(확인 2026-09-15).

## Bite Alarm 준비 구현

BiteStateMachine은 순수 입력/시각 기반이며 Windows API를 모른다. BiteAlarmSession은
주어진 프레임 스트림과 IBiteDetector를 받아 오디오/입력 관찰을 연결한다.
알람은 사용자 입력이 있어야 중단되며 UI 소멸만으로 acknowledge하지 않는다.
WaitingForDisappearance에서 Present 또는 CaptureFailed가 소멸 확인 시간을 초기화한다.
실제 시간 옵션은 호출자가 제공한다. detector와 게임별 이미지/좌표/threshold는 아직 없다.
