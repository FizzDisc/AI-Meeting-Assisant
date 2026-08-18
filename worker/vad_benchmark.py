"""Compare local VAD implementations without running speech recognition."""
from __future__ import annotations
import argparse, json, time
from pathlib import Path
from openvino_backend import load_pcm16


def run(audio_path: Path, pyannote_model: Path) -> dict:
    audio, duration = load_pcm16(audio_path)
    results = []
    from whisperx.vads import Pyannote, Silero
    definitions = [
        ("pyannote", lambda: Pyannote("cpu", model_fp=str(pyannote_model), vad_onset=0.5)),
        ("silero", lambda: Silero(vad_onset=0.5, chunk_size=30)),
    ]
    for name, factory in definitions:
        started = time.perf_counter()
        vad = factory()
        loaded = time.perf_counter()
        prepared = vad.preprocess_audio(audio)
        scores = vad({"waveform": prepared, "sample_rate": 16000})
        windows = vad.merge_chunks(scores, 30, onset=0.5, offset=0.363)
        finished = time.perf_counter()
        results.append({"name": name, "loadSeconds": round(loaded-started, 3),
                        "detectSeconds": round(finished-loaded, 3),
                        "windows": len(windows),
                        "speechSeconds": round(sum(float(w["end"])-float(w["start"]) for w in windows), 3),
                        "ranges": [{"start": round(float(w["start"]), 3), "end": round(float(w["end"]), 3)} for w in windows]})
    return {"audio": str(audio_path.resolve()), "durationSeconds": round(duration, 3), "results": results}


def main() -> int:
    parser=argparse.ArgumentParser();parser.add_argument("audio",type=Path);parser.add_argument("--output",type=Path)
    args=parser.parse_args();vad_model=Path(__file__).resolve().parent/".venv/Lib/site-packages/whisperx/assets/pytorch_model.bin"
    report=run(args.audio.resolve(),vad_model)
    text=json.dumps(report,ensure_ascii=False,indent=2)
    if args.output: args.output.resolve().write_text(text,encoding="utf-8")
    print(text);return 0


if __name__ == "__main__": raise SystemExit(main())
