# ADR 014: AI hardware selection and fallback

## Status

Accepted for Sprint 2.4.

## Decision

The worker exposes three stable compute preferences: `automatic`, `prefer-cuda` and `cpu-only`. Automatic uses CUDA only when the installed Torch runtime reports a compatible NVIDIA device; otherwise it selects CPU/int8 and returns a human-readable fallback reason. Prefer-CUDA fails explicitly instead of silently running a potentially long CPU job. CPU-only overrides an available GPU.

CUDA jobs use float16. Batch size is selected conservatively from total device VRAM: 4 below 4 GiB, 8 below 8 GiB and 16 at or above 8 GiB. CPU jobs use int8 and batch size 2. Health diagnostics expose Torch/CUDA build versions, device name, VRAM, selected profile and supported preferences. Each transcript persists the actual preference, device, compute type, batch size and fallback reason.

## Consequences

- A CPU fallback is observable and explainable rather than accidental.
- The current Intel-only development machine remains fully supported through CPU/int8.
- CUDA packages are not installed on machines without compatible NVIDIA hardware.
- DirectML and other Intel/AMD acceleration paths require separate benchmarking and compatibility work because they are not interchangeable with CTranslate2 CUDA.
