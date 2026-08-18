"""JSON-lines boundary and local ML-runtime diagnostics.

Health checks never download or load model weights. Protocol responses go to
stdout; diagnostics belong on stderr.
"""

from __future__ import annotations

import importlib.metadata
import importlib.util
import json
import platform
import shutil
import sys
from typing import Any
from transcription_jobs import TranscriptionJobManager
from hardware import detect_hardware

PROTOCOL_VERSION = "1.0"
WORKER_VERSION = "0.5.0"
SUPPORTED_PYTHON = (3, 10) <= sys.version_info[:2] < (3, 14)
JOBS = TranscriptionJobManager()


def response(request_id: str, message_type: str, payload: dict[str, Any]) -> dict[str, Any]:
    return {"protocolVersion": PROTOCOL_VERSION, "requestId": request_id, "type": message_type,
            "ok": True, "payload": payload, "error": None}


def error(request_id: str, code: str, message: str) -> dict[str, Any]:
    return {"protocolVersion": PROTOCOL_VERSION, "requestId": request_id, "type": "error",
            "ok": False, "payload": {}, "error": {"code": code, "message": message}}


def package_status(distribution: str, module: str | None = None) -> dict[str, Any]:
    try:
        installed = importlib.util.find_spec(module or distribution.replace("-", "_")) is not None
    except ModuleNotFoundError:
        installed = False
    version = None
    if installed:
        try:
            version = importlib.metadata.version(distribution)
        except importlib.metadata.PackageNotFoundError:
            pass
    return {"installed": installed, "version": version}


def runtime_diagnostics() -> dict[str, Any]:
    packages = {
        "whisperx": package_status("whisperx"),
        "torch": package_status("torch"),
        "pyannoteAudio": package_status("pyannote-audio", "pyannote.audio"),
    }
    compute: dict[str, Any] = {"preference": "automatic", "mode": "cpu", "computeType": "int8",
                               "batchSize": 2, "cudaAvailable": False, "deviceName": None,
                               "totalVramBytes": None, "torchVersion": None, "torchCudaVersion": None,
                               "fallbackReason": "Torch is not installed.",
                               "supportedPreferences": ["automatic", "prefer-cuda", "cpu-only", "intel-gpu"]}
    if packages["torch"]["installed"]:
        try:
            compute = detect_hardware()
        except Exception as exc:
            compute["error"] = str(exc)

    ffmpeg_available = shutil.which("ffmpeg") is not None
    missing = [name for name, item in packages.items() if not item["installed"]]
    if not ffmpeg_available:
        missing.append("ffmpeg")
    ml_ready = SUPPORTED_PYTHON and not missing
    return {
        "pythonExecutable": sys.executable,
        "platform": platform.platform(),
        "packages": packages,
        "compute": compute,
        "ffmpegAvailable": ffmpeg_available,
        "mlReady": ml_ready,
        "missingRequirements": missing,
    }


def handle(message: dict[str, Any]) -> dict[str, Any]:
    request_id = str(message.get("requestId", "unknown"))
    if message.get("protocolVersion") != PROTOCOL_VERSION:
        return error(request_id, "unsupported_protocol", "Expected protocol version 1.0")
    if message.get("type") == "health.check":
        version = sys.version_info
        diagnostics = runtime_diagnostics()
        capabilities = ["health.check", "runtime.diagnostics", "transcription.jobs"]
        if diagnostics["mlReady"]:
            capabilities.append("transcription.run")
        return response(request_id, "health.result", {
            "status": "ready" if diagnostics["mlReady"] else "setup-required",
            "workerVersion": WORKER_VERSION,
            "pythonVersion": f"{version.major}.{version.minor}.{version.micro}",
            "runtimeSupported": SUPPORTED_PYTHON,
            "mlReady": diagnostics["mlReady"],
            "capabilities": capabilities,
            "diagnostics": diagnostics,
        })
    payload = message.get("payload") or {}
    try:
        if message.get("type") == "transcription.start":
            return response(request_id, "transcription.accepted", JOBS.start(payload))
        if message.get("type") == "transcription.status":
            return response(request_id, "transcription.status", JOBS.status(str(payload.get("jobId", ""))))
        if message.get("type") == "transcription.cancel":
            return response(request_id, "transcription.cancelled", JOBS.cancel(str(payload.get("jobId", ""))))
    except (ValueError, FileNotFoundError, KeyError) as exc:
        return error(request_id, "invalid_transcription_job", str(exc))
    return error(request_id, "unsupported_request", "Unsupported worker request type.")


def main() -> int:
    try:
        for line in sys.stdin:
            try:
                result = handle(json.loads(line))
            except (json.JSONDecodeError, TypeError, ValueError) as exc:
                result = error("unknown", "invalid_request", str(exc))
            print(json.dumps(result, separators=(",", ":")), flush=True)
    finally:
        JOBS.shutdown()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
