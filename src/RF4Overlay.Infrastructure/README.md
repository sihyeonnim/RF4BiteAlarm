# Windows Infrastructure

공통 Windows 구현의 위치입니다. 현재 네이티브 기능은 구현되지 않았습니다.

- Capture: Windows Graphics Capture, 창 선택, 프레임 수명 및 장치 복구
- Input: 전역 키 관찰, sequence 매칭, 사용자 입력 감지
- Audio: 알람 및 메트로놈 소리 재생
- Tray: 메뉴를 Core의 FeatureCommandDispatcher로 전달

Core 계약을 구현하고 Feature를 참조하지 않습니다. 사용자 입력 관찰과 입력 자동화는
별도 서비스로 유지합니다. 네이티브 API를 Feature에 직접 추가하지 않습니다.
