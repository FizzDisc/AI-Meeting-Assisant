"""Isolated WhisperX job; process isolation makes native inference cancellable."""
from __future__ import annotations
import json, subprocess, sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from hardware import select_compute

def write_atomic(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f".{path.name}.tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    temporary.replace(path)

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

def run(request_path: Path) -> int:
    request = json.loads(request_path.read_text(encoding="utf-8"))
    inputs = [Path(item).resolve() for item in request["audioPaths"]]
    model_path, output_path = Path(request["modelPath"]).resolve(), Path(request["outputPath"]).resolve()
    status_path = Path(request["statusPath"]).resolve()
    write_atomic(status_path, {"status": "normalizing", "progress": 0.1})
    normalized = []
    for index, source in enumerate(inputs):
        label = source_name(source, index)
        normalized_path = output_path.parent / f"normalized_{label}.wav"
        normalize_audio(source, normalized_path)
        normalized.append((label, normalized_path))
    write_atomic(status_path, {"status": "loading-model", "progress": 0.25})
    import whisperx
    import torch
    compute = select_compute(torch, request.get("computePreference", "automatic"))
    device, compute_type, batch_size = compute["mode"], compute["computeType"], compute["batchSize"]
    model = whisperx.load_model(str(model_path), device, compute_type=compute_type, language=request.get("language"))
    results = []
    for index, (label, normalized_path) in enumerate(normalized):
        write_atomic(status_path, {"status": "transcribing", "progress": 0.4 + 0.5 * index / len(normalized),
                                   "source": label, "device": device, "computeType": compute_type,
                                   "batchSize": batch_size, "fallbackReason": compute["fallbackReason"]})
        audio = whisperx.load_audio(str(normalized_path))
        results.append((label, model.transcribe(audio, batch_size=batch_size)))
    detected_languages = {source: result.get("language") for source, result in results}
    languages = {language for language in detected_languages.values() if language}
    transcript = {"schemaVersion": 2, "createdAtUtc": datetime.now(timezone.utc).isoformat(),
                  "language": next(iter(languages)) if len(languages) == 1 else None,
                  "detectedLanguages": detected_languages, "device": device, "computeType": compute_type,
                  "batchSize": batch_size, "computePreference": compute["preference"],
                  "fallbackReason": compute["fallbackReason"],
                  "segments": merge_segments(results)}
    write_atomic(output_path, transcript)
    write_atomic(status_path, {"status": "completed", "progress": 1.0,
                               "outputPath": str(output_path), "segmentCount": len(transcript["segments"])})
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
