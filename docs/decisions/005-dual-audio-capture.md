# ADR 005: Separate synchronized-start audio streams

## Status

Accepted and validated in Sprint 1.4.2.

## Decision

Start the selected system-audio loopback endpoint and microphone as two independent WASAPI providers. Give both output filenames the same session timestamp, retain separate PCM16 WAV files and expose separate live levels. A failure to start either provider rolls back both. A runtime fault in either stream fails and cleans up the whole recording session.

## Consequences

- Users can verify both inputs independently while recording.
- Separate files preserve later transcription, mixing and synchronization options.
- The shared filename timestamp identifies the pair but is not yet sample-accurate synchronization; a monotonic session clock follows in Sprint 1.6.

## Validation

Automated validation passed with 22/22 Core tests and 7/7 Windows smoke checks, including partial-start rollback, shared filename timestamps, both capture modes and exactly-once cleanup. Two manual hardware tests passed. The final inspected pair was playable and measured 13.471 seconds for the four-channel microphone stream and 13.613 seconds for the stereo system-audio stream at 48 kHz/16-bit PCM, a 142 ms duration difference.
