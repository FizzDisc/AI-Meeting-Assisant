import tempfile
import time
import unittest
from pathlib import Path

from transcription_jobs import TranscriptionJobManager


class TranscriptionJobTests(unittest.TestCase):
    def test_running_child_process_can_be_cancelled(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            audio, model, output = root / "input.wav", root / "model", root / "processing" / "transcript.json"
            audio.write_bytes(b"not-real-audio")
            model.mkdir()
            fake_job = root / "slow_job.py"
            fake_job.write_text("import time\ntime.sleep(30)\n", encoding="utf-8")
            manager = TranscriptionJobManager(fake_job)
            started = manager.start({"audioPaths": [str(audio)], "modelPath": str(model), "outputPath": str(output)})
            cancelled = manager.cancel(started["jobId"])
            self.assertEqual("cancelled", cancelled["status"])
            self.assertEqual("cancelled", manager.status(started["jobId"])["status"])

    def test_invalid_input_does_not_start_a_process(self) -> None:
        manager = TranscriptionJobManager()
        with self.assertRaises(FileNotFoundError):
            manager.start({"audioPaths": ["missing.wav"], "modelPath": "missing", "outputPath": "out.json"})

    def test_source_labels_must_match_inputs(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            audio, model = root / "input.wav", root / "model"
            audio.write_bytes(b"not-real-audio")
            model.mkdir()
            manager = TranscriptionJobManager()
            with self.assertRaisesRegex(ValueError, "sourceLabels must match"):
                manager.start({"audioPaths": [str(audio)], "modelPath": str(model),
                               "outputPath": str(root / "out.json"), "sourceLabels": []})
            with self.assertRaisesRegex(ValueError, "unsupported source"):
                manager.start({"audioPaths": [str(audio)], "modelPath": str(model),
                               "outputPath": str(root / "out.json"), "sourceLabels": ["unknown"]})

    def test_failed_child_persists_exit_code_and_diagnostic_log(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            audio, model, output = root / "input.wav", root / "model", root / "processing" / "transcript.json"
            audio.write_bytes(b"not-real-audio")
            model.mkdir()
            fake_job = root / "failed_job.py"
            fake_job.write_text("import sys\nprint('durable diagnostic', file=sys.stderr)\nraise SystemExit(7)\n", encoding="utf-8")
            manager = TranscriptionJobManager(fake_job)
            started = manager.start({"audioPaths": [str(audio)], "modelPath": str(model), "outputPath": str(output)})
            for _ in range(100):
                status = manager.status(started["jobId"])
                if status["status"] == "failed": break
                time.sleep(0.01)
            self.assertEqual(7, status["exitCode"])
            self.assertIn("durable diagnostic", status["error"])
            self.assertTrue(Path(status["diagnosticLogPath"]).is_file())


if __name__ == "__main__":
    unittest.main()
