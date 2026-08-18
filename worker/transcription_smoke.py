from __future__ import annotations
import json, sys, time
from pathlib import Path
from transcription_jobs import TranscriptionJobManager

if len(sys.argv) not in (3, 4):
    raise SystemExit("Usage: transcription_smoke.py <model-directory> <audio.wav> [second-audio.wav]")
model = Path(sys.argv[1]).resolve()
audio_paths = [Path(item).resolve() for item in sys.argv[2:]]
output = audio_paths[0].parent / "processing" / "transcript.json"
manager = TranscriptionJobManager()
job = manager.start({"audioPaths": [str(audio) for audio in audio_paths],
                     "modelPath": str(model), "outputPath": str(output)})
print(f"Started {job['jobId']}")
while True:
    status = manager.status(job["jobId"])
    print(json.dumps(status, ensure_ascii=False))
    if status["status"] in ("completed", "failed", "cancelled"): break
    time.sleep(1)
if status["status"] != "completed": raise SystemExit(1)
print(output)
