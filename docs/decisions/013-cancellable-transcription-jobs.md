# ADR 013: Cancellable transcription job isolation

## Status

Accepted for Sprint 2.3.

## Decision

The long-lived JSON-lines worker owns job orchestration but not native inference execution. Each transcription runs in a disposable Python child process. The protocol exposes `transcription.start`, `transcription.status` and `transcription.cancel`; cancellation terminates that child process and leaves the worker available.

One or two finalized WAV inputs are normalized with local FFmpeg to mono PCM16 at 16 kHz. Two tracks are mixed for the first transcription slice. WhisperX loads only an explicitly supplied local CTranslate2 model directory. It may not resolve a model name or initiate an implicit download. Results are atomically written as schema-versioned JSON containing language, compute mode and timestamped segments.

## Consequences

- Cancellation works even while native Torch/CTranslate2 code is executing.
- A failed or cancelled job cannot crash the capture/UI worker.
- Normalized audio and status files remain inspectable inside the session processing directory.
- The first slice does not perform alignment or diarization; those require separately managed models and later contracts.
- Production model installation and selection remain the responsibility of AMA-115. A tiny model download script exists only for explicit development smoke tests and its payload is Git-ignored.
