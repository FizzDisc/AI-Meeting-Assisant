# ADR 011: Interrupted session recovery and storage preflight

- Status: Accepted for Sprint 1.6.3
- Date: 2026-08-18

## Decision

On application startup, scan direct `session_*` workspaces. Atomically change stale `preparing` or `recording` manifests to `interrupted`. Preserve every artifact and preserve malformed manifests unchanged. Report missing media in the manifest recovery detail and corrupt/missing manifests to the UI.

Before capture, require at least 2 GiB free on the target drive. Reject start with an actionable error containing required and available capacity.

## Rationale

A process crash cannot finalize containers or write the normal terminal state. Treating those sessions as active forever is misleading, while deleting them could destroy recoverable meeting evidence. Explicit `interrupted` status supports later repair tooling.

The initial 2 GiB threshold is a conservative short-meeting guardrail, not a final capacity estimator. A future settings/capacity slice can calculate required space from planned duration, resolution and bitrate.

## Non-goals

This slice does not repair MP4/WAV containers, resume capture, delete abandoned workspaces or estimate long-recording capacity.
