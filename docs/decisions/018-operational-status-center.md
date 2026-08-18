# ADR 018: Operational status center

## Status

Accepted for Sprint 2.8.

## Decision

The main dashboard shows operational state instead of a release roadmap or a privacy marketing card. The status center exposes the current user-facing state, a dedicated AI-runtime state and an expandable recent technical activity list.

Startup publishes explicit stages before potentially slow native ML initialization, including `Loading AI runtime...` and the final compute/Python result. Recording lifecycle, recovery, transcription stages, cancellation and actionable errors feed the same in-memory log.

Long transcription phases never present a static percentage as proof of liveness. Queuing, normalization and model loading use an indeterminate progress bar plus elapsed time and phase-specific explanation. After 15 seconds of CPU model loading, the activity log explicitly records that the worker remains active; this is particularly important because an isolated cold model load can dominate a very short or silent recording.

The technical activity log is newest-first, suppresses adjacent duplicates and retains at most 50 entries. It is intentionally session-local and can be cleared by the user. Durable diagnostic files and export remain later settings/diagnostics work.

## Consequences

- A slow Torch/worker cold start no longer looks like a frozen application.
- Planned product work is no longer mixed with live application health.
- The small bounded log cannot grow without limit or become a hidden persistence store.
