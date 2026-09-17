# Reuse the model process for live batches

Low-priority transcription requests without diarization use one serial model
process. CPU/CUDA WhisperX and Intel OpenVINO models are retained between batches
with matching session, model, language and runtime settings. Ordinary full-file
transcription and final speaker analysis retain isolated one-shot processes.

The process is terminated on cancellation, failure, configuration/session change,
before final speaker analysis, or 120 seconds after a completed batch is observed
by the supervisor. Process exit releases native RAM/VRAM allocations. A completed
job retains its own terminal status so later queries/cancellations cannot affect
another job using the same process. Concurrent live submissions are rejected.

The child closes per-job log handles before publishing a completion marker. The
supervisor consumes that marker before reporting completion, preserving cleanup
safety. OpenVINO per-batch metrics are reset to avoid accumulating track results.
Transcript performance metadata includes `modelReused` and `modelLoadSeconds`;
the latter is zero for model reuse. Existing raw-result caches are independent.

Validation uses real process/IPC lifecycle tests with a lightweight fake runner,
plus an instrumented model loader to check reuse across distinct batches. Actual
GPU memory, throughput and energy savings require a hardware/model benchmark;
no percentage improvement is assumed.
