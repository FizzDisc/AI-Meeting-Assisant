# ADR-002: Direct WASAPI microphone capture with event-driven buffer management

- Status: Accepted for Sprint 1.3
- Date: 2026-08-17

## Context

Sprint 1.3 requires recording from a single selected microphone endpoint with live level metering and reliable file output. The Windows audio stack offers two primary APIs for microphone input: WASAPI (exclusive mode, low-latency, COM-based) and MME (legacy, simpler, higher latency). Given the application's role as a recording tool that must supervise long sessions and handle device loss gracefully, WASAPI is the correct choice.

Direct WASAPI usage (via COM interop) avoids third-party audio library dependencies and gives the application full control over lifecycle, buffer management, and error recovery.

## Decision

Implement microphone capture using WASAPI IAudioClient in shared mode:
- Activate IAudioCapture from the selected IMMDevice
- Use event-driven reading from a circular buffer to avoid polling
- Implement live level metering (RMS calculation per buffer)
- Write raw PCM to a WAV container with accurate header (sample rate, bit depth, duration)
- Manage device loss explicitly and report it to the UI via state machine

Do not use third-party audio libraries (NAudio, CSCore, FMOD) in Sprint 1.3. The WASAPI surface is stable and the added cognitive load of another dependency is not justified for microphone capture alone.

## Consequences

- Full responsibility for COM object lifetime, wave format negotiation and buffer management
- No abstraction over WASAPI in the Windows adapter; future system audio or screen will require similar patterns
- Live metering adds small overhead (RMS per 100ms-ish buffers) but remains UI-responsive
- Device loss, permission denial and exclusive-mode conflicts become explicit error cases that must be tested
- WAV files are simple, widely supported, and suitable for later WhisperX ingestion

## Revisit criteria

- If stereo or spatial audio capture becomes a requirement
- If per-application audio (exclusive to Teams, etc.) is mandated
- If audio drift or sync issues emerge in multi-stream Sprint 1.6
