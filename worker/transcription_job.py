"""Isolated WhisperX job; process isolation makes native inference cancellable."""
from __future__ import annotations
import hashlib, json, os, subprocess, sys, threading, time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from hardware import select_compute, select_intel_compute

_xpu_dll_handles = []

def activate_torch_xpu(runtime_path: Path) -> None:
    """Make the isolated Intel Torch runtime importable before importing torch."""
    for directory in (runtime_path / "Library" / "bin", runtime_path / "bin", runtime_path / "torch" / "lib"):
        if directory.is_dir():
            if hasattr(os, "add_dll_directory"):
                _xpu_dll_handles.append(os.add_dll_directory(str(directory)))
            os.environ["PATH"] = f"{directory}{os.pathsep}{os.environ.get('PATH', '')}"
    sys.path.insert(0, str(runtime_path))

def write_atomic(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f".{path.name}.{os.getpid()}.{threading.get_ident()}.tmp")
    try:
        temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
        for attempt in range(8):
            try:
                os.replace(temporary, path)
                return
            except PermissionError:
                if attempt == 7: raise
                time.sleep(0.025 * (attempt + 1))
    finally:
        try: temporary.unlink(missing_ok=True)
        except OSError: pass

def fingerprint(path: Path) -> dict[str, Any]:
    stat = path.stat()
    return {"path": str(path), "size": stat.st_size, "modifiedNs": stat.st_mtime_ns,
            "directory": path.is_dir()}

def stage_key(value: dict[str, Any]) -> str:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()

def load_stage_cache(path: Path, key: str) -> dict[str, Any] | None:
    try:
        cached = json.loads(path.read_text(encoding="utf-8"))
        return cached if cached.get("schemaVersion") == 1 and cached.get("key") == key else None
    except (OSError, ValueError, TypeError):
        return None

def write_stage_cache(path: Path, key: str, **value: Any) -> None:
    write_atomic(path, {"schemaVersion": 1, "key": key, **value})

def normalize_audio(source: Path, output: Path) -> None:
    command = ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", str(source),
               "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", str(output)]
    subprocess.run(command, check=True, capture_output=True, text=True)

def source_name(path: Path, index: int) -> str:
    name = path.stem.lower()
    if name.startswith("microphone_"): return "microphone"
    if name.startswith("system_audio_"): return "system_audio"
    return f"track_{index + 1}"

def merge_segments(results: list[tuple[str, dict[str, Any]]]) -> list[dict[str, Any]]:
    segments = []
    for source, result in results:
        segments.extend({"start": float(item["start"]), "end": float(item["end"]),
                         "text": str(item["text"]).strip(), "source": source}
                        for item in result.get("segments", []) if str(item["text"]).strip())
    return sorted(segments, key=lambda item: (item["start"], item["end"], item["source"]))

def compress_speech_audio(audio: dict[str, Any], windows: list[dict[str, Any]], separator_seconds: float = 0.25):
    import torch
    rate, waveform = int(audio["sample_rate"]), audio["waveform"]
    pieces, mapping, cursor = [], [], 0.0
    for index, window in enumerate(windows):
        start, end = float(window["start"]), float(window["end"])
        piece = waveform[:, round(start*rate):round(end*rate)]
        piece_seconds = piece.shape[1]/rate
        if piece_seconds <= 0: continue
        pieces.append(piece)
        mapping.append({"compressedStart": cursor, "compressedEnd": cursor+piece_seconds,
                        "originalStart": start, "originalEnd": end})
        cursor += piece_seconds
        if index < len(windows)-1:
            pieces.append(torch.zeros((waveform.shape[0], round(separator_seconds*rate)), dtype=waveform.dtype))
            cursor += separator_seconds
    return {"waveform": torch.cat(pieces, dim=1), "sample_rate": rate}, mapping

def restore_turn_timestamps(turns: list[dict[str, Any]], mapping: list[dict[str, float]]) -> list[dict[str, Any]]:
    restored = []
    for turn in turns:
        for item in mapping:
            start=max(float(turn["start"]),item["compressedStart"]);end=min(float(turn["end"]),item["compressedEnd"])
            if end<=start: continue
            restored.append({"start":item["originalStart"]+(start-item["compressedStart"]),
                             "end":min(item["originalStart"]+(end-item["compressedStart"]),item["originalEnd"]),
                             "speaker":turn["speaker"]})
    return restored

