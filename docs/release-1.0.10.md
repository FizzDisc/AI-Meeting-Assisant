# 1.0.10 — remove affected inference dependencies

The standard runtime uses Faster-Whisper directly instead of the WhisperX wrapper.
Transformers and NLTK are no longer installed. Existing Tiny/Small/Medium model
files are reused. Transcription remains local with CTranslate2, batched decoding,
per-track language detection and cached model reuse. Voice activity detection
uses the local Silero ONNX model, so segment boundaries may change.

Setup opens once after upgrade and requires a new runtime without the retired
packages, or allows recording-only operation. Old runtime files are retained
until explicitly cleaned; the active pointer switches only after validation.

The signing application is pending. The Windows installer remains unsigned.
A clean-Windows MSI lifecycle test could not be executed: Sandbox/Hyper-V tools
were not available locally, and automatic safety review blocked creation of the
proposed CI installation/uninstallation script. No such CI test was submitted.

See [dependency review](security-review-2026-10.md) for scope and limitations.

## Verification

57 Python tests plus five subtests pass. Windows setup, runtime integrity,
cancellation, cleanup and system-only capture regressions pass. The Release
solution builds without warnings. A fresh isolated runtime passed pip check,
actual backend imports, FFmpeg and worker health; checks confirmed that none of
WhisperX, Transformers or NLTK is installed. Tiny model download and offline
transcription passed on both a tone fixture and locally synthesized speech;
explicit English and automatic language selection produced nonempty transcripts
with valid timestamps. Model reuse is covered by regression tests.

This is CPU validation on the development Windows machine, not a clean-OS MSI
lifecycle test. GPU accuracy/performance and optional speaker models were not
revalidated. The user subsequently confirmed successful installation, system-only
recording, transcription and export on their Windows machine. This does not
replace a clean-OS lifecycle test. The two temporary runtime-validation
directories have been manually removed after testing.
