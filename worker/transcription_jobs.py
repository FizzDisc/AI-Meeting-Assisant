from __future__ import annotations
import json, subprocess, sys, threading, uuid
from pathlib import Path
from typing import Any

class TranscriptionJobManager:
    def __init__(self, job_script: Path | None = None) -> None:
        self._job_script = (job_script or Path(__file__).with_name("transcription_job.py")).resolve()
        self._finalization_script = Path(__file__).with_name("incremental_finalize_job.py").resolve()
        self._jobs: dict[str, dict[str, Any]] = {}
        self._lock = threading.Lock()

    def start(self, payload: dict[str, Any]) -> dict[str, Any]:
        audio_paths = [Path(item).resolve() for item in payload.get("audioPaths", [])]
        model_path, output_text = Path(payload.get("modelPath", "")).resolve(), str(payload.get("outputPath", "")).strip()
        preference = str(payload.get("computePreference", "automatic"))
        if not audio_paths or len(audio_paths) > 2: raise ValueError("One or two audioPaths are required.")
        missing = next((path for path in audio_paths if not path.is_file()), None)
        if missing: raise FileNotFoundError(f"Audio input not found: {missing}")
        if not model_path.is_dir(): raise FileNotFoundError(f"Local model directory not found: {model_path}")
        openvino_model_text = str(payload.get("openVinoModelPath") or "").strip()
        openvino_runtime_text = str(payload.get("openVinoRuntimePath") or "").strip()
        silero_text = str(payload.get("sileroVadPath") or "").strip()
        torch_xpu_text = str(payload.get("torchXpuRuntimePath") or "").strip()
        openvino_model = Path(openvino_model_text).resolve() if openvino_model_text else None
        openvino_runtime = Path(openvino_runtime_text).resolve() if openvino_runtime_text else None
        silero_path = Path(silero_text).resolve() if silero_text else None
        torch_xpu_path = Path(torch_xpu_text).resolve() if torch_xpu_text else None
        if preference == "intel-gpu":
            if openvino_model is None or not openvino_model.is_dir():
                raise FileNotFoundError("The selected OpenVINO speech model is not installed.")
            if openvino_runtime is None or not openvino_runtime.is_dir():
                raise FileNotFoundError("The local OpenVINO runtime is not installed.")
            if silero_path is None or not silero_path.is_dir():
                raise FileNotFoundError("The optimized local Silero VAD is not installed.")
            if torch_xpu_path is not None and not (torch_xpu_path / "torch" / "lib" / "c10_xpu.dll").is_file():
                raise FileNotFoundError("The selected Intel XPU runtime is invalid.")
        if not output_text: raise ValueError("outputPath is required.")
        output_path, job_id = Path(output_text).resolve(), uuid.uuid4().hex
        output_path.parent.mkdir(parents=True, exist_ok=True)
        status_path = output_path.parent / f"transcription-{job_id}.status.json"
        request_path = output_path.parent / f"transcription-{job_id}.request.json"
        diarization_text = str(payload.get("diarizationModelPath") or "").strip()
        diarization_path = Path(diarization_text).resolve() if diarization_text else None
        if diarization_path is not None and not (diarization_path / "config.yaml").is_file():
            raise FileNotFoundError(f"Local diarization model is invalid: {diarization_path}")
        source_labels = payload.get("sourceLabels")
        if source_labels is not None:
            if not isinstance(source_labels, list) or len(source_labels) != len(audio_paths):
                raise ValueError("sourceLabels must match audioPaths.")
            if any(label not in ("microphone", "system_audio") for label in source_labels):
                raise ValueError("sourceLabels contains an unsupported source.")
        request = {"audioPaths": [str(p) for p in audio_paths], "modelPath": str(model_path),
                   "outputPath": str(output_path), "statusPath": str(status_path), "language": payload.get("language"),
                   "computePreference": preference,
                   "openVinoModelPath": str(openvino_model) if openvino_model else None,
                   "openVinoRuntimePath": str(openvino_runtime) if openvino_runtime else None,
                   "sileroVadPath": str(silero_path) if silero_path else None,
                   "torchXpuRuntimePath": str(torch_xpu_path) if torch_xpu_path else None,
                   "modelId": payload.get("modelId"),
                   "diarizationModelPath": str(diarization_path) if diarization_path else None,
                   "sourceLabels": source_labels}
        request_path.write_text(json.dumps(request), encoding="utf-8")
        log_path = output_path.parent / f"transcription-{job_id}.worker.log"
        log_handle = log_path.open("w", encoding="utf-8")
        try:
            creationflags = 0x00004000 if sys.platform == "win32" and payload.get("lowPriority") else 0
            process = subprocess.Popen([sys.executable, "-u", str(self._job_script), str(request_path)],
                                       stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                                       stderr=log_handle, text=True, creationflags=creationflags)
        except Exception:
            log_handle.close()
            raise
        with self._lock:
            self._jobs[job_id] = {"process": process, "statusPath": status_path,
                                  "logPath": log_path, "logHandle": log_handle}
        return {"jobId": job_id, "status": "queued", "progress": 0.0}

    def start_incremental_finalize(self, payload: dict[str, Any]) -> dict[str, Any]:
        required = ("mergedTranscriptPath", "systemAudioPath", "outputPath")
        values = {name: Path(str(payload.get(name, ""))).resolve() for name in required}
        for name in ("mergedTranscriptPath", "systemAudioPath"):
            if not values[name].is_file(): raise FileNotFoundError(f"{name} not found: {values[name]}")
        diarization_text = str(payload.get("diarizationModelPath") or "").strip()
        diarization = Path(diarization_text).resolve() if diarization_text else None
        if diarization is not None and not (diarization / "config.yaml").is_file():
            raise FileNotFoundError(f"Local diarization model is invalid: {diarization}")
        output_path, job_id = values["outputPath"], uuid.uuid4().hex
        output_path.parent.mkdir(parents=True, exist_ok=True)
        status_path = output_path.parent / f"incremental-finalize-{job_id}.status.json"
        request_path = output_path.parent / f"incremental-finalize-{job_id}.request.json"
        request = {"mergedTranscriptPath": str(values["mergedTranscriptPath"]),
                   "systemAudioPath": str(values["systemAudioPath"]), "outputPath": str(output_path),
                   "statusPath": str(status_path), "diarizationModelPath": str(diarization) if diarization else None,
                   "torchXpuRuntimePath": payload.get("torchXpuRuntimePath"),
                   "computePreference": payload.get("computePreference", "automatic")}
        request_path.write_text(json.dumps(request), encoding="utf-8")
        log_path = output_path.parent / f"incremental-finalize-{job_id}.worker.log"
        log_handle = log_path.open("w", encoding="utf-8")
        try:
            process = subprocess.Popen([sys.executable, "-u", str(self._finalization_script), str(request_path)],
                                       stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                                       stderr=log_handle, text=True)
        except Exception:
            log_handle.close(); raise
        with self._lock:
            self._jobs[job_id] = {"process": process, "statusPath": status_path,
                                  "logPath": log_path, "logHandle": log_handle}
        return {"jobId": job_id, "status": "queued", "progress": 0.0}

    def status(self, job_id: str) -> dict[str, Any]:
        job, result = self._get(job_id), {"status": "queued", "progress": 0.0}
        process, status_path = job["process"], Path(job["statusPath"])
        if status_path.is_file():
            try: result = json.loads(status_path.read_text(encoding="utf-8"))
            except (OSError, json.JSONDecodeError): pass
        result["jobId"] = job_id
        exit_code = process.poll()
        if result.get("status") in ("completed", "failed", "cancelled") and exit_code is None:
            # A job writes its terminal status atomically immediately before
            # exiting. Wait briefly so its inherited diagnostic-log handle is
            # released before the desktop starts deleting temporary artifacts.
            try:
                exit_code = process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                exit_code = None
        if exit_code is not None:
            self._close_log(job)
        if exit_code not in (None, 0) and result.get("status") not in ("failed", "cancelled"):
            log_path = Path(job["logPath"])
            stderr = log_path.read_text(encoding="utf-8", errors="replace")[-4000:] if log_path.is_file() else ""
            result = {"jobId": job_id, "status": "failed", "progress": 0.0,
                      "exitCode": exit_code, "diagnosticLogPath": str(log_path),
                      "error": stderr.strip() or f"Transcription process exited with code {exit_code}."}
            status_path.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        return result

    def cancel(self, job_id: str) -> dict[str, Any]:
        job, status = self._get(job_id), {"jobId": job_id, "status": "cancelled", "progress": 0.0}
        process = job["process"]
        if process.poll() is not None:
            return self.status(job_id)
        process.terminate()
        try: process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill(); process.wait(timeout=5)
        self._close_log(job)
        Path(job["statusPath"]).write_text(json.dumps(status), encoding="utf-8")
        return status

    def shutdown(self) -> None:
        with self._lock: ids = list(self._jobs)
        for job_id in ids:
            try: self.cancel(job_id)
            except Exception: pass

    def _get(self, job_id: str) -> dict[str, Any]:
        with self._lock: job = self._jobs.get(job_id)
        if job is None: raise KeyError(f"Unknown transcription job: {job_id}")
        return job

    @staticmethod
    def _close_log(job: dict[str, Any]) -> None:
        handle = job.get("logHandle")
        if handle is not None and not handle.closed:
            handle.flush()
            handle.close()
