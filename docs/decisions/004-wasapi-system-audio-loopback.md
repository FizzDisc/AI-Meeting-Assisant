# ADR 004: WASAPI endpoint loopback for system audio

## Status

Accepted and validated in Sprint 1.4.1.

## Decision

Capture system and Teams audio from the user-selected Windows render endpoint with WASAPI shared-mode loopback. Reuse the existing format conversion, level calculation, lifecycle and PCM16 WAV production code. Write an independent `system_audio_*.wav` file.

## Consequences

- The solution works for Teams and other applications without depending on application internals.
- The selected endpoint must be the endpoint on which the user actually plays audio.
- Sprint 1.4.1 records loopback only; microphone plus loopback orchestration and synchronization follow in Sprint 1.4.2.
- Protected media or driver-specific processing can still prevent useful samples and requires hardware smoke testing.

## Validation

Automated validation passed with 21/21 Core tests and 5/5 Windows smoke checks. Manual validation confirmed source selection, live system-audio level, elapsed timer, stop/finalization and playback. The inspected output contained 2,833,920 audio-data bytes at 48 kHz, 16-bit stereo for 14.76 seconds.
