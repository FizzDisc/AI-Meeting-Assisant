# Product backlog

## Sprint 0 definition of done

- [x] Greenfield repository with no Marvin code or dependency
- [x] Native Windows shell and minimal recording dashboard
- [x] Capture abstractions without a production recorder
- [x] Versioned .NET/Python contract and worker health-check skeleton
- [x] Architecture, privacy guardrails and technology decision recorded
- [x] Ordered implementation backlog
- [x] Build on a machine with the .NET 8 SDK
- [x] Interactive UI smoke test after SDK installation

## Sprint 1 — trustworthy capture foundation

1. **AMA-101: Capture state machine** — **Done in Sprint 1.1.** Idle → preparing → recording → stopping → completed/failed, with guarded transitions and tests. The UI currently drives a clearly labelled no-media simulation.
2. **AMA-102: Select sources** — Enumerate screens/windows, render endpoints and microphones; remember user choices.
3. **AMA-103: System audio** — Record a selected WASAPI loopback endpoint with timestamps and device-loss handling.
4. **AMA-104: Microphone** — Record the selected input independently and display a live level meter.
5. **AMA-105: Screen/window** — Use Windows Graphics Capture with source selection and privacy-safe preview.
6. **AMA-106: Session workspace** — Create an atomic, resumable manifest and validate free disk space.
7. **AMA-107: Controls and consent** — Wire start/stop, elapsed time, clear status, consent and failure recovery into the shell.
8. **AMA-108: Smoke-test matrix** — Teams, USB/Bluetooth devices, switching, sleep and long recordings.

## Sprint 2 — local transcription

- Package and supervise the Python worker and negotiate capabilities.
- Add media normalization and cancellable WhisperX transcription.
- Report model download, progress, hardware mode and actionable failures.
- Persist aligned timestamped transcript segments; add viewer and Markdown/JSON export.

## Sprint 3 — speakers

- Integrate pyannote behind an adapter and merge speaker turns with WhisperX words.
- Let users rename/merge speakers and optionally remember identities locally.
- Expose ambiguous overlaps for manual correction.

## Sprint 4 — meeting intelligence

- Generate editable minutes linked to transcript timestamps.
- Extract action items, owners, due dates, decisions, risks and open questions.
- Keep every generated claim traceable to evidence.
- Local model first; remote providers require explicit opt-in.

## Later — knowledge base

- Search approved meeting artifacts with access boundaries.
- Link recurring topics, decisions and commitments over time.
- Add retention controls, selective indexing, export and permanent deletion.
- Evaluate encrypted storage and enterprise key management.

## Decisions before production capture

- Supported Windows builds and GPU baseline
- Whether per-application audio is required beyond endpoint loopback
- Recording consent language and regional compliance
- Default retention and deletion guarantees
- Installer, updates, Python and model distribution
