import json
import shutil
import tempfile
import time
import unittest
from pathlib import Path
from unittest.mock import Mock, patch
import live_transcription
from live_transcription import LiveProcess
from transcription_jobs import TranscriptionJobManager
from transcription_job import run


class LiveTranscriptionTests(unittest.TestCase):
    def test_model_loaded_once_for_distinct_batches_and_language_change_reloads(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            audio = root / "audio.wav"
            audio.write_bytes(b"test")
            model = Mock()
            model.transcribe.return_value = {"segments": [], "language": "de"}
            whisper = Mock()
            whisper.load_model.return_value = model
            cache = {}
            compute = {"mode": "cpu", "computeType": "int8", "batchSize": 2,
                       "preference": "cpu-only", "fallbackReason": None}
            for index, language in enumerate((None, None, "en")):
                output = root / str(index) / "out.json"
                request = root / "request.json"
                request.write_text(json.dumps({"audioPaths": [str(audio)], "modelPath": str(root),
                    "outputPath": str(output), "statusPath": str(root / "status.json"),
                    "computePreference": "cpu-only", "language": language}))
                with patch.dict("sys.modules", {"torch": Mock(), "whisperx": whisper}), \
                     patch("transcription_job.select_compute", return_value=compute), \
                     patch("transcription_job.normalize_audio"), \
                     patch("transcription_job.analyze_pcm16_signal", return_value={"hasUsableSignal": True}):
                    self.assertEqual(0, run(request, cache))
                performance = json.loads(output.read_text())["performance"]
                self.assertEqual(index == 1, performance["modelReused"])
                if index == 1:
                    self.assertEqual(0, performance["modelLoadSeconds"])
            self.assertEqual(2, whisper.load_model.call_count)
            self.assertEqual(3, model.transcribe.call_count)

    def test_live_lifecycle_and_historical_status(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            server = root / "live_transcription.py"
            shutil.copyfile(live_transcription.__file__, server)
            # Real IPC/process supervision; only the expensive model runner is replaced.
            (root / "transcription_job.py").write_text(
                "import json,time\nfrom pathlib import Path\n"
                "def write_atomic(path,value):\n path.write_text(json.dumps(value))\n"
                "def run(path,model_cache=None):\n"
                " r=json.loads(path.read_text())\n"
                " if r.get('language')=='slow': time.sleep(30)\n"
                " if r.get('language')=='fail': raise ValueError('test failure')\n"
                " write_atomic(Path(r['statusPath']),{'status':'completed','progress':1})\n")
            audio = root / "audio.wav"
            audio.write_bytes(b"test")
            manager = TranscriptionJobManager()
            manager._live = LiveProcess(idle_seconds=.3, server_script=server)
            payload = {"audioPaths": [str(audio)], "modelPath": str(root),
                       "outputPath": str(root / "out.json"), "lowPriority": True}

            def start(language=None):
                return manager.start({**payload, "language": language})["jobId"]

            def finish(job):
                deadline = time.monotonic() + 5
                while time.monotonic() < deadline:
                    status = manager.status(job)
                    if status["status"] in ("completed", "failed", "cancelled"):
                        return status
                    time.sleep(.01)
                self.fail("Job did not finish")

            try:
                first = start()
                process = manager._live.process
                self.assertEqual("completed", finish(first)["status"])
                second = start()
                self.assertIs(process, manager._live.process)
                self.assertEqual("completed", finish(second)["status"])
                changed = start("en")
                self.assertIsNot(process, manager._live.process)
                self.assertIsNotNone(process.poll())
                self.assertEqual("completed", finish(changed)["status"])
                process = manager._live.process
                process.wait(timeout=5)  # Automatic idle eviction.
                restarted = start("slow")
                process = manager._live.process
                self.assertEqual("completed", manager.cancel(first)["status"])
                self.assertIsNone(process.poll(), "Old job cancellation killed a new job")
                self.assertEqual("cancelled", manager.cancel(restarted)["status"])
                self.assertIsNotNone(process.poll())
                failed = start("fail")
                self.assertEqual("failed", finish(failed)["status"])
                recovered = start()
                self.assertEqual("completed", finish(recovered)["status"])
                process = manager._live.process
                merged = root / "merged.json"
                merged.write_text('{"schemaVersion":3,"segments":[]}')
                final = manager.start_incremental_finalize({"mergedTranscriptPath": str(merged),
                    "systemAudioPath": str(audio), "outputPath": str(root / "final.json")})
                self.assertIsNotNone(process.poll(), "Finalization retained the live model")
                self.assertEqual("completed", finish(final["jobId"])["status"])
            finally:
                manager.shutdown()
