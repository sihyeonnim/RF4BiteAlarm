# 개발 지침

- 먼저 Git 상태와 docs/STATUS.md, docs/ARCHITECTURE.md를 읽고 기존 작업을 존중한다.
- 파일 수정, 도구 실행, 오류 수정을 직접 수행한다. 사용자에게 복사·붙여넣기나 명령 대행을 요구하지 않는다.
- 의미 있는 코드 변경 후 `dotnet build`와 `dotnet test`를 실행한다. 실패를 해결한 뒤 다음 단계로 간다.
- 가능하면 실제 WPF 앱도 실행하여 UI를 확인한다.
- Phase 완료 시 변경을 검토하고 커밋한다. bin/obj, 비밀정보, 개인 설정, 임시 진단 자료는 제외한다.
- Feature 간 직접 참조를 추가하지 않는다. 공통 Windows API 구현은 Infrastructure에 둔다.
- UI, Tray, Hotkey는 동일한 FeatureCommandDispatcher를 호출한다.
- 사용자 입력 관찰과 입력 자동화는 분리한다.
- 실제 입질 UI 자료 없이 좌표·템플릿·임계값을 실제 값으로 가정하지 않는다.
- Auto Pilking 실제 구현 전에 RF4 최신 공식 운영정책을 확인하고 출처와 날짜를 기록한다.
  허용되는 범위에서만 구현하며 탐지 회피나 자동화 은폐는 구현하지 않는다.
- 세션 종료 전 STATUS에 완료 범위, 검증, 다음 작업, 알려진 문제를 갱신한다.
- 불필요한 enterprise 계층/프레임워크를 도입하지 않는다.
