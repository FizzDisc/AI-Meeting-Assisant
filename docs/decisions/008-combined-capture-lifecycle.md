# ADR 008: Combined capture lifecycle

- Status: Accepted for Sprint 1.5.2
- Date: 2026-08-18

## Decision

Compose the established screen and dual-audio coordinators behind one `CombinedCaptureCoordinator`. Start screen, system loopback and microphone under one serialized lifecycle and assign one timestamp to all three output filenames. Keep MP4 and both WAV files independent.

## Rationale

Independent media artifacts preserve debuggability and allow later transcription, device handover and synchronization without remuxing during capture. Reusing the validated providers avoids coupling ScreenRecorderLib audio behavior to our WASAPI pipeline.

## Failure policy

Start is atomic from the user's perspective: failure of any stream rolls back every stream already started. Runtime failure of any provider fails the recording session, whose normal stop path cleans up the other providers. Cleanup continues after individual stop failures and returns the first failure.

## Deferred

Shared filenames do not imply sample alignment. Sprint 1.6 adds a monotonic session clock, per-stream timing metadata and measurable synchronization acceptance criteria.
