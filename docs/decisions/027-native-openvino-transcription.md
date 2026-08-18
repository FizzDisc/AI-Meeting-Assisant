# ADR 027: Native OpenVINO transcription backend

## Status

Accepted and manually validated in Sprint 3.5.2.

## Decision

Add `intel-gpu` as an explicit compute preference backed by Intel's native OpenVINO GenAI `WhisperPipeline`. It never silently falls back and initially supports only the separately converted Whisper Small FP16 model. The existing CTranslate2 model remains required as the selected catalog model and continues to serve CPU/CUDA jobs; model formats are not interchangeable.

The supervised transcription child process remains the cancellation boundary. It normalizes microphone and system audio separately, uses the local WhisperX/Pyannote VAD to create bounded speech windows, sends those windows to one compiled Intel-GPU pipeline, reconciles overlapping timestamps, merges both sources and then executes the existing optional speaker-diarization stage. Status files distinguish runtime/model loading, per-source speech detection, GPU decoding and diarization.

## Consequences

- Settings validate the OpenVINO runtime, dedicated model and Whisper Small selection before saving.
- Cancellation and application shutdown terminate VAD, GPU inference and diarization through the existing disposable process.
- No OpenVINO package or model is bundled or committed yet; installation and packaging remain explicit local lifecycle work.
- CPU Pyannote VAD and diarization dominate short recordings. A faster offline VAD may be evaluated later, but only with the same quality gate.
- Intel NPU and the rejected Optimum/Transformers adapter remain unavailable in the product UI.

## Validation

A two-minute UI transcription completed in 73.54 seconds and persisted `device=gpu`, `computeType=openvino-fp16` and `computePreference=intel-gpu`. It retained six microphone segments, one system-audio segment and one detected remote speaker. Manual cancellation during processing and closing the application both completed without leaving a worker process running.
