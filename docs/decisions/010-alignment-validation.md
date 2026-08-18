# ADR 010: Post-capture alignment validation

- Status: Accepted for Sprint 1.6.2
- Date: 2026-08-18

## Decision

Validate synchronization after media finalization without modifying the artifacts. Read duration directly from PCM WAV headers and the ISO BMFF `mvhd` atom. For each stream calculate:

`expected end = provider start offset + media duration`

The alignment spread is the maximum expected end minus the minimum expected end. A session is `aligned` at 100 ms or less and `warning` above 100 ms.

## Rationale

Raw file durations differ legitimately because providers start sequentially. Comparing durations alone therefore exaggerates drift. Combining monotonic start offsets with container durations measures whether streams cover the same real session interval. Session shutdown/finalization overhead is reported separately and must not be mistaken for media drift.

## Failure behavior

Unsupported or malformed media does not destroy an otherwise finalized session. The manifest receives alignment status `unavailable` plus a diagnostic detail. Processing can then stop or request repair in a later recovery slice.

## Deferred

This slice measures alignment but does not pad, trim or remux media. Corrective normalization belongs to the processing pipeline once real measurements justify it.
