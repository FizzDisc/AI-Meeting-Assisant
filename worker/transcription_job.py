"""Isolated WhisperX job; process isolation makes native inference cancellable."""
from __future__ import annotations
import json, subprocess, sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

def write_atomic(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f".{path.name}.tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    temporary.replace(path)

def normalize_audio(inputs: list[Path], output: Path) -> None:
    command = ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y"]
    for source in inputs: command.extend(["-i", str(source)])
    if len(inputs) == 1:
        command.extend(["-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", str(output)])
    else:
        command.extend(["-filter_complex", f"amix=inputs={len(inputs)}:duration=longest:normalize=1",
                        "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", str(output)])
    subprocess.run(command, check=True, capture_output=True, text=True)

def run(request_path: Path) -> int:
    request = json.loads(request_path.read_text(encoding="utf-8"))
    inputs = [Path(item).resolve() for item in request["audioPaths"]]
    model_path, output_path = Path(request["modelPath"]).resolve(), Path(request["outputPath"]).resolve()
    normalized_path, status_path = output_path.parent / "normalized.wav", Path(request["statusPath"]).resolve()
    write_atomic(status_path, {"status": "normalizing", "progress": 0.1})
    normalize_audio(inputs, normalized_path)
    write_atomic(status_path, {"status": "loading-model", "progress": 0.25})
    import whisperx
    import torch
    device = "cuda" if torch.cuda.is_available() else "cpu"
    compute_type = "float16" if device == "cuda" else "int8"
    model = whisperx.load_model(str(model_path), device, compute_type=compute_type, language=request.get("language"))
    audio = whisperx.load_audio(str(normalized_path))
    write_atomic(status_path, {"status": "transcribing", "progress": 0.4,
                               "device": device, "computeType": compute_type})
    result = model.transcribe(audio, batch_size=8 if device == "cuda" else 2)
    transcript = {"schemaVersion": 1, "createdAtUtc": datetime.now(timezone.utc).isoformat(),
                  "language": result.get("language"), "device": device, "computeType": compute_type,
                  "segments": [{"start": float(s["start"]), "end": float(s["end"]),
                                "text": str(s["text"]).strip()} for s in result.get("segments", [])]}
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
