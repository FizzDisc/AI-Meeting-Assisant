# AI Meeting Assistant

Local-first Windows desktop app for synchronized screen, system/Teams audio and microphone recording, followed by local source-aware transcription. This greenfield repository contains no Marvin prototype code.

## Download for Windows

**[Download the Windows x64 installer (v1.0.8)](https://github.com/FizzDisc/AI-Meeting-Assisant/releases/download/v1.0.8/AI-Meeting-Assistant-1.0.8-win-x64.msi)**

Run the MSI, then use the Welcome screen to download the AI components and a speech model. No separate Python or .NET SDK installation is needed. Close the app before updating.

[Release notes and downloads](https://github.com/FizzDisc/AI-Meeting-Assisant/releases/tag/v1.0.8)

This repository is private: sign in to GitHub with an account that has access to download the installer.

## Implemented

- .NET 8 WPF desktop app and native Windows capture
- optional screen capture, recoverable session workspaces and alignment diagnostics
- local WhisperX transcription of separate microphone and system-audio tracks
- transcript viewer/export and meeting library
- operational status center and versioned settings
- offline pyannote speaker-diarization foundation

No AI model is bundled or downloaded automatically.

## Development setup

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
