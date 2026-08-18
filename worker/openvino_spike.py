"""Isolated OpenVINO Whisper benchmark; not part of the production worker."""
from __future__ import annotations
import argparse, json, sys, threading, time, wave
from datetime import datetime, timezone
from pathlib import Path

def add_spike_packages(worker_dir: Path) -> Path:
    packages = worker_dir / ".openvino-spike"
    if not packages.is_dir():
        raise RuntimeError("OpenVINO spike runtime missing. Run scripts/setup-openvino-spike.ps1.")
    sys.path.insert(0, str(packages))
    return packages

def load_pcm16(path: Path):
    import numpy as np
    with wave.open(str(path), "rb") as handle:
        if handle.getsampwidth() != 2:
            raise RuntimeError("The benchmark currently accepts PCM16 WAV input.")
        channels, sample_rate = handle.getnchannels(), handle.getframerate()
        frames = handle.getnframes()
        audio = np.frombuffer(handle.readframes(frames), dtype="<i2").astype(np.float32) / 32768.0
    if channels > 1:
        audio = audio.reshape(-1, channels).mean(axis=1)
    duration = len(audio) / sample_rate
    if sample_rate != 16000 and len(audio):
        source_positions = np.arange(len(audio), dtype=np.float64)
        target_length = max(1, round(duration * 16000))
        target_positions = np.linspace(0, len(audio) - 1, target_length)
        audio = np.interp(target_positions, source_positions, audio).astype(np.float32)
    return audio, duration

def directory_size(path: Path) -> int:
    return sum(item.stat().st_size for item in path.rglob("*") if item.is_file())

def chunk_windows(duration: float, chunk_seconds: float = 30.0, overlap_seconds: float = 2.0):
    if duration <= 0: return []
    step = chunk_seconds - overlap_seconds
    if step <= 0: raise ValueError("Chunk overlap must be smaller than chunk length.")
    windows, start = [], 0.0
    while start < duration:
        end = min(duration, start + chunk_seconds)
        keep_start = start if not windows else start + overlap_seconds / 2
        keep_end = end if end >= duration else end - overlap_seconds / 2
        windows.append((start, end, keep_start, keep_end))
        if end >= duration: break
        start += step
    return windows

def write_report(path: Path, report: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f".{path.name}.tmp")
    temporary.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    temporary.replace(path)

def available_devices():
    import openvino as ov
    core = ov.Core()
    return [{"id": device, "name": core.get_property(device, "FULL_DEVICE_NAME")}
            for device in core.available_devices]

def with_heartbeat(device: str, action):
    stopped = threading.Event()
    started = time.monotonic()
    def report():
        print(f"{device} inference started...", flush=True)
        while not stopped.wait(15):
            elapsed = int(time.monotonic() - started)
            print(f"{device} inference active - {elapsed // 60:02d}:{elapsed % 60:02d} elapsed", flush=True)
    thread = threading.Thread(target=report, name=f"{device}-benchmark-heartbeat", daemon=True)
    thread.start()
    try:
        return action()
    finally:
        stopped.set()
        thread.join(timeout=1)

def run_once(model_path: Path, audio, duration: float, device: str, language: str | None):
    from optimum.intel import OVModelForSpeechSeq2Seq
    from transformers.utils import import_utils
    # TorchCodec is installed by WhisperX but unusable with its pinned Torch build.
    # The pipeline does not need it for an already decoded NumPy waveform.
    import_utils._torchcodec_available = False
    from transformers import AutoProcessor, pipeline
    started = time.perf_counter()
    processor = AutoProcessor.from_pretrained(model_path, local_files_only=True)
    model = OVModelForSpeechSeq2Seq.from_pretrained(
        model_path, device=device, local_files_only=True,
        ov_config={"CACHE_DIR": str(model_path / ".ov-cache")})
    loaded = time.perf_counter()
    transcriber = pipeline("automatic-speech-recognition", model=model,
                           tokenizer=processor.tokenizer, feature_extractor=processor.feature_extractor,
                           return_timestamps=True)
    generation = {"task": "transcribe"}
    if language: generation["language"] = language
    segments = []
    windows = chunk_windows(duration)
    for index, (start, end, keep_start, keep_end) in enumerate(windows, 1):
        print(f"{device} chunk {index}/{len(windows)} - {start:0.1f}s to {end:0.1f}s", flush=True)
        samples = audio[round(start * 16000):round(end * 16000)]
        result = with_heartbeat(device, lambda: transcriber(samples, generate_kwargs=generation))
        for item in result.get("chunks", []):
            text = item.get("text", "").strip()
            local_start, local_end = item["timestamp"]
            absolute_start = start + float(local_start or 0.0)
            absolute_end = start + (float(local_end) if local_end is not None else end - start)
            midpoint = (absolute_start + absolute_end) / 2
            if text and keep_start <= midpoint <= keep_end:
                segments.append({"start": max(absolute_start, keep_start),
                                 "end": min(absolute_end, keep_end, duration), "text": text})
    finished = time.perf_counter()
    inference = finished - loaded
    return {"device": device, "loadAndCompileSeconds": round(loaded - started, 3),
            "inferenceSeconds": round(inference, 3),
            "realTimeFactor": round(inference / duration, 4) if duration else None,
            "text": " ".join(item["text"] for item in segments), "chunks": segments}

def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("audio", type=Path)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--devices", nargs="+", default=["CPU", "GPU"])
    parser.add_argument("--language", default="de")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    worker_dir = Path(__file__).resolve().parent
    packages = add_spike_packages(worker_dir)
    model_path, audio_path = args.model.resolve(), args.audio.resolve()
    if not model_path.is_dir(): raise RuntimeError(f"OpenVINO model not found: {model_path}")
    if not audio_path.is_file(): raise RuntimeError(f"Audio not found: {audio_path}")
    detected = available_devices()
    available = {item["id"] for item in detected}
    missing = [device for device in args.devices if device not in available]
    if missing: raise RuntimeError(f"Requested OpenVINO device(s) unavailable: {', '.join(missing)}")
    audio, duration = load_pcm16(audio_path)
    output = args.output.resolve() if args.output else Path("artifacts/benchmarks/openvino-spike.json").resolve()
    report = {"schemaVersion": 1, "createdAtUtc": datetime.now(timezone.utc).isoformat(),
              "status": "running",
              "audioPath": str(audio_path), "audioDurationSeconds": round(duration, 3),
              "modelPath": str(model_path), "modelBytes": directory_size(model_path),
              "runtimeBytes": directory_size(packages), "availableDevices": detected,
              "results": []}
    write_report(output, report)
    try:
        for device in args.devices:
            report["currentDevice"] = device
            write_report(output, report)
            report["results"].append(run_once(model_path, audio, duration, device, args.language))
            write_report(output, report)
    except KeyboardInterrupt:
        report["status"] = "interrupted"
        report["interruptedAtUtc"] = datetime.now(timezone.utc).isoformat()
        write_report(output, report)
        print(f"Benchmark interrupted; completed results saved to {output}", flush=True)
        return 130
    report.pop("currentDevice", None)
    report["status"] = "completed"
    report["completedAtUtc"] = datetime.now(timezone.utc).isoformat()
    write_report(output, report)
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0

if __name__ == "__main__": raise SystemExit(main())