def configure_diarization_profile(pipeline: Any, device: str) -> dict[str, Any]:
    if device == "xpu":
        pipeline.segmentation_batch_size = 32
        pipeline.embedding_batch_size = 8
        pipeline._segmentation.step = pipeline._segmentation.duration * 0.15
    return {"segmentation": int(pipeline.segmentation_batch_size),
            "embedding": int(pipeline.embedding_batch_size),
            "segmentationStep": round(pipeline._segmentation.step / pipeline._segmentation.duration, 3)}

def diarize_system_audio(model_path: Path, normalized: list[tuple[str, Path]], status_path: Path,
                         speech_windows: list[dict[str, Any]] | None = None,
                         device: str = "cpu", xpu_runtime: Path | None = None) -> tuple[list[dict[str, Any]], int, dict[str, Any]]:
    from diarization_job import extract, load_pcm16
    system_path = next((path for label, path in normalized if label == "system_audio"), None)
    if system_path is None: return [], 0, {}
    write_atomic(status_path, {"status": "loading-diarization-model", "progress": 0.91})
    if device == "xpu":
        if xpu_runtime is None: raise RuntimeError("Intel XPU diarization was selected without an XPU runtime.")
        token = stage_key({"status": str(status_path), "time": time.time_ns()})[:12]
        request_path = status_path.parent / f".xpu-diarization-{token}.request.json"
        result_path = status_path.parent / f".xpu-diarization-{token}.result.json"
        write_atomic(request_path, {"modelPath": str(model_path), "audioPath": str(system_path),
                                    "speechWindows": speech_windows, "runtimePath": str(xpu_runtime),
                                    "resultPath": str(result_path)})
        write_atomic(status_path, {"status": "diarizing", "progress": 0.94,
                                   "source": "system_audio", "device": "xpu"})
        try:
            completed = subprocess.run([sys.executable, "-u", str(Path(__file__).with_name("xpu_diarization_stage.py")),
                                        str(request_path)], capture_output=True, text=True)
            if completed.returncode != 0:
                raise RuntimeError(f"Intel XPU diarization failed: {completed.stderr.strip() or completed.stdout.strip()}")
            result = json.loads(result_path.read_text(encoding="utf-8"))
            return result["turns"], int(result["speakerCount"]), result["profile"]
        finally:
            request_path.unlink(missing_ok=True)
            result_path.unlink(missing_ok=True)
    from pyannote.audio import Pipeline
    pipeline = Pipeline.from_pretrained(model_path)
    # Intel integrated GPUs perform best when segmentation keeps the model's
    # native batch while the heavier speaker embedding uses smaller batches.
    profile = configure_diarization_profile(pipeline, device)
    write_atomic(status_path, {"status": "diarizing", "progress": 0.94,
                               "source": "system_audio", "device": device})
    audio = load_pcm16(system_path)
    mapping = None
    if speech_windows:
        audio, mapping = compress_speech_audio(audio, speech_windows)
    turns = extract(pipeline(audio))
    if mapping is not None: turns = restore_turn_timestamps(turns, mapping)
    return turns, len({turn["speaker"] for turn in turns}), profile

def reconcile_without_diarization(segments: list[dict[str, Any]]) -> list[dict[str, Any]]:
    result = []
    for segment in segments:
        item = dict(segment)
        known = item.get("source") == "microphone"
        item.update(speaker="You" if known else None,
                    speakerAssignment="known-source" if known else "not-run",
                    speakerOverlapRatio=1.0 if known else 0.0)
        result.append(item)
    return result

