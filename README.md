# RF4 Bite Alarm

## Overview

RF4 Bite Alarm monitors Russian Fishing 4 for the fish-caught indicator <img width="35" height="35" alt="Fish caught indicator" src="https://github.com/user-attachments/assets/734843b6-1aaf-474b-9a12-813ae112e4b8" /> and plays a repeating alarm when a catch is detected.

The demo below includes sound. Unmute the player to hear the alarm.

https://github.com/user-attachments/assets/7bcf0aff-cc9e-4f2c-9bd7-c88d47713cec

## Features

- Automatically locates the running RF4 window and captures it with Windows Graphics Capture.
- Requires the fish-caught indicator to appear in three consecutive frames before triggering, reducing false alarms.
- Repeats the selected alarm until physical keyboard or mouse input acknowledges the catch. The sound currently playing finishes naturally, and subsequent repetitions are stopped.
- Provides five alarm sounds, one-time sound previews, adjustable volume, and a configurable global start/stop hotkey.
- Supports starting and stopping the alarm, reopening the window, and exiting from the system tray.

## Build and Run

```powershell
dotnet build RF4BiteAlarm.slnx
dotnet test RF4BiteAlarm.slnx
dotnet publish src/RF4BiteAlarm/RF4BiteAlarm.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

User settings are stored in `%LOCALAPPDATA%\RF4BiteAlarm\settings.json`.
