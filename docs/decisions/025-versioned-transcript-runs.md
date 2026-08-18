# ADR 025: Versioned transcript runs

## Decision

Each successful transcription writes a unique `processing/transcript_{timestamp}_{modelId}.json` containing model ID, actual compute metadata and end-to-end processing duration. After success, the desktop atomically copies that artifact to `processing/transcript.json` as a backward-compatible latest-run pointer.

The run catalog includes legacy canonical-only transcripts and deduplicates a canonical pointer that matches a versioned run. The Meeting Library selects a model for the next job and a preserved run to open. Re-transcription never deletes prior successful outputs.

## Consequences

- The same recording can be compared across models without overwriting evidence.
- Existing library/transcript discovery continues to work through the canonical pointer.
- Meeting-scoped speaker names remain shared because all runs occupy the same processing directory.
- Side-by-side text diff and quality scoring are future presentation features; this slice preserves and exposes the underlying comparable data.
