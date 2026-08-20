import json
import tempfile
import unittest
from pathlib import Path

from incremental_finalize_job import run


class IncrementalFinalizeTests(unittest.TestCase):
    def test_finalizes_precomputed_segments_without_retranscribing(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            merged, audio, output, status, request = [root / name for name in
                ("merged.json", "system.wav", "final.json", "status.json", "request.json")]
            merged.write_text(json.dumps({"SchemaVersion": 3, "CreatedAtUtc": "2026-08-20T00:00:00Z",
                "ModelId": "small", "ProcessingDurationMilliseconds": 20,
                "Segments": [{"Start": 0, "End": 1, "Text": "local", "Source": "microphone"},
                             {"Start": 1, "End": 2, "Text": "remote", "Source": "system_audio"}]}), encoding="utf-8")
            audio.write_bytes(b"unused-without-diarization")
            request.write_text(json.dumps({"mergedTranscriptPath": str(merged), "systemAudioPath": str(audio),
                "outputPath": str(output), "statusPath": str(status), "diarizationModelPath": None}), encoding="utf-8")
            self.assertEqual(0, run(request))
            result = json.loads(output.read_text(encoding="utf-8"))
            self.assertTrue(result["incrementalProcessing"]["finalized"])
            self.assertEqual("You", result["segments"][0]["speaker"])
            self.assertEqual("not-run", result["segments"][1]["speakerAssignment"])
            self.assertEqual("completed", json.loads(status.read_text(encoding="utf-8"))["status"])
            self.assertEqual(len(result), len({key.lower() for key in result}))

    def test_silent_system_audio_skips_diarization_model_loading(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            merged, audio, output, status, request = [root / name for name in
                ("merged.json", "system.wav", "final.json", "status.json", "request.json")]
            merged.write_text(json.dumps({"SchemaVersion": 3, "Segments": []}), encoding="utf-8")
            audio.write_bytes(b"not-a-real-wave-because-it-must-not-be-opened")
            request.write_text(json.dumps({"mergedTranscriptPath": str(merged), "systemAudioPath": str(audio),
                "outputPath": str(output), "statusPath": str(status),
                "diarizationModelPath": str(root / "model-that-must-not-load")}), encoding="utf-8")
            self.assertEqual(0, run(request))
            result = json.loads(output.read_text(encoding="utf-8"))
            self.assertFalse(result["diarizationEnabled"])
            self.assertIn("0.00 seconds", result["diarizationSkippedReason"])
            self.assertEqual("completed", json.loads(status.read_text(encoding="utf-8"))["status"])


if __name__ == "__main__":
    unittest.main()
