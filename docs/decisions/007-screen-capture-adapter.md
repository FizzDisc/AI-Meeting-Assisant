# ADR 007: Screen capture adapter

- Status: Accepted for Sprint 1.5.1
- Date: 2026-08-18

## Decision

Use ScreenRecorderLib 6.6.0 behind the Core-owned `IScreenCaptureProvider` boundary for the first production screen-capture slice. Configure one `DisplayRecordingSource`, H.264 MP4, 30 fps, x64, hardware encoding when available, and no audio track.

## Rationale

Reliable screen-to-H.264 capture requires native display acquisition, Direct3D frame handling, Media Foundation encoder negotiation and asynchronous file finalization. Reimplementing that native pipeline inside this slice would add a large COM surface before the product's session and synchronization semantics are proven. The adapter gives the application explicit lifecycle and error semantics while keeping replacement possible.

The library must not own meeting audio. Our existing WASAPI providers deliberately produce separate microphone and system-audio artifacts for later synchronization, diagnostics and device handover.

## Consequences

- The Windows adapter and desktop executable are x64.
- Windows Media Foundation and the Visual C++ x64 runtime are deployment prerequisites.
- Screen capture output is an independently playable MP4.
- Sprint 1.5.2 composes this provider with the existing dual-audio coordinator.
- Preview, window-only capture and live display switching are not part of 1.5.1.
