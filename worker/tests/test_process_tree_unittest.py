import tempfile
import time
import unittest
from pathlib import Path
from transcription_jobs import TranscriptionJobManager


class ProcessTreeTests(unittest.TestCase):
    def test_cancel_and_shutdown_stop_descendant_work(self):
        for shutdown in (False, True):
            with self.subTest(shutdown=shutdown), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                child = root / "child.py"
                heartbeat = root / "heartbeat"
                child.write_text("import pathlib,sys,time\np=pathlib.Path(sys.argv[1])\n"
                                 "while True:\n p.write_text(str(time.time_ns()))\n time.sleep(.05)\n")
                parent = root / "parent.py"
                parent.write_text("import subprocess,sys,time\n"
                                  f"subprocess.Popen([sys.executable, {str(child)!r}, {str(heartbeat)!r}])\n"
                                  "time.sleep(30)\n")
                audio = root / "input.wav"
                audio.write_bytes(b"test")
                manager = TranscriptionJobManager(parent)
                try:
                    job = manager.start({"audioPaths": [str(audio)], "modelPath": str(root),
                                         "outputPath": str(root / "out.json")})
                    deadline = time.monotonic() + 5
                    while not heartbeat.exists() and time.monotonic() < deadline:
                        time.sleep(.02)
                    self.assertTrue(heartbeat.exists(), "Descendant did not start")
                    if shutdown:
                        manager.shutdown()
                    else:
                        manager.cancel(job["jobId"])
                    self.assertEqual("cancelled", manager.status(job["jobId"])["status"])
                    time.sleep(.2)
                    stopped = heartbeat.read_text()
                    time.sleep(.3)
                    self.assertEqual(stopped, heartbeat.read_text(), "Descendant is still running")
                finally:
                    manager.shutdown()
