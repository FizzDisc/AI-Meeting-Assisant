# ADR 015: Explicit desktop transcription orchestration

## Status

Accepted for Sprint 2.5.

## Decision

The desktop remembers the most recently completed capture session and enables an explicit **Transcribe latest** action when a local model is available. Recording completion does not automatically start CPU/GPU work. The UI polls the supervised job and exposes normalization, model loading, transcription, completion and failure states plus progress and cancellation.

The current development-model resolver prefers an explicit `AI_MEETING_ASSISTANT_MODEL` directory and otherwise discovers the Git-ignored `worker/models/faster-whisper-tiny` directory. This is temporary development wiring and will be replaced by persisted Model Manager selection. Only successfully finalized sessions become eligible; interrupted or failed sessions are not silently processed.

## Consequences

- Users control when potentially long local inference starts.
- Capture and transcription cannot be started concurrently from the main window.
- Cancellation reaches the isolated worker job rather than only changing UI state.
- The first UI slice exposes the transcript artifact path but not yet a transcript reader/editor or export workflow.
