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
- 사용자 입력 관찰과 IInputAutomation 계약/Windows `SendInput` 구현은 분리한다.
  자동 입력은 우클릭 또는 modifier 없는 단일 keyboard key만 표현하며, 취소 중에도 up 이벤트를 보장한다.

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
CaptureMonitor는 최신 프레임 하나만 유지하고 연결 상태를 UI에 전달한다. Bite Alarm이
구독하면 용량 1의 latest-only channel로 같은 프레임을 공유하여 별도 WGC 세션을 만들지 않는다.

- 크기 변경: pool.Recreate.
- 창 종료/최소화/5초 무프레임/장치 오류: 자원 정리 후 2초 간격 재탐색 및 재연결.
- Dispose: 취소 후 iterator 자원 해제 완료까지 대기.
- 캡처 실패는 detector의 UI 소멸 신호로 변환하지 않음.
- HDR 및 실제 RF4 전체화면 호환성은 실물 검증 대기.

## RF4 PIP

PictureInPictureFeature는 `IPictureInPicturePresenter`를 통해 표시 계층과 분리한다.
WPF presenter는 App 계층에 있으며 CaptureMonitor의 공용 latest-only stream을 구독하므로
Bite Alarm과 PIP를 함께 실행해도 WGC 세션을 추가로 만들지 않는다.

- 기본 크기 480×300, 최소 크기 240×160, 사용자가 자유롭게 크기 조절 가능.
- `Topmost=true`, ToolWindow, 작업 표시줄에는 별도 앱으로 표시하지 않음.
- 영상은 비율을 보존하는 `Uniform` 방식으로 검은 배경 안에 표시.
- RF4 프레임이 아직 없으면 `RF4 프레임 대기 중`을 표시하고 연결 후 자동 전환.
- PIP 창 X, UI/Tray/Hotkey Toggle, 앱 종료 모두 같은 Feature 수명과 취소 경로를 사용.
- 각 프레임은 BGRA8 BitmapSource로 만들고 freeze한 뒤 UI Dispatcher에서 교체.

기존 설정에 PIP binding이 없으면 `Ctrl+F11`을 추가하며 사용자가 지정한 다른 Feature binding은 유지한다.

공식 근거 (2026-09-15 확인):
[Screen capture](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture),
[CreateFreeThreaded](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded),
[CreateForWindow](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow).

## Bite Alarm

제품 Feature는 CaptureMonitor의 프레임을 FishCaughtIconDetector에 전달한다.
IBiteDetector와 BiteAlarmSession은 영상 판정과 알람 상태를 분리한다.

상태: Inactive → Monitoring → Alerting → WaitingForDisappearance → Monitoring.

- Present 최초 감지 즉시 1회, 이후 설정 간격 반복.
- 사용자 활동 확인 시 다음 반복부터 중단하며 현재 소리는 끝까지 재생. 기능 정지/앱 종료 시에는 즉시 정리.
- UI 소멸만으로 사용자가 확인한 것으로 취급하지 않음.
- 확인 후 같은 UI가 남아 있으면 새 입질로 처리하지 않음.
- 연속된 안정적 Absent 뒤에만 재무장. Present/CaptureFailed는 소멸 확인 근거를 초기화.
- 세션은 관찰/타이머/입력 확인과 오디오를 직렬화하고 종료 시 구독/소리 정리.
- 프레임 스트림 실패는 세션을 중단시키며 정상 소멸로 간주하지 않음.

FishCaughtIconDetector는 제공된 1920×1080 자료의 normalized ROI
`(left 0.2750, top 0.9269, width 0.0229, height 0.0407)`만 읽는다.
전체 화면, 액션 문구, 게이지 및 배경은 비교하지 않는다. 기준 해상도 좌표계에서 아이콘 중심과
흰색 무채색 픽셀을 계산하고, 원형 띠 전체 비율·네 사분면의 최소 비율·원 내부 물고기 형태의
비율이 모두 기준을 넘으면서 원 바깥 배경의 흰색 비율이 상한 이하여야 후보로 판정한다.
따라서 ROI 전체가 갑자기 밝아진 프레임은 아이콘으로 인정하지 않는다. 하나의 이미지 템플릿과
픽셀별 동일 비교는 사용하지 않는다.

원형 경계도 추가 확인한다. 중심에서 16방향을 샘플링하여 반경 12–16의 가장 밝은 점이
안쪽 반경 9–10 및 바깥 반경 19–20보다 모두 25 이상 밝은 방향이 최소 12개여야 한다.
원 내부가 채워진 밝은 면/문구와 얇은 원형 테두리를 구분하며, 상대 대비로 기존 어두운 아이콘을
보존한다. 모든 반경은 1920×1080 기준이며 기존 normalized ROI 안에서만 샘플링한다.

기본 3개 연속 프레임이 후보일 때만 Present를 출력하며 중간 Absent/CaptureFailed는 누적을 초기화한다.
사용자가 알람을 확인한 뒤 들어오는 입력은 소멸 확인을 초기화한다. Steam 오버레이처럼 입력으로
닫는 화면이 잠깐 아이콘을 가려도 같은 아이콘이 다시 알람을 일으키지 않는다. 제품의 임시 반복
간격은 5초, 소멸 확인은 2초이다. 이후 UI 설정으로 노출한다.
제공된 POSITIVE 1장과 NEGATIVE 4장의 지정 ROI crop만 WindowsTests 자산으로 보관한다.
테스트는 crop을 1920×1080의 원래 위치에 배치하고 1280×720/2560×1440 변환도 회귀 검증한다.

## Auto Pilking

