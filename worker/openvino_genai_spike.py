"""Benchmark Intel's native OpenVINO GenAI Whisper pipeline."""
from __future__ import annotations
import argparse, json, sys, time
from datetime import datetime, timezone
from pathlib import Path

from openvino_spike import add_spike_packages, available_devices, directory_size, load_pcm16, with_heartbeat, write_report

def detect_speech_windows(audio, method: str, chunk_seconds: float = 30.0):
    from openvino_backend import detect_speech_windows as detect, load_vad
    model_path = Path(__file__).resolve().parent / ".venv/Lib/site-packages/whisperx/assets/pytorch_model.bin"
    import torch
    silero = Path(torch.hub.get_dir()) / "snakers4_silero-vad_master" if method == "silero" else None
    return detect(audio, load_vad(method, model_path, silero), chunk_seconds)

def run_once(model_path: Path, audio, duration: float, device: str, language: str | None, speech_windows=None):
    import openvino_genai as genai
    started = time.perf_counter()
    pipeline = genai.WhisperPipeline(str(model_path), device)
    loaded = time.perf_counter()
    config = pipeline.get_generation_config()
    config.task = "transcribe"
    config.return_timestamps = True
    if language: config.language = f"<|{language}|>"
    inputs = speech_windows if speech_windows is not None else [{"start": 0.0, "end": duration}]
    chunks, texts = [], []
    for index, window in enumerate(inputs, 1):
        start, end = float(window["start"]), float(window["end"])
        print(f"{device} speech window {index}/{len(inputs)} - {start:0.1f}s to {end:0.1f}s", flush=True)
        samples = audio[round(start * 16000):round(end * 16000)]
        result = with_heartbeat(device, lambda: pipeline.generate(samples, config))
        if result.texts and result.texts[0].strip(): texts.append(result.texts[0].strip())
        for item in result.chunks or []:
            absolute_start = start + float(item.start_ts)
            absolute_end = min(start + float(item.end_ts), end, duration)
            if item.text.strip() and absolute_start < end and absolute_end > absolute_start:
                chunks.append({"start": absolute_start, "end": absolute_end, "text": item.text.strip()})
    finished = time.perf_counter()
    inference = finished - loaded
    from openvino_backend import reconcile_segments
    chunks = reconcile_segments(chunks)
    return {"device": device, "loadAndCompileSeconds": round(loaded - started, 3),
            "inferenceSeconds": round(inference, 3),
            "realTimeFactor": round(inference / duration, 4) if duration else None,
            "text": " ".join(item["text"] for item in chunks), "chunks": chunks,
            "speechWindowCount": len(inputs),
            "speechSeconds": round(sum(float(item["end"]) - float(item["start"]) for item in inputs), 3)}

def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("audio", type=Path)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--devices", nargs="+", default=["CPU", "GPU"])
    parser.add_argument("--language", default="de")
    parser.add_argument("--vad", action="store_true")
    parser.add_argument("--vad-method", choices=["pyannote", "silero"], default="pyannote")
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
    vad_started = time.perf_counter()
    speech_windows = detect_speech_windows(audio, args.vad_method) if args.vad else None
    vad_seconds = time.perf_counter() - vad_started
    output = args.output.resolve() if args.output else Path("artifacts/benchmarks/openvino-genai-spike.json").resolve()
    report = {"schemaVersion": 1, "engine": "openvino-genai", "status": "running",
              "createdAtUtc": datetime.now(timezone.utc).isoformat(), "audioPath": str(audio_path),
              "audioDurationSeconds": round(duration, 3), "modelPath": str(model_path),
              "modelBytes": directory_size(model_path), "runtimeBytes": directory_size(packages),
              "availableDevices": detected, "vadEnabled": args.vad,
              "vadMethod": args.vad_method if args.vad else None,
              "vadSeconds": round(vad_seconds, 3), "detectedSpeechWindows": len(speech_windows or []),
              "results": []}
    write_report(output, report)
    try:
        for device in args.devices:
            print(f"OpenVINO GenAI {device} run starting...", flush=True)
            report["currentDevice"] = device
            write_report(output, report)
            report["results"].append(run_once(model_path, audio, duration, device, args.language, speech_windows))
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
    summary = {
        "status": report["status"],
        "output": str(output),
        "audioDurationSeconds": report["audioDurationSeconds"],
        "vadEnabled": report["vadEnabled"],
        "vadSeconds": report["vadSeconds"],
        "results": [
            {
                "device": result["device"],
                "loadAndCompileSeconds": result["loadAndCompileSeconds"],
                "inferenceSeconds": result["inferenceSeconds"],
                "realTimeFactor": result["realTimeFactor"],
                "speechWindowCount": result["speechWindowCount"],
                "speechSeconds": result["speechSeconds"],
                "segmentCount": len(result["chunks"]),
                "textCharacters": len(result["text"]),
            }
            for result in report["results"]
        ],
    }
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0

if __name__ == "__main__": raise SystemExit(main())
