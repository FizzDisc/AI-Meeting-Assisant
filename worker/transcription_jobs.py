from __future__ import annotations
import json, subprocess, sys, threading, uuid
from pathlib import Path
from typing import Any

class TranscriptionJobManager:
    def __init__(self, job_script: Path | None = None) -> None:
        self._job_script = (job_script or Path(__file__).with_name("transcription_job.py")).resolve()
        self._jobs: dict[str, dict[str, Any]] = {}
        self._lock = threading.Lock()

    def start(self, payload: dict[str, Any]) -> dict[str, Any]:
        audio_paths = [Path(item).resolve() for item in payload.get("audioPaths", [])]
        model_path, output_text = Path(payload.get("modelPath", "")).resolve(), str(payload.get("outputPath", "")).strip()
        if not audio_paths or len(audio_paths) > 2: raise ValueError("One or two audioPaths are required.")
        missing = next((path for path in audio_paths if not path.is_file()), None)
        if missing: raise FileNotFoundError(f"Audio input not found: {missing}")
        if not model_path.is_dir(): raise FileNotFoundError(f"Local model directory not found: {model_path}")
        if not output_text: raise ValueError("outputPath is required.")
        output_path, job_id = Path(output_text).resolve(), uuid.uuid4().hex
        output_path.parent.mkdir(parents=True, exist_ok=True)
        status_path = output_path.parent / f"transcription-{job_id}.status.json"
        request_path = output_path.parent / f"transcription-{job_id}.request.json"
        request = {"audioPaths": [str(p) for p in audio_paths], "modelPath": str(model_path),
                   "outputPath": str(output_path), "statusPath": str(status_path), "language": payload.get("language")}
        request_path.write_text(json.dumps(request), encoding="utf-8")
        process = subprocess.Popen([sys.executable, "-u", str(self._job_script), str(request_path)],
                                   stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                                   stderr=subprocess.PIPE, text=True)
        with self._lock: self._jobs[job_id] = {"process": process, "statusPath": status_path}
        return {"jobId": job_id, "status": "queued", "progress": 0.0}

    def status(self, job_id: str) -> dict[str, Any]:
        job, result = self._get(job_id), {"status": "queued", "progress": 0.0}
        process, status_path = job["process"], Path(job["statusPath"])
        if status_path.is_file(): result = json.loads(status_path.read_text(encoding="utf-8"))
        result["jobId"] = job_id
        exit_code = process.poll()
        if exit_code not in (None, 0) and result.get("status") not in ("failed", "cancelled"):
            stderr = process.stderr.read()[-2000:] if process.stderr else ""
            result = {"jobId": job_id, "status": "failed", "progress": 0.0,
                      "error": stderr.strip() or f"Transcription process exited with code {exit_code}."}
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
        if process.stderr: process.stderr.close()
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
