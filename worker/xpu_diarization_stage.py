"""Isolated Intel XPU speaker-diarization stage.

Keeping PyTorch XPU out of the OpenVINO Whisper process prevents the two
runtimes from competing for the same integrated GPU context.
"""
from __future__ import annotations
import json, sys
from pathlib import Path

from transcription_job import (activate_torch_xpu, compress_speech_audio,
                               configure_diarization_profile,
                               restore_turn_timestamps, write_atomic)

def run(request_path: Path) -> int:
    request = json.loads(request_path.read_text(encoding="utf-8"))
    activate_torch_xpu(Path(request["runtimePath"]).resolve())
    import torch
    if not torch.xpu.is_available():
        raise RuntimeError("The isolated PyTorch runtime does not report an Intel XPU device.")
    torch.set_num_threads(12)
    torch.set_num_interop_threads(4)
    from diarization_job import extract, load_pcm16
    from pyannote.audio import Pipeline
    pipeline = Pipeline.from_pretrained(Path(request["modelPath"]).resolve())
    pipeline.to(torch.device("xpu"))
    profile = configure_diarization_profile(pipeline, "xpu")
    audio = load_pcm16(Path(request["audioPath"]).resolve())
    mapping = None
    windows = request.get("speechWindows")
    if windows:
        audio, mapping = compress_speech_audio(audio, windows)
    turns = extract(pipeline(audio))
    if mapping is not None:
        turns = restore_turn_timestamps(turns, mapping)
    write_atomic(Path(request["resultPath"]), {"turns": turns,
                                               "speakerCount": len({turn["speaker"] for turn in turns}),
                                               "profile": profile})
    return 0

def main() -> int:
    if len(sys.argv) != 2: return 2
    try: return run(Path(sys.argv[1]).resolve())
    except Exception as exc:
        print(str(exc), file=sys.stderr)
        return 1

if __name__ == "__main__": raise SystemExit(main())
