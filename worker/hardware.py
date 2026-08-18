"""Hardware detection and deterministic inference-profile selection."""
from __future__ import annotations
from typing import Any

PREFERENCES = ("automatic", "prefer-cuda", "cpu-only")


def select_compute(torch_api: Any, preference: str = "automatic") -> dict[str, Any]:
    if preference not in PREFERENCES:
        raise ValueError(f"Unknown compute preference: {preference}")
    cuda_available = bool(torch_api.cuda.is_available())
    if preference == "prefer-cuda" and not cuda_available:
        raise RuntimeError("CUDA was requested but no compatible NVIDIA CUDA device/runtime is available.")
    use_cuda = cuda_available and preference != "cpu-only"
    if not use_cuda:
        reason = None
        if preference == "cpu-only": reason = "CPU-only mode was selected."
        elif not cuda_available: reason = "No compatible NVIDIA CUDA device/runtime was detected."
        return {"preference": preference, "mode": "cpu", "computeType": "int8", "batchSize": 2,
                "cudaAvailable": cuda_available, "deviceName": None, "totalVramBytes": None,
                "fallbackReason": reason}

    properties = torch_api.cuda.get_device_properties(0)
    vram = int(properties.total_memory)
    batch_size = 4 if vram < 4 * 1024**3 else 8 if vram < 8 * 1024**3 else 16
    return {"preference": preference, "mode": "cuda", "computeType": "float16", "batchSize": batch_size,
            "cudaAvailable": True, "deviceName": torch_api.cuda.get_device_name(0),
            "totalVramBytes": vram, "fallbackReason": None}


def detect_hardware(preference: str = "automatic") -> dict[str, Any]:
    import torch
    result = select_compute(torch, preference)
    result["torchVersion"] = str(torch.__version__)
    result["torchCudaVersion"] = torch.version.cuda
    result["supportedPreferences"] = list(PREFERENCES)
    return result
