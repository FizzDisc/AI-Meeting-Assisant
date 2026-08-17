# ADR 006: Fail and finalize the whole audio session

## Status

Accepted and validated in Sprint 1.4.3.

## Decision

Treat microphone and system audio as one recording session for lifecycle and failure handling. If either provider fails at runtime, transition the session to failed, stop/finalize both streams and dispose each provider exactly once. Do not silently continue with only one stream.

## Rationale

A meeting recording that silently loses one side is more dangerous than an explicit failure. Independent WAV files are retained and finalized for diagnosis or partial recovery, while the UI clearly reports which stream failed and how to refresh or change the device.

## Validation

Automated validation passed with 22/22 Core tests and 12/12 Windows smoke checks. Manual validation passed five rapid recording cycles, close-during-recording finalization and a silent system-audio start. The final silent-start pair measured 10.885 seconds for microphone and 11.041 seconds for system audio. Physical device unplug was not tested and remains in the wider hardware matrix.
