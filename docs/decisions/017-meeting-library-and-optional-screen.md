# ADR 017: Meeting library and optional screen capture

## Status

Accepted for Sprint 2.7.

## Decision

The desktop enumerates every `session_*` workspace from the local capture root. Valid manifests become library entries with start time, duration, status, available streams, alignment state and transcript state. Missing or unreadable manifests remain visible as invalid entries with diagnostics.

Transcription can be explicitly started or retried for any completed session containing non-empty microphone and system-audio artifacts. Existing transcripts open in the established viewer. Deletion requires an explicit confirmation and is restricted to a direct `session_*` child of the configured capture root; it removes the complete workspace so media, transcript and metadata cannot become inconsistent. The library does not rename or silently repair session data.

Screen capture is optional per recording. An empty screen source in `CapturePlan` means audio-only capture: no screen provider is created, no MP4 is emitted and the manifest contains only the audio streams. The choice is persisted in the local application settings file and defaults to enabled.

Desktop shutdown first marks the view model as shutting down, cancels UI polling and waits a bounded five seconds for that task to unwind before disposing the worker. The polling task does not send a second cancellation request during shutdown; closing the supervised worker remains the authoritative final cancellation and cannot accidentally restart a fresh worker with no knowledge of the old job ID.

## Consequences

- Old and interrupted recordings remain discoverable rather than being hidden by a newest-only workflow.
- Audio-only meetings use less storage and capture processing.
- The stream list is authoritative; consumers must not assume that every session contains video.
- Search, titles, retention and moving the library remain later slices.
