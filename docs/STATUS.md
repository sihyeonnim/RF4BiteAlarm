# 현재 상태

최종 갱신: 2026-09-15 (Asia/Seoul)

## 완료된 Phase

**Phase 1 — 개발환경 및 기본 모듈 구조 완료**

- 기존 리포지터리는 .git만 있었고 커밋은 없었음.
- 기존 .NET SDK 10.0.401, Git 2.53.0, WindowsDesktop runtime/targeting pack 확인.
- RF4Overlay.slnx 및 App/Core/Infrastructure/Features/Tests 5개 프로젝트 생성.
- 공통 Start/Stop/Toggle dispatcher 및 독립 Feature 등록.
- 캡처/알람/사용자 입력/입력 자동화/Global Hotkey 계약 정의.
- 단일 키·modifier·반복 key sequence 표현 및 유효성 검사.
- WPF 기본 화면: 3개 Feature 상태와 준비 중 버튼 표시.
- 빌드 결과물 제외, SDK 고정, nullable 및 경고 오류 처리 설정.

## 검증

- `dotnet build`: 성공, 경고 0 / 오류 0.
- `dotnet test`: 전체 8개 통과, 실패 0 / 건너뜀 0.
- 실제 실행 파일을 computer-use로 실행: 창 제목, 3개 Feature 문구,
  비활성 버튼을 스크린샷 및 접근성 트리에서 확인.
- 창 닫기 버튼으로 종료 후 창 목록에서 제거된 것 확인.
- 실제 게임 감지/소리/Hotkey/자동 입력 검증은 아직 대상이 아님.

## 다음 작업

1. Feature 비동기 수명, 상태 변경 알림, 명령 직렬화 및 오류 표시.
2. Infrastructure 입력 관찰과 Hotkey sequence 매칭, Tray 명령 연결.
3. Metronome 및 소리 서비스 구현 후 실행/정지 검증.
4. WGC 캡처를 별도로 검증한 뒤 실제 UI 자료를 받아 Bite Alarm 구현.

## 알려진 제약 및 문제

- 현재 3개 Feature는 모두 명시적인 미구현 placeholder이며 실행 불가.
- Infrastructure는 프로젝트 경계만 존재하며 Windows API 구현은 없음.
- Hotkey는 표현 계약만 있음. 시간/반복/충돌 매칭 엔진 및 Tray는 미구현.
- Bite Alarm 상태 머신, 반복 알람, 사용자 입력 서비스도 미구현.
- 실제 입질 이미지/해상도/좌표 미제공. 임의 좌표나 감지 임계값을 넣지 않았음.
- Auto Pilking 운영정책은 실제 구현 전에 확인할 예정.
- 샌드박스 NuGet 접속 실패는 승인된 `dotnet restore`로 해결함.
- 샌드박스 기본 빌드는 오류 상세 없이 실패했고 단일 노드 진단에서
  컴파일러 named pipe UnauthorizedAccessException 확인. 승인된 환경의 빌드는 정상.
  단일 노드 진단 빌드도 fallback 후 성공함. 프로젝트 설정으로 보안 제한을 우회하지 않음.
- 샌드박스 `dotnet test`는 출력 없이 지연되어 중단을 요청했으며,
  승인된 환경에서 `dotnet test`를 실행해 8개 통과 확인.

## 2026-09-15 Feature runtime

비동기 RunAsync 기반으로 전환. Feature별 명령 직렬화, 중복 start/stop, 작업 취소와 종료 대기,
Faulted 상태 및 UI 통지를 구현했다. 명령 취소 토큰은 대기 중인 명령에만 적용되고,
시작된 Feature 수명은 Stop 또는 앱 종료로 취소된다. 상태 구독자는 UI dispatcher로 전달한다.
현재 모든 Feature는 여전히 Unavailable이며 다음 단계에서 Metronome을 연결한다.
검증: dotnet build 성공(경고/오류 0), dotnet test 5개 통과.

## 2026-09-15 Global Hotkey engine

Windows 전용 키보드/마우스 저수준 hook과 별도 메시지 루프를 구현했다.
콜백은 bounded queue에 관찰 데이터를 넣고 즉시 반환하며 입력을 차단하지 않는다.
worker가 injected 입력을 제외하고 사용자 활동과 key down/up을 전달한다.
순수 matcher는 반복 억제, 정확한 modifier, 최대 key 간격, 반복/혼합 sequence를 지원한다.
중복/prefix 설정은 거절한다. match 후 history를 소비하고 비-prefix 겹침은 가장 긴 suffix 우선이다.
검증: build 경고/오류 0, test 10개 통과. 실제 hook/UI 연결은 다음 작업에서 검증한다.

## 2026-09-15 Audio / Metronome / Tray

공통 AudioService(NAudio 2.2.1), Feature별 voice 및 one-shot tick/alarm, volume/stop을 구현했다.
Metronome은 BPM 20–300, 누적 drift를 줄이는 목표 시각 스케줄 및 지연 박자 건너뛰기를 사용한다.
WPF 버튼/Tray/Hotkey는 같은 runtime 명령을 호출한다. 실제 키 기록으로 최대 8-key sequence를
등록할 수 있고 설정은 LocalApplicationData/RF4Overlay/settings.json에 저장한다.
X는 Tray 숨김, 명시적 종료는 hook/명령 큐/runtime/audio 정리 후 종료다.
검증: build 경고/오류 0, test 14개 통과. computer-use로 입력 모니터 초기화,
Metronome 시작/정지/재시작 상태 변경 및 실행 중 명시적 종료 후 프로세스 제거 확인.
실제 청각 및 물리 키 전역 단축키/Tray 메뉴 클릭은 사용자 검증 필요.
UIA BPM set_value는 도구 오류로 실패하여 그 경로는 아직 검증하지 않았다.

## 2026-09-15 WGC foundation

WGC CreateFreeThreaded + D3D11 device interop + SoftwareBitmap CPU 복사 구현.
RF4 HWND/PID 탐색, Steam 스트리밍 창 구분, 최소화/창 종료/5초 프레임 중단 처리,
크기 변경 시 pool 재생성, 실패 시 장치/세션 정리 후 2초 재연결을 구현했다.
App/Infrastructure target은 Windows 10.0.19041 API로 지정했다.
진단 UI: 연결 상태, frame dimensions/count, 사용자가 요청할 때만 PNG 저장.
검증: dotnet build 경고/오류 0. dotnet test 총 17개(순수 14 + Windows 통합 3) 통과.
Windows 통합 테스트가 실제 WPF 테스트 창의 파란 픽셀을 캡처하고,
빨간 창으로 가린 상태에서 resize된 프레임도 파란색인지 확인했다. 창 종료 예외,
hook 시작/중복 정리, 음량 0의 실제 audio voice 생성/재생/정리도 통과했다.
NEEDS_REAL_RF4_TEST: RF4 본체는 발견되지 않았으며 현재 Steam streaming_client만 존재한다.
본체 게임 캡처/재실행/device loss 실물 재현은 아직 검증하지 않았다.
