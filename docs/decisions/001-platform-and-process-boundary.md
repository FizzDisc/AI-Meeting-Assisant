# ADR-001: Windows shell and isolated AI worker

- Status: Accepted for Sprint 0
- Date: 2026-08-17

## Context

The product needs deep Windows capture integration and a native desktop experience, while the strongest local transcription and diarization ecosystem is Python-based. ML dependencies are large, hardware-sensitive and evolve independently from UI code.

## Decision

Use C#/.NET 8 with WPF for the Windows shell and capture orchestration. Run WhisperX, pyannote and later AI processing in a supervised Python worker. Keep domain/use-case abstractions free of WPF and Python. Exchange versioned JSON messages and artifact paths across the boundary.

WPF is selected because it is mature, Windows-native, ships with the .NET desktop stack and adds no third-party UI dependency. Reconsider the presentation layer before broad feature work only if a product requirement needs WinUI-specific capabilities.

## Consequences

- Capture remains in the ecosystem best suited to Windows APIs.
- ML packages can be pinned and updated without loading them into the UI process.
- Worker crashes and GPU failures are isolated from the desktop process.
- Packaging must coordinate .NET, Python and model distribution.
- The IPC contract needs compatibility tests and explicit versioning.

