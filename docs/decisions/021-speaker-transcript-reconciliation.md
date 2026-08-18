# ADR 021: Speaker-labelled transcript reconciliation

## Decision

Transcription and diarization run as phases of the same isolated, cancellable worker job. WhisperX transcribes the microphone and system-audio tracks separately. The microphone is labelled `You`; pyannote runs only on normalized system audio and its exclusive turns are reconciled with system-audio transcript timestamps.

For every system segment, overlap duration is accumulated per anonymous speaker. A label is accepted only when the best speaker covers at least 50% of the segment and leads the second candidate by at least 15 percentage points. Otherwise the segment is persisted as `ambiguous` or `unassigned`, including candidate overlap ratios for later correction. Transcript schema 3 retains the source label and adds speaker assignment metadata.

## Consequences

- The UI never invents a speaker identity from audio alone.
- Cancellation still terminates one inference child process.
- Old schema 1/2 transcripts remain readable.
- Segment-level timestamps limit precision; future word alignment can improve reconciliation without changing the capture format.
