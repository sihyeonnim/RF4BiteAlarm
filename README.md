# RF4 Overlay

Windows용 C# / .NET 10 / WPF RF4 보조 플랫폼입니다.

## 현재 사용할 수 있는 기능

- Metronome: 시작/정지, 소리 주기 0.2–60초, BPM 1–300, 음량 조절.
- WPF 버튼, System Tray, Global Hotkey가 동일 Feature Command를 호출.
- 단일 키, modifier 조합, 반복/혼합 key sequence 기록 및 저장.
- RF4 본체 창 자동 탐색과 Windows Graphics Capture 진단.
- Bite Alarm: 정규화 ROI에서 물고기 포획 아이콘을 감지하고 반복 알람 재생.
- Auto Pilking은 사용 불가. 자동 입력 구현은 없음.

기본 단축키는 Bite Alarm **Ctrl+F9**, Metronome **Ctrl+F8**입니다. Auto Pilking의
Ctrl+F10은 예약되어 있으나 기능은 아직 사용 불가입니다. 단축키는 원래 앱에도 전달됩니다.

'키 기록'을 클릭하고 원하는 키/조합을 차례로 누른 뒤 '저장'을 클릭합니다.
NumPad1 세 번도 가능합니다. 같은 기능의 키 사이 제한은 100–5000ms로 설정합니다.
중복 또는 prefix가 겹치는 설정은 거절합니다.

창 X 버튼은 Tray로 숨깁니다. 완전 종료는 '프로그램 종료' 또는 Tray의 '종료'입니다.
설정은 사용자 LocalApplicationData/RF4Overlay/settings.json에 자동 저장됩니다.
주기와 BPM은 `BPM = 60 / 주기(초)`로 서로 연동되며, 소수 주기도 그대로 저장됩니다.

## 개발 및 실행

Windows 10 build 19041 이상, .NET SDK 10.0.401, Git을 사용합니다.

```powershell
dotnet build
dotnet test
dotnet run --project src/RF4Overlay.App
```

`dotnet test`에는 실제 WGC/오디오/hook 테스트가 포함됩니다.
잠금 해제된 Windows 데스크톱과 WGC 지원 그래픽 장치/오디오 출력이 필요합니다.
순수 로직 테스트만 실행하려면 `dotnet test tests/RF4Overlay.Tests`를 사용합니다.

## 구조

| 프로젝트 | 책임 |
| --- | --- |
| App | WPF 셸, ViewModel, 설정/서비스 조립 |
| Core | Feature runtime, 공통 계약, 순수 Hotkey matcher |
| Infrastructure | Windows 입력, Tray, Audio, WGC, 설정 파일 |
| Features | Metronome, Bite Alarm 준비, Auto Pilking placeholder |
| Tests | Windows API 없는 로직 테스트 |
| WindowsTests | 실제 Windows 캡처/장치/파일 통합 검증 |

현재 상태와 남은 검증은 [docs/STATUS.md](docs/STATUS.md),
설계 결정은 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)에 기록합니다.