AutoPilkingFeature는 각 주기 시작 시 thread-safe 설정 snapshot을 읽고 설정한 누름 시간에
±0.5초 난수를 적용한 뒤 `IInputAutomation.HoldAsync`로 입력을 누르고 release 시간만큼 기다린다.
난수가 적용된 누름 시간은 허용 범위 0.1–60초로 제한한다. 기본값은 우클릭, 1.0초 누름/3.5초 해제다.
이전 설정 형식에서 우클릭 3.0/3.0인 기존 기본값만 새 기본값으로 한 번 이관하고 사용자 지정 값은 유지한다.
keyboard 선택지는 modifier 조합이나 sequence가 아닌 단일 키만 허용한다. UI의 위/아래 버튼은
0.1초씩 조절하며 유효 범위는 0.1–60초다. UI, Tray, Hotkey는 다른 Feature와 동일하게
FeatureCommandDispatcher를 사용한다. 해제 시간은 고정이며 탐지 회피나 은폐 기능은 없다.

IGameForegroundGate는 native RF4 프로세스(rf4_x64/rf4/RussianFishing4)가 Windows 전면 창인지
100ms 간격으로 확인한다. Auto Pilking은 RF4가 전면일 때만 한 주기를 시작하고, 주기 도중 포커스를
잃으면 연결된 cancellation token으로 현재 입력을 즉시 해제한다. Double Click to Holding은 RF4 밖의
더블클릭을 무시하며 유지 중 포커스를 잃으면 좌클릭과 선택된 Shift를 즉시 해제한다. 두 Feature의
runtime 상태는 Running으로 유지되므로 RF4 복귀 후 자동으로 다시 입력을 받을 수 있다.

WindowsInputAutomation은 `SendInput`으로 right-button/key down을 보낸 뒤 대기하며, 정상 완료와 취소
모두 `finally`에서 대응하는 up을 보낸다. 입력 관찰 hook은 Windows injected flag를 확인하므로 이 입력을
사용자 활동이나 전역 단축키로 다시 처리하지 않는다.

운영정책 확인일: 2026-09-21. [RF4 Terms of Use](https://store.steampowered.com/eula/766570_eula_0)는
게임상 이득을 위한 bot/cheating program 사용을 금지하고, [공식 2019 공지](https://store.steampowered.com/news/posts/?appids=766570HELP&enddate=1560531147)는
bot, cheat software, macro를 제재 대상으로 열거한다. 사용자는 이 실험에 별도 허락을 받았다고 명시했다.
구현 범위는 그 진술을 전제로 한 로컬 고정 입력 반복이며, 프로그램은 허락의 범위를 검증하지 않는다.

## 2026-09-21 기능별 설정 UI 및 알람 소리

Metronome, Auto Pilking, Bite Alarm, Double Click to Holding 행의 톱니바퀴 ToggleButton이 각 설정 패널을 펼친다.
기본 상태는 접힘이며, 설정은 기존 MainViewModel에 바인딩한다.
화면은 밝은 회색의 compact 행과 구분선을 사용한다. 기능 순서는 Bite Alarm, Auto Pilking,
Double Click to Holding, RF4 PIP, Metronome이다. 설명문, 캡처/입력 상태, 진단 저장, 키 사이 제한은
기능을 제거하지 않고 메인 UI에서 숨긴다. 단축키 기록은 각 행의 키보드 아이콘으로 연다.
BiteAlarmSetting은 기본 Alarm 파형(Default), 4개 내장 WAV 선택과 독립 음량을 저장한다.
이전 설정 파일은 Default/70%로 이관한다. BiteAlarmSettings의 불변 snapshot을 다음 알람에서 읽는다.
WAV는 Infrastructure의 embedded resource로 배포하고 NAudio로 44.1kHz mono float로 변환한다.
긴 소리가 재생 중이면 반복 trigger를 건너뛰어 재생 도중 처음부터 다시 시작하지 않는다.

추가 소리 표시 이름은 알람 1–4이며 기존 SoundCue 저장 값은 유지한다.
설정의 Test 명령은 별도 audio voice로 선택한 소리/음량을 한 번 재생한다.
재클릭 시 이전 preview를 정지하며 ViewModel.Dispose에서 preview voice를 해제한다.

## 기능 상태 음성 안내

FeatureCommandDispatcher는 선택적인 IFeatureAnnouncement를 받아 실제 Running/Stopped 상태 전환에만 알린다.
따라서 UI, Tray, 단축키에서 같은 동작을 보장하고 중복 시작·정지 또는 실패 명령은 읽지 않는다.
PIP 창 X처럼 기능 수명이 자체 종료되는 경우도 중지 안내를 재생한다.
Infrastructure의 FeatureAnnouncementService는 Windows SAPI 영어 음성을 전용 STA thread의 queue에서
순차 재생한다. 기능 실행 thread를 막지 않으며 종료 시 queue와 COM voice를 정리한다.
SAPI의 Language 409(en-US) voice를 우선 선택하고, 설치되어 있지 않으면 시스템 기본 voice를 사용한다.
VoiceAnnouncementSettings는 ON/OFF와 0–100 정수 음량의 불변 snapshot을 제공한다.
기본 표시값과 이전 33% 설정의 이관값은 ON·50%이며 UI 변경은 settings.json에 저장된다.
표시 50%는 이전 SAPI 33과 같은 출력이다. 0→0, 50→33, 100→100의 구간별 선형 변환을 적용해
기존 기본 음량을 유지하면서 전체 0–100 출력 범위를 제공한다. 서비스는 enqueue 시와 재생 직전에
Enabled를 확인하고, 각 Speak 직전에 변환된 현재 Volume을 적용한다.
앱 전체 종료가 내부적으로 기능을 정리할 때는 여러 음성이 연속되지 않도록 안내하지 않는다.
