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
5. **AMA-105: Screen/window** — **Sprint 1.5.1 and 1.5.2 complete.** Record the selected display as a local H.264 MP4. The Windows adapter owns the native Media Foundation encoder behind `IScreenCaptureProvider`; oversized sources are proportionally downscaled to encoder-safe dimensions. Manual screen-only validation passed for both displays, including the 5160×2160 source at 3840×1608 output. Sprint 1.5.2 starts screen, system audio and microphone as one atomic lifecycle with a shared artifact timestamp and restores both live audio meters. Manual combined capture and binary artifact validation passed: video 16.633 s, system audio 16.559 s and microphone 16.290 s. The measured 74 ms/343 ms offsets become Sprint 1.6 alignment input; preview/window capture remain later slices.
6. **AMA-106: Session workspace** — **Sprint 1.6.1 through 1.6.3 complete.** Each recording creates a unique `session_*` directory containing all three media artifacts and an atomically replaced, versioned `manifest.json`. A shared monotonic clock and native media parsers produce an alignment classification. At startup, stale `preparing`/`recording` manifests are atomically marked `interrupted` without deleting artifacts; missing, empty and corrupt artifacts remain explicit diagnostics. A real forced-process-termination test recovered one session as `interrupted`, preserved its MP4 and microphone WAV and exposed the zero-byte system-audio WAV. Capture start requires at least 2 GiB free on the target drive. Durable promotion remains later work.
7. **AMA-107: Controls and consent** — Wire start/stop, elapsed time, clear status, consent and failure recovery into the shell. The right-hand card is now correctly labelled **Release roadmap** rather than Processing pipeline. **UI TODO:** redesign the record/stop control; the growing stack of button label, timer and two meters is functional but visually awkward.
8. **AMA-108: Smoke-test matrix** — **Sprint 1.4.3 and Sprint 1.5.3 complete.** Automated coverage includes rapid dual and combined start/stop, simultaneous stream faults, partial start rollback, stop failure, repeated shutdown, silent-loopback timeline preservation, runtime display failure, encoder-safe ultrawide sizing and actionable device/encoder errors. Cleanup of one stream continues even when another stream's stop fails. Manual validation passed combined rapid cycles, close during recording, playable non-empty final artifacts and a successful new recording after restart. Physical USB/display unplug, Teams, Bluetooth switching, sleep and long recordings remain broader matrix items.
9. **AMA-109: Live microphone handover** — Allow changing the microphone during an active meeting when a headset battery dies or the user moves to another device. Finalize the old microphone segment, start a new segment at a recorded session-clock offset, keep system audio running and expose the handover in the future session manifest. Do not append incompatible device formats into one WAV file.
10. **AMA-110: Live screen handover** — Allow switching the selected display during an active meeting without exposing file-management details in the UI. Prefer safe dynamic source reconfiguration; if dimensions or encoder constraints require it, store internal timestamped segments and present them as one meeting recording.

## Sprint 2 — local transcription

- **Sprint 2.1 complete:** package the Python entry script with the desktop build, supervise one long-lived hidden process, exchange correlated JSON-lines requests, enforce protocol version/timeouts, retain bounded stderr diagnostics, negotiate worker/runtime capabilities and stop the process with the app. Manual validation confirmed worker startup and clean worker shutdown.
- **Sprint 2.2 complete:** bootstrap an isolated `worker/.venv` with stable WhisperX 3.8.6, prefer that runtime automatically and report structured Python/package/FFmpeg/CUDA diagnostics without downloading model weights. Current upstream compatibility accepts Python 3.10 through 3.13. The supervised request budget is 30 seconds to tolerate native Torch/CUDA cold starts while remaining bounded. Manual validation confirmed `AI runtime ready · CPU · Python 3.13.14` after installation.
- Add media normalization and cancellable WhisperX transcription.
- Report model download, progress, hardware mode and actionable failures.
- Persist aligned timestamped transcript segments; add viewer and Markdown/JSON export.

### Cross-cutting UI

- **AMA-111: Status Center** — Replace the dashboard's release-roadmap card with an operational status surface for current jobs, progress, warnings, recoveries and a collapsible bounded technical log. Startup must expose explicit stages such as `Loading AI runtime…`, package/compute validation and `AI runtime ready` so native Torch/CUDA cold starts never look like a frozen application. Move the product roadmap to a separate secondary view rather than mixing planned releases with live application state.
- **AMA-112: Settings Center** — Add a dedicated settings surface with sections for Recording, Storage, AI Models, Privacy and Diagnostics. Persist versioned settings locally, validate changes before applying them and distinguish application defaults from per-recording overrides.
- **AMA-113: Recording profiles and export formats** — Keep robust capture masters as separate PCM WAV audio and H.264 MP4 video by default. Offer selectable quality profiles and later lossless/compressed exports such as FLAC, MP3 or M4A for audio and H.264/H.265 MP4 where supported. Do not encode microphone/system audio directly to a lossy format during capture; derive exports after successful finalization. Add screen modes `Off`, `Full motion` and a later storage-saving `Presentation/snapshot` mode. Fixed 5/15-second frame intervals are not a meeting default because they lose cursor movement, animations and visual context; evaluate slide-change detection before exposing interval capture.
- **AMA-114: Storage and retention settings** — Let users choose and validate the session storage location, show available space and estimated recording capacity, configure minimum-free-space guardrails, retention/deletion policy and separate model/cache locations. Moving an existing library must be explicit, resumable and verified.
- **AMA-115: Local Model Manager** — Ship no speech or diarization model by default. Present a curated compatible model catalog with task, languages, quality/speed class, download size, expected RAM/VRAM, license and source. Require explicit install, verify checksums, show download progress, support selecting a default/per-job model, offline import, update and removal. Keep the supervised worker/runtime separable from model payloads.

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
