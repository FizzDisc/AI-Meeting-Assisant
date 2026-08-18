"""Benchmark speech-only Pyannote diarization with explicit CPU threads."""
from __future__ import annotations
import argparse, json, os, sys, time
from pathlib import Path


def main() -> int:
    parser=argparse.ArgumentParser();parser.add_argument("audio",type=Path);parser.add_argument("--model",type=Path,required=True)
    parser.add_argument("--threads",type=int,default=12);parser.add_argument("--device",choices=["cpu","xpu"],default="cpu")
    parser.add_argument("--xpu-runtime",type=Path);parser.add_argument("--output",type=Path,required=True);args=parser.parse_args()
    dll_handles=[]
    if args.device=="xpu":
        root=args.xpu_runtime.resolve() if args.xpu_runtime else Path(__file__).resolve().parent/".torch-xpu-spike"
        dll_handles=[os.add_dll_directory(str(root/path)) for path in (Path("Library/bin"),Path("bin"),Path("torch/lib"))]
        sys.path.insert(0,str(root))
    import torch
    torch.set_num_threads(args.threads);torch.set_num_interop_threads(max(1,min(args.threads,4)))
    from diarization_job import extract, load_pcm16
    from openvino_backend import detect_speech_windows, load_vad
    from transcription_job import compress_speech_audio
    audio=load_pcm16(args.audio.resolve());waveform=audio["waveform"].squeeze(0).numpy()
    silero=Path(torch.hub.get_dir())/"snakers4_silero-vad_master"
    vad_started=time.perf_counter();vad=load_vad("silero",Path("unused"),silero);windows=detect_speech_windows(waveform,vad);vad_seconds=time.perf_counter()-vad_started
    compressed,mapping=compress_speech_audio(audio,windows)
    from pyannote.audio import Pipeline
    load_started=time.perf_counter();pipeline=Pipeline.from_pretrained(args.model.resolve());pipeline.to(torch.device(args.device));load_seconds=time.perf_counter()-load_started
    inference_started=time.perf_counter();turns=extract(pipeline(compressed));inference_seconds=time.perf_counter()-inference_started
    report={"audio":str(args.audio.resolve()),"threads":args.threads,"device":args.device,"audioSeconds":round(audio["waveform"].shape[1]/audio["sample_rate"],3),
            "speechSeconds":round(sum(w["end"]-w["start"] for w in windows),3),"windows":len(windows),
            "vadSeconds":round(vad_seconds,3),"modelLoadSeconds":round(load_seconds,3),"diarizationSeconds":round(inference_seconds,3),
            "turns":len(turns),"speakers":len({t["speaker"] for t in turns})}
    args.output.resolve().write_text(json.dumps(report,indent=2),encoding="utf-8");print(json.dumps(report,indent=2));return 0


if __name__=="__main__":raise SystemExit(main())
