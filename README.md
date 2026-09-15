# RF4 Overlay

Windows용 C# / .NET 10 / WPF 기반 RF4 보조 플랫폼입니다.
현재 **Phase 1: 모듈형 기본 구조**까지 구현했습니다. 게임 기능은 아직 동작하지 않습니다.

## 프로젝트

| 위치 | 책임 |
| --- | --- |
| `src/RF4Overlay.App` | WPF 셸, ViewModel, 모듈 조립 |
| `src/RF4Overlay.Core` | Feature Command, 캡처·입력·소리 계약 |
| `src/RF4Overlay.Infrastructure` | 공통 Windows 구현 위치 (현재 예약) |
| `src/RF4Overlay.Features` | BiteAlarm / Metronome / AutoPilking 독립 모듈 |
| `tests/RF4Overlay.Tests` | 공통 명령 및 계약 검증 |

## 개발

필수: Windows, .NET SDK 10.0.401 (`global.json`), Git.
현재 단계에는 Visual Studio나 별도 workload 설치가 필요하지 않습니다.

```powershell
dotnet build
dotnet test
dotnet run --project src/RF4Overlay.App
```

진행 기록은 [docs/STATUS.md](docs/STATUS.md), 구조와 다음 단계 설계는
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)를 확인합니다.
