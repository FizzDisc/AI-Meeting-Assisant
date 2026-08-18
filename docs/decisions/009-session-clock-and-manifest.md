# ADR 009: Session clock and capture manifest

- Status: Accepted for Sprint 1.6.1
- Date: 2026-08-18

## Decision

Create one directory per combined capture and maintain a schema-versioned `manifest.json`. Start one monotonic `Stopwatch` before the first provider starts. Record offsets from the providers' actual start notifications: Media Foundation `Recording` for screen and WASAPI `CaptureStarted` for both audio streams.

## Clock model

UTC timestamps describe when the session occurred. Monotonic milliseconds describe ordering and relative media start positions and are immune to wall-clock corrections. The manifest stores relative filenames so a complete session directory can be moved safely.

## Persistence

Write JSON to a uniquely named sibling temporary file and atomically replace `manifest.json`. Status progresses through `preparing`, `recording`, and `completed` or `failed`. Stop records total monotonic duration after all streams have been finalized.

## Deferred

Sprint 1.6.1 records evidence but does not alter media. Sprint 1.6.2 uses offsets and measured artifact durations to define alignment and acceptance tolerances. Crash recovery, free-space checks and promotion into the durable meeting library remain separate slices.
