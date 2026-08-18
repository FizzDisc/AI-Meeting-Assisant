"""Native OpenVINO GenAI transcription used by the supervised job process."""
from __future__ import annotations
import sys, wave
from pathlib import Path
from typing import Any, Callable

Progress = Callable[[int, int, float, float], None]


def activate_runtime(runtime_path: Path) -> None:
    if not runtime_path.is_dir():
        raise FileNotFoundError(f"OpenVINO runtime not found: {runtime_path}")
    value = str(runtime_path)
    if value not in sys.path: sys.path.insert(0, value)


def load_pcm16(path: Path):
    import numpy as np
    with wave.open(str(path), "rb") as source:
        if source.getnchannels() != 1 or source.getsampwidth() != 2 or source.getframerate() != 16000:
            raise ValueError("OpenVINO input must be mono PCM16 at 16 kHz.")
        frames = source.readframes(source.getnframes())
    audio = np.frombuffer(frames, dtype=np.int16).astype(np.float32) / 32768.0
    return audio, len(audio) / 16000.0


def detect_speech_windows(audio, model_path: Path, chunk_seconds: float = 30.0):
    from whisperx.vads import Pyannote
    if not model_path.is_file():
        raise FileNotFoundError(f"Local VAD model not found: {model_path}")
    vad = Pyannote("cpu", model_fp=str(model_path), vad_onset=0.5)
    scores = vad({"waveform": vad.preprocess_audio(audio), "sample_rate": 16000})
    return vad.merge_chunks(scores, chunk_seconds, onset=0.5, offset=0.363)


def reconcile_segments(segments: list[dict[str, Any]]) -> list[dict[str, Any]]:
    ordered = sorted(segments, key=lambda item: (float(item["start"]), float(item["end"])))
    result: list[dict[str, Any]] = []
    for segment in ordered:
        item = dict(segment)
        if result and item["start"] < result[-1]["end"]:
            if item["text"] == result[-1]["text"] and item["end"] <= result[-1]["end"] + 0.25:
                continue
            result[-1]["end"] = max(result[-1]["start"], item["start"])
        result.append(item)
    return result


class OpenVinoTranscriber:
    def __init__(self, runtime_path: Path, model_path: Path, vad_model_path: Path,
                 language: str | None = None) -> None:
        activate_runtime(runtime_path)
        import openvino as ov
        if "GPU" not in ov.Core().available_devices:
            raise RuntimeError("Intel GPU was requested but OpenVINO does not report a GPU device.")
        if not model_path.is_dir():
            raise FileNotFoundError(f"OpenVINO speech model not found: {model_path}")
        import openvino_genai as genai
        self._pipeline = genai.WhisperPipeline(str(model_path), "GPU")
        self._config = self._pipeline.get_generation_config()
        self._config.task = "transcribe"
        self._config.return_timestamps = True
        if language: self._config.language = f"<|{language}|>"
        self._vad_model_path = vad_model_path

    def transcribe(self, path: Path, progress: Progress) -> dict[str, Any]:
        audio, duration = load_pcm16(path)
        windows = detect_speech_windows(audio, self._vad_model_path)
        segments: list[dict[str, Any]] = []
        for index, window in enumerate(windows, 1):
            start, end = float(window["start"]), float(window["end"])
            progress(index, len(windows), start, end)
            samples = audio[round(start * 16000):round(end * 16000)]
            generated = self._pipeline.generate(samples, self._config)
            segments.extend({"start": start + float(chunk.start_ts),
                             "end": min(start + float(chunk.end_ts), duration),
                             "text": chunk.text.strip()}
                            for chunk in (generated.chunks or []) if chunk.text.strip())
        return {"language": None, "segments": reconcile_segments(segments),
                "speechWindowCount": len(windows)}
