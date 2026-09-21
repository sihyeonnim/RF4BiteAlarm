# RF4 Bite Alarm

RF4Overlay에서 Bite Alarm 기능만 분리한 Windows용 WPF 애플리케이션입니다.

## 기능

- 실행 중인 RF4 창 자동 탐색 및 Windows Graphics Capture
- 포획 아이콘 3프레임 연속 감지 후 반복 알람
- 물리 키/마우스 입력 시 알람 확인 및 다음 포획 재무장
- 5개 알람 소리, 음량, 전역 시작/정지 단축키 설정
- 시스템 트레이에서 시작/정지, 창 열기, 종료

## 빌드

```powershell
dotnet build RF4BiteAlarm.slnx
dotnet test RF4BiteAlarm.slnx
dotnet publish src/RF4BiteAlarm/RF4BiteAlarm.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

설정 파일은 `%LOCALAPPDATA%\RF4BiteAlarm\settings.json`에 저장됩니다.
