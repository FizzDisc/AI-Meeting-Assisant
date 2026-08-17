# AI Meeting Assistant

Greenfield desktop application for recording meetings on Windows and turning them into useful, locally processed knowledge. This repository deliberately contains no code from the former Marvin prototype.

## Sprint 0 scope

- Native Windows application shell with a minimal recording dashboard
- Explicit capture and transcription boundaries, with placeholder implementations only
- Versioned JSON contract between the .NET application and Python worker
- Architecture decisions and an ordered delivery backlog
- No production screen or audio capture yet

## Repository layout

```text
src/
  AiMeetingAssistant.Desktop/   WPF application and presentation layer
  AiMeetingAssistant.Core/      Use-case and capture abstractions
  AiMeetingAssistant.Contracts/ Versioned worker messages
worker/                          Python AI worker skeleton
contracts/                       Language-neutral JSON Schema
docs/                            Architecture, decisions and backlog
```

## Prerequisites

- Windows 10 version 2004 or newer
- .NET 8 SDK (the desktop workload is included in the Windows SDK)
- Python 3.11 for the worker; ML dependencies are intentionally deferred

## Run the desktop shell

```powershell
dotnet restore .\AI-Meeting-Assistant.sln
dotnet run --project .\src\AiMeetingAssistant.Desktop
```

The record control currently exercises the Sprint 1.1 state machine using a clearly labelled simulation. No screen or audio is captured or saved yet.

## Verify Sprint 1.1

```powershell
dotnet build .\AI-Meeting-Assistant.sln
dotnet run --project .\tests\AiMeetingAssistant.Core.Tests
dotnet run --project .\src\AiMeetingAssistant.Desktop
```

## Exercise the worker protocol

```powershell
'{"protocolVersion":"1.0","requestId":"demo","type":"health.check","payload":{}}' | python .\worker\main.py
```

Expected output is one JSON response on stdout. Logs must go to stderr so the protocol stream remains machine-readable.

See [architecture](docs/architecture.md), [Sprint backlog](docs/backlog.md), and [ADR-001](docs/decisions/001-platform-and-process-boundary.md).
