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
2. **AMA-102: Select sources** — **Done in Sprint 1.2.** Enumerate active displays, Windows render endpoints and microphone endpoints; identify Windows defaults, refresh on demand and preserve a still-available selection. Persisting choices across app restarts is deferred until settings storage exists.
3. **AMA-103: System audio** — **Done through Sprint 1.4.2.** Record the selected render endpoint and microphone simultaneously into independent `system_audio_*.wav` and `microphone_*.wav` files with a shared timestamp, separate live levels, one elapsed timer, atomic start rollback and exactly-once cleanup. Silent loopback gaps are filled to preserve wall-clock duration. Manual validation measured 13.471 s microphone and 13.613 s system audio (142 ms difference); sample-accurate synchronization remains Sprint 1.6 scope.
4. **AMA-104: Microphone** — **Done in Sprint 1.3.** Record the selected input independently via WASAPI `IAudioClient`, display a live RMS level meter and save finalized PCM16 WAV files under `artifacts/captures/`. Automated validation covers state transitions, format conversion, WAV production, fault/stop races and idempotent cleanup (20/20 Core tests plus 3/3 Windows smoke checks). Manual hardware validation passed with an Intel microphone array: 48 kHz, 16-bit, four-channel WAV, 8.35 seconds, playable output.
5. **AMA-105: Screen/window** — **Sprint 1.5.1 complete.** Record the selected display as a local H.264 MP4 with no audio track. The Windows adapter owns the native Media Foundation encoder behind `IScreenCaptureProvider`; oversized sources are proportionally downscaled to encoder-safe dimensions. Manual validation passed for both displays, including the 5160×2160 source at 3840×1608 output. Audio remains isolated until Sprint 1.5.2; preview/window capture remain later slices.
6. **AMA-106: Session workspace** — Create an atomic, resumable manifest and validate free disk space.
7. **AMA-107: Controls and consent** — Wire start/stop, elapsed time, clear status, consent and failure recovery into the shell. **UI TODO:** redesign the record/stop control; the growing stack of button label, timer and two meters is functional but visually awkward.
8. **AMA-108: Smoke-test matrix** — **Sprint 1.4.3 complete.** Automated coverage includes rapid dual start/stop, simultaneous stream faults, partial start rollback, stop failure, repeated shutdown, silent-loopback timeline preservation and actionable device-invalidated errors. Manual validation passed rapid cycles, close-during-recording and silent-loopback duration; physical USB unplug was not tested. Teams, Bluetooth switching, sleep and long recordings remain broader matrix items.
9. **AMA-109: Live microphone handover** — Allow changing the microphone during an active meeting when a headset battery dies or the user moves to another device. Finalize the old microphone segment, start a new segment at a recorded session-clock offset, keep system audio running and expose the handover in the future session manifest. Do not append incompatible device formats into one WAV file.
10. **AMA-110: Live screen handover** — Allow switching the selected display during an active meeting without exposing file-management details in the UI. Prefer safe dynamic source reconfiguration; if dimensions or encoder constraints require it, store internal timestamped segments and present them as one meeting recording.

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
