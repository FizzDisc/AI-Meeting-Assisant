# AI Meeting Assistant

Local-first Windows desktop app for synchronized screen, system/Teams audio and microphone recording, followed by local source-aware transcription. This greenfield repository contains no Marvin prototype code.

## Implemented

- .NET 8 WPF desktop app and native Windows capture
- optional screen capture, recoverable session workspaces and alignment diagnostics
- local WhisperX transcription of separate microphone and system-audio tracks
- transcript viewer/export and meeting library
- operational status center and versioned settings
- offline pyannote speaker-diarization foundation

No AI model is bundled or downloaded automatically.

## Setup

Requires Windows 10 2004+, .NET 8 SDK, Python 3.10–3.13 and FFmpeg.

```powershell
.\scripts\setup-ai-runtime.ps1
.\scripts\install-development-model.ps1
dotnet run --project .\src\AiMeetingAssistant.Desktop
```

## Optional diarization model

Accept the conditions for `pyannote/speaker-diarization-community-1` on Hugging Face, create a read token, then run:

```powershell
.\scripts\install-diarization-model.ps1
```

The token is used only during download and is not stored. The pipeline then runs locally/offline. Sprint 3 processes only `system_audio`; the separate microphone track remains the known local participant (`You`).

## Verify

```powershell
dotnet build .\AI-Meeting-Assistant.sln
dotnet run --project .\tests\AiMeetingAssistant.Core.Tests
dotnet run --project .\tests\AiMeetingAssistant.Windows.SmokeTests
.\worker\.venv\Scripts\python.exe -m unittest discover .\worker\tests -p "test_*_unittest.py"
```

See [architecture](docs/architecture.md), [backlog](docs/backlog.md), and [decisions](docs/decisions/).
