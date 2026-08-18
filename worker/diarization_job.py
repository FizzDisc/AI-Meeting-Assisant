"""Isolated local pyannote job for system audio."""
import json,sys,wave
from datetime import datetime,timezone
from pathlib import Path
def atomic(p,v):
 p.parent.mkdir(parents=True,exist_ok=True);t=p.with_name(f".{p.name}.tmp");t.write_text(json.dumps(v,indent=2),encoding="utf-8");t.replace(p)
def extract(output):
 annotation=getattr(output,"exclusive_speaker_diarization",None) or output.speaker_diarization
 return [{"start":float(turn.start),"end":float(turn.end),"speaker":str(speaker)} for turn,speaker in annotation]
def load_pcm16(path):
 import numpy as np
 import torch
 with wave.open(str(path),"rb") as source:
  if source.getsampwidth()!=2 or source.getcomptype()!="NONE":raise ValueError("Diarization input must be uncompressed PCM16 WAV.")
  channels,rate=source.getnchannels(),source.getframerate();samples=np.frombuffer(source.readframes(source.getnframes()),dtype="<i2").astype("float32")/32768.0
 return {"waveform":torch.from_numpy(samples.reshape(-1,channels).T.copy()),"sample_rate":rate}
def run(request_path):
 r=json.loads(request_path.read_text());audio=Path(r["audioPath"]).resolve();model=Path(r["modelPath"]).resolve();out=Path(r["outputPath"]).resolve();status=Path(r["statusPath"]).resolve()
 if not audio.is_file():raise FileNotFoundError(f"System audio not found: {audio}")
 if not(model/"config.yaml").is_file():raise FileNotFoundError(f"Offline diarization model is invalid: {model}")
 atomic(status,{"status":"loading-model","progress":.2});from pyannote.audio import Pipeline
 pipeline=Pipeline.from_pretrained(model);atomic(status,{"status":"diarizing","progress":.5});items=extract(pipeline(load_pcm16(audio)))
 atomic(out,{"schemaVersion":1,"createdAtUtc":datetime.now(timezone.utc).isoformat(),"source":"system_audio","turns":items});atomic(status,{"status":"completed","progress":1.0,"outputPath":str(out),"speakerCount":len({x['speaker'] for x in items}),"turnCount":len(items)});return 0
if __name__=="__main__":
 try:raise SystemExit(run(Path(sys.argv[1]).resolve()) if len(sys.argv)==2 else 2)
 except Exception as e:print(str(e),file=sys.stderr);raise SystemExit(1)
