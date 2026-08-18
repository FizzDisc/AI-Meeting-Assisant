# ADR 026: Isolated OpenVINO spike for Intel acceleration

## Status

Sprint 3.5.1 evaluation complete; tested Optimum/OpenVINO backend rejected for production.

## Context

The production WhisperX path uses CTranslate2. Its Windows GPU backend targets NVIDIA CUDA and cannot execute on the Intel GPU in the reference laptop. Replacing that path without evidence would risk timestamps, source separation, cancellation and diarization.

OpenVINO officially supports Whisper on Intel hardware through [Optimum Intel](https://huggingface.co/docs/optimum-intel/openvino/reference), and its [inference documentation](https://huggingface.co/docs/optimum-intel/en/openvino/inference) supports selecting integrated Intel GPUs with `device="gpu"`. OpenVINO 2026.3 detects the reference laptop's Intel Core Ultra 5 135U CPU, Intel integrated GPU and Intel AI Boost NPU.

## Decision

Keep the production worker unchanged. Install the OpenVINO experiment into ignored `worker/.openvino-spike/` packages, use a separately downloaded OpenVINO Whisper model and benchmark the same normalized audio on CPU and GPU. Persist compile time, inference time, real-time factor, text, timestamps and package/model size as JSON.

Sprint 3.5.2 may integrate a backend only after a representative recording shows a material end-to-end improvement and acceptable transcript/timestamp quality. Diarization remains on the existing path during this spike.

## Consequences

- The experiment is reversible and cannot silently alter production inference.
- OpenVINO models require separate weights; existing CTranslate2 model directories are not reusable.
- GPU compilation cost and cache warm-up are measured separately from inference.
- Packaging cost is explicit in the benchmark report.

## Initial measurements

The monolithic 39:54 long-form comparison was manually interrupted after 29 minutes, proving that the Transformers wrapper did not scale acceptably. The revised overlapping 30-second path completed a 199.781-second system-audio sample in 47.739 seconds on OpenVINO CPU and 24.484 seconds on Intel GPU, a 48.7% GPU inference reduction. Text was near-identical with minor decoding differences. A full representative meeting transcript remains mandatory before production integration. The isolated runtime is about 252 MB and the FP16 small model about 1.56 GB.

The final 2,393.88-second GPU meeting run completed in 820.911 seconds (RTF 0.3429). It produced 479 segments and 52,418 characters, including 129 adjacent exact duplicates and long repeated-phrase hallucinations. The validated WhisperX transcript contains 16,503 characters for the same system-audio source. This fails the quality gate regardless of successful Intel GPU execution and prevents Sprint 3.5.2 production integration of this backend.
