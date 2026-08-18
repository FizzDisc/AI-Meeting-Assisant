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


if __name__ == "__main__":
    unittest.main()
