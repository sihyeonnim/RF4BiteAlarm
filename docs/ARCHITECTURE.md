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