def run(request_path: Path) -> int:
    started = time.monotonic()
    request = json.loads(request_path.read_text(encoding="utf-8"))
    inputs = [Path(item).resolve() for item in request["audioPaths"]]
    model_path, output_path = Path(request["modelPath"]).resolve(), Path(request["outputPath"]).resolve()
    status_path = Path(request["statusPath"]).resolve()
    preference = request.get("computePreference", "automatic")
    cache_directory = output_path.parent / "cache"
    raw_key = stage_key({"version": 1, "sources": [fingerprint(path) for path in inputs],
                         "model": fingerprint(model_path), "modelId": request.get("modelId"),
                         "language": request.get("language"), "preference": preference,
                         "openVinoModelPath": request.get("openVinoModelPath")})
    raw_cache_path = cache_directory / f"raw-transcription-{raw_key[:16]}.json"
    cached_raw = load_stage_cache(raw_cache_path, raw_key)
    phase_started = time.monotonic()
    normalized = []
    for index, source in enumerate(inputs):
        label = source_name(source, index)
        normalized_path = output_path.parent / f"normalized_{label}.wav"
        if cached_raw is None or not normalized_path.is_file():
            write_atomic(status_path, {"status": "normalizing", "progress": 0.1})
            normalize_audio(source, normalized_path)
        normalized.append((label, normalized_path))
    normalization_seconds = time.monotonic()-phase_started
    xpu_runtime_text = request.get("torchXpuRuntimePath") if preference == "intel-gpu" else None
    xpu_runtime = Path(xpu_runtime_text).resolve() if xpu_runtime_text else None
    xpu_available = bool(xpu_runtime and xpu_runtime.is_dir())
    if preference == "intel-gpu":
        compute = select_intel_compute()
    else:
        import torch
        compute = select_compute(torch, preference)
    device, compute_type, batch_size = compute["mode"], compute["computeType"], compute["batchSize"]
    model_seconds, openvino_metrics, raw_cache_hit = 0.0, None, cached_raw is not None
    if cached_raw:
        write_atomic(status_path, {"status": "reusing-transcription", "progress": 0.85})
        results = [(item["source"], item["result"]) for item in cached_raw["results"]]
        openvino_metrics = cached_raw.get("openVinoMetrics")
    else:
        write_atomic(status_path, {"status": "loading-openvino-model" if preference == "intel-gpu" else "loading-model", "progress": 0.25})
        model_started = time.monotonic()
        if preference == "intel-gpu":
            from openvino_backend import OpenVinoTranscriber
            vad_model = Path(sys.executable).resolve().parent.parent / "Lib/site-packages/whisperx/assets/pytorch_model.bin"
            model = OpenVinoTranscriber(Path(request["openVinoRuntimePath"]), Path(request["openVinoModelPath"]),
                                        vad_model, request.get("language") or "de",
                                        silero_repository=Path(request["sileroVadPath"]))
        else:
            import whisperx
            model = whisperx.load_model(str(model_path), device, compute_type=compute_type, language=request.get("language"))
        model_seconds = time.monotonic()-model_started
        results = []
        for index, (label, normalized_path) in enumerate(normalized):
            base, span = 0.35 + 0.5 * index / len(normalized), 0.5 / len(normalized)
            if preference == "intel-gpu":
                write_atomic(status_path, {"status": "detecting-speech", "progress": base,
                                           "source": label, "device": device, "computeType": compute_type})
                def progress(window_index, window_count, _start, _end):
                    fraction = (window_index - 1) / max(window_count, 1)
                    write_atomic(status_path, {"status": "transcribing", "progress": base + span * fraction,
                                               "source": label, "device": device, "computeType": compute_type,
                                               "currentWindow": window_index, "totalWindows": window_count})
                results.append((label, model.transcribe(normalized_path, progress)))
            else:
                write_atomic(status_path, {"status": "transcribing", "progress": base,
                                           "source": label, "device": device, "computeType": compute_type,
                                           "batchSize": batch_size, "fallbackReason": compute["fallbackReason"]})
                import whisperx
                audio = whisperx.load_audio(str(normalized_path))
                results.append((label, model.transcribe(audio, batch_size=batch_size)))
        openvino_metrics = model.metrics if preference == "intel-gpu" else None
        write_stage_cache(raw_cache_path, raw_key,
                          results=[{"source": source, "result": result} for source, result in results],
                          openVinoMetrics=openvino_metrics)
    detected_languages = {source: result.get("language") for source, result in results}
    languages = {language for language in detected_languages.values() if language}
    segments = merge_segments(results)
    diarization_path_text = request.get("diarizationModelPath")
    speaker_count = 0
    diarization_seconds = 0.0
    diarization_batches = None
    diarization_cache_hit = False
    diarization_skipped_reason = None
    system_result = next((result for source, result in results if source == "system_audio"), None)
    system_speech_seconds = float(system_result.get("speechSeconds", 0.0)) if system_result else 0.0
    if diarization_path_text and preference == "intel-gpu" and system_speech_seconds < 2.0:
        diarization_skipped_reason = f"System audio contains only {system_speech_seconds:.2f} seconds of detected speech."
        segments = reconcile_without_diarization(segments)
    elif diarization_path_text:
        from speaker_reconciliation import reconcile
        windows = system_result.get("speechWindows") if preference == "intel-gpu" and system_result else None
        diarization_path = Path(diarization_path_text).resolve()
        diarization_device = "xpu" if xpu_available else "cpu"
        diarization_key = stage_key({"version": 1, "systemAudio": fingerprint(next(path for label, path in normalized if label == "system_audio")),
                                     "model": fingerprint(diarization_path), "device": diarization_device,
                                     "windows": windows, "xpuProfile": {"segmentation": 32, "embedding": 8, "step": 0.15}})
        diarization_cache_path = cache_directory / f"speaker-turns-{diarization_key[:16]}.json"
        cached_diarization = load_stage_cache(diarization_cache_path, diarization_key)
        if cached_diarization:
            write_atomic(status_path, {"status": "reusing-speakers", "progress": 0.97,
                                       "device": diarization_device})
            turns, speaker_count = cached_diarization["turns"], int(cached_diarization["speakerCount"])
            diarization_batches = cached_diarization.get("profile")
            diarization_cache_hit = True
        else:
            diarization_started = time.monotonic()
            turns, speaker_count, diarization_batches = diarize_system_audio(
                diarization_path, normalized, status_path, windows, diarization_device, xpu_runtime)
            diarization_seconds = time.monotonic()-diarization_started
            write_stage_cache(diarization_cache_path, diarization_key, turns=turns,
                              speakerCount=speaker_count, profile=diarization_batches)
        write_atomic(status_path, {"status": "assigning-speakers", "progress": 0.98})
        segments = reconcile(segments, turns)
    else:
        segments = reconcile_without_diarization(segments)
    performance = {"normalizationSeconds": round(normalization_seconds, 3),
                   "modelLoadSeconds": round(model_seconds, 3),
                   "diarizationSeconds": round(diarization_seconds, 3),
                   "diarizationDevice": "xpu" if xpu_available else "cpu",
                   "diarizationBatchSizes": diarization_batches,
                   "cache": {"rawTranscription": raw_cache_hit, "speakerTurns": diarization_cache_hit}}
    if preference == "intel-gpu": performance["openVino"] = openvino_metrics
    transcript = {"schemaVersion": 3, "createdAtUtc": datetime.now(timezone.utc).isoformat(),
                  "language": next(iter(languages)) if len(languages) == 1 else None,
                  "detectedLanguages": detected_languages, "device": device, "computeType": compute_type,
                  "batchSize": batch_size, "computePreference": compute["preference"],
                  "fallbackReason": compute["fallbackReason"],
                  "diarizationRequested": bool(diarization_path_text),
                  "diarizationEnabled": bool(diarization_path_text) and diarization_skipped_reason is None,
                  "diarizationSkippedReason": diarization_skipped_reason, "speakerCount": speaker_count,
                  "modelId": request.get("modelId") or model_path.name,
                  "processingDurationMilliseconds": int((time.monotonic() - started) * 1000),
                  "performance": performance,
                  "segments": segments}
    write_atomic(output_path, transcript)
    write_atomic(status_path, {"status": "completed", "progress": 1.0,
                               "outputPath": str(output_path), "segmentCount": len(transcript["segments"]),
                               "speakerCount": speaker_count})
    return 0

def main() -> int:
    if len(sys.argv) != 2: return 2
    request_path = Path(sys.argv[1]).resolve()
    try: return run(request_path)
    except Exception as exc:
        try:
            request = json.loads(request_path.read_text(encoding="utf-8"))
            write_atomic(Path(request["statusPath"]), {"status": "failed", "progress": 0.0, "error": str(exc)})
        except Exception: pass
        print(str(exc), file=sys.stderr)
        return 1

if __name__ == "__main__": raise SystemExit(main())
