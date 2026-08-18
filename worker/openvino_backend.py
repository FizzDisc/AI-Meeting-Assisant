"""Native OpenVINO GenAI transcription used by the supervised job process."""
from __future__ import annotations
import sys, time, wave
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


def load_vad(method: str, pyannote_model_path: Path, silero_repository: Path | None = None):
    if method == "pyannote":
        from whisperx.vads import Pyannote
        if not pyannote_model_path.is_file():
            raise FileNotFoundError(f"Local VAD model not found: {pyannote_model_path}")
        return Pyannote("cpu", model_fp=str(pyannote_model_path), vad_onset=0.5)
    if method == "silero":
        import torch
        repository = silero_repository
        if repository is None:
            raise FileNotFoundError("A local Silero VAD repository is required.")
        if not repository.is_dir():
            raise FileNotFoundError(f"Local Silero VAD is not installed: {repository}")
        model, utilities = torch.hub.load(str(repository), "silero_vad", source="local", onnx=False)
        return LocalSileroVad(model, utilities[0])
    raise ValueError(f"Unknown VAD method: {method}")


def detect_speech_windows(audio, vad, chunk_seconds: float = 30.0):
    if isinstance(vad, LocalSileroVad): return vad.detect(audio, chunk_seconds)
    scores = vad({"waveform": vad.preprocess_audio(audio), "sample_rate": 16000})
    return vad.merge_chunks(scores, chunk_seconds, onset=0.5, offset=0.363)


class LocalSileroVad:
    def __init__(self, model, get_speech_timestamps) -> None:
        self._model, self._get_speech_timestamps = model, get_speech_timestamps

    def detect(self, audio, chunk_seconds: float) -> list[dict[str, float]]:
        import torch
        timestamps = self._get_speech_timestamps(torch.from_numpy(audio), model=self._model,
                                                 sampling_rate=16000,
                                                 max_speech_duration_s=chunk_seconds, threshold=0.5)
        segments = [(item["start"] / 16000.0, item["end"] / 16000.0) for item in timestamps]
        if not segments: return []
        windows, current_start, current_end = [], segments[0][0], 0.0
        for start, end in segments:
            if end-current_start > chunk_seconds and current_end-current_start > 0:
                windows.append({"start": current_start, "end": current_end})
                current_start = start
            current_end = end
        windows.append({"start": current_start, "end": current_end})
        return windows


def reconcile_segments(segments: list[dict[str, Any]]) -> list[dict[str, Any]]:
    ordered = sorted(segments, key=lambda item: (float(item["start"]), float(item["end"])))
    result: list[dict[str, Any]] = []
    repeated = 0
    for segment in ordered:
        item = dict(segment)
        repeated = repeated + 1 if result and item["text"] == result[-1]["text"] else 1
        if repeated > 2: continue
        if result and item["start"] < result[-1]["end"]:
            if item["text"] == result[-1]["text"] and item["end"] <= result[-1]["end"] + 0.25:
                continue
            result[-1]["end"] = max(result[-1]["start"], item["start"])
        result.append(item)
    return result


class OpenVinoTranscriber:
    def __init__(self, runtime_path: Path, model_path: Path, vad_model_path: Path,
                 language: str | None = None, vad_method: str = "silero",
                 silero_repository: Path | None = None) -> None:
        started = time.perf_counter()
        activate_runtime(runtime_path)
        import openvino as ov
        if "GPU" not in ov.Core().available_devices:
            raise RuntimeError("Intel GPU was requested but OpenVINO does not report a GPU device.")
        if not model_path.is_dir():
            raise FileNotFoundError(f"OpenVINO speech model not found: {model_path}")
        import openvino_genai as genai
        cache_path = model_path.parent / ".openvino-cache" / model_path.name
        cache_path.mkdir(parents=True, exist_ok=True)
        self._pipeline = genai.WhisperPipeline(str(model_path), "GPU", CACHE_DIR=str(cache_path))
        self._config = self._pipeline.get_generation_config()
        self._config.task = "transcribe"
        self._config.return_timestamps = True
        if language: self._config.language = f"<|{language}|>"
        compiled = time.perf_counter()
        self._vad = load_vad(vad_method, vad_model_path, silero_repository)
        self.metrics = {"compileSeconds": round(compiled-started, 3),
                        "vadLoadSeconds": round(time.perf_counter()-compiled, 3), "tracks": []}

    def transcribe(self, path: Path, progress: Progress) -> dict[str, Any]:
        audio, duration = load_pcm16(path)
        vad_started = time.perf_counter()
        windows = detect_speech_windows(audio, self._vad)
        vad_seconds = time.perf_counter()-vad_started
        inference_started = time.perf_counter()
        segments: list[dict[str, Any]] = []
        for index, window in enumerate(windows, 1):
            start, end = float(window["start"]), float(window["end"])
            progress(index, len(windows), start, end)
            samples = audio[round(start * 16000):round(end * 16000)]
            generated = self._pipeline.generate(samples, self._config)
            for chunk in generated.chunks or []:
                absolute_start = start + float(chunk.start_ts)
                absolute_end = min(start + float(chunk.end_ts), end, duration)
                if chunk.text.strip() and absolute_start < end and absolute_end > absolute_start:
                    segments.append({"start": absolute_start, "end": absolute_end,
                                     "text": chunk.text.strip()})
        inference_seconds = time.perf_counter()-inference_started
        track_metrics = {"vadSeconds": round(vad_seconds, 3),
                         "inferenceSeconds": round(inference_seconds, 3),
                         "speechWindowCount": len(windows),
                         "speechSeconds": round(sum(float(w["end"])-float(w["start"]) for w in windows), 3)}
        self.metrics["tracks"].append(track_metrics)
        return {"language": None, "segments": reconcile_segments(segments), **track_metrics,
                "speechWindows": [{"start": float(w["start"]), "end": float(w["end"])} for w in windows]}
