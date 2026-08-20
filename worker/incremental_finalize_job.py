"""Finalize chunk transcripts with one full-meeting speaker analysis."""
from __future__ import annotations
import json, sys, time
from datetime import datetime, timezone
from pathlib import Path
from speaker_reconciliation import reconcile
from transcription_job import diarize_system_audio, normalize_audio, reconcile_without_diarization, write_atomic

def canonicalize_transcript(value: dict) -> dict:
    result = {(key[:1].lower() + key[1:]): item for key, item in value.items()}
    result["segments"] = [
        {(key[:1].lower() + key[1:]): item for key, item in segment.items()}
        for segment in result.get("segments", [])
    ]
    return result

def run(request_path: Path) -> int:
    started = time.monotonic()
    request = json.loads(request_path.read_text(encoding="utf-8"))
    merged_path = Path(request["mergedTranscriptPath"]).resolve()
    system_audio = Path(request["systemAudioPath"]).resolve()
    output_path = Path(request["outputPath"]).resolve()
    status_path = Path(request["statusPath"]).resolve()
    transcript = canonicalize_transcript(json.loads(merged_path.read_text(encoding="utf-8")))
    segments = list(transcript.get("segments") or [])
    diarization_text = str(request.get("diarizationModelPath") or "").strip()
    speaker_count, profile = 0, None
    system_segments = [item for item in segments if item.get("source") == "system_audio"]
    system_speech_seconds = sum(max(0.0, float(item.get("end", 0)) - float(item.get("start", 0))) for item in system_segments)
    meeting_seconds = max((float(item.get("end", 0)) for item in segments), default=0.0)
    skip_reason = (f"System audio contains only {system_speech_seconds:.2f} seconds of transcribed speech."
                   if system_speech_seconds < 2.0 else
                   f"Automatic speaker analysis skips short meetings ({meeting_seconds:.1f} seconds under the 90-second threshold)."
                   if meeting_seconds < 90.0 else None)
    if diarization_text and skip_reason is None:
        normalized = output_path.parent / "normalized_incremental_system_audio.wav"
        write_atomic(status_path, {"status": "normalizing", "progress": .1, "source": "system_audio"})
        normalize_audio(system_audio, normalized)
        xpu_text = str(request.get("torchXpuRuntimePath") or "").strip() if request.get("computePreference") == "intel-gpu" else ""
        xpu_runtime = Path(xpu_text).resolve() if xpu_text else None
        device = "xpu" if xpu_runtime and xpu_runtime.is_dir() else "cpu"
        turns, speaker_count, profile = diarize_system_audio(Path(diarization_text).resolve(),
            [("system_audio", normalized)], status_path, None, device, xpu_runtime)
        write_atomic(status_path, {"status": "assigning-speakers", "progress": .98})
        segments = reconcile(segments, turns)
        transcript["diarizationEnabled"] = True
        transcript["diarizationSkippedReason"] = None
    else:
        if diarization_text:
            write_atomic(status_path, {"status": "skipping-speakers", "progress": .98,
                                       "source": "system_audio", "reason": skip_reason})
        segments = reconcile_without_diarization(segments)
        transcript["diarizationEnabled"] = False
        transcript["diarizationSkippedReason"] = skip_reason or "No local diarization model is installed."
    transcript.update(createdAtUtc=datetime.now(timezone.utc).isoformat(), segments=segments,
                      diarizationRequested=bool(diarization_text), speakerCount=speaker_count)
    transcript["processingDurationMilliseconds"] = int(transcript.get("processingDurationMilliseconds") or 0) + int((time.monotonic()-started)*1000)
    transcript["incrementalProcessing"] = {"finalized": True, "speakerProfile": profile}
    write_atomic(output_path, transcript)
    write_atomic(status_path, {"status": "completed", "progress": 1.0, "outputPath": str(output_path),
                               "segmentCount": len(segments), "speakerCount": speaker_count})
    return 0

if __name__ == "__main__":
    if len(sys.argv) != 2: raise SystemExit(2)
    request_path = Path(sys.argv[1]).resolve()
    try: raise SystemExit(run(request_path))
    except Exception as exc:
        try:
            request = json.loads(request_path.read_text(encoding="utf-8"))
            write_atomic(Path(request["statusPath"]), {"status": "failed", "progress": 0.0, "error": str(exc)})
        except Exception: pass
        print(str(exc), file=sys.stderr)
        raise SystemExit(1)
