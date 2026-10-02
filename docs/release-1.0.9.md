# 1.0.9 — safer local inference and first-run checks

- Force Hugging Face offline inference and local-only Whisper model loading;
  remove inherited Hugging Face tokens from inference environments.
- Disable pyannote usage metrics and Hugging Face telemetry in app-launched Python.
- Limit speech-model downloads to known payload files and license notices at a
  fixed repository revision. Download progress uses only the selected file sizes.
- Validate pip consistency, actual WhisperX ASR imports and FFmpeg. Welcome setup
  no longer treats package presence alone as readiness; checks have a timeout.
- Resolve FFmpeg beside an explicitly selected portable Python executable.
- Explain recording readiness, missing sources and system-audio-only recording
  beside the meters and on the recording button, including while disabled.
- Include verified license texts for eight previously unspecified dependencies.
- Run the full Python suite and first-run regression tests in release CI.

## Verification

Windows Release build and MSI packaging succeeded. 55 Python tests plus five
subtests passed. Windows runtime-download, integrity, extraction, cancellation,
cleanup, system-only capture and first-run regressions passed.

An isolated fresh runtime download/install passed pip check, WhisperX ASR import,
FFmpeg execution and worker health. A Tiny model downloaded from the official
catalog and completed offline processing. A separate locally synthesized spoken
sentence produced a nonempty transcript. No microphone or personal recordings
were used. Existing user runtime/models were not changed.

This ran on the development Windows machine using an isolated portable runtime,
not a clean Windows VM. MSI installation, upgrade and uninstall on a clean VM
remain unverified. Optional gated speaker-diarization models were not tested.
Pyannote warns that TorchCodec shared-FFmpeg decoding is unavailable; tested
paths load decoded audio in memory via our FFmpeg pipeline instead.

The installer remains unsigned. Six dependency security alerts remain open;
see [assessment and mitigations](security-review-2026-10.md). No GitHub release
assets were replaced and 1.0.9 has not been published as a GitHub Release.
