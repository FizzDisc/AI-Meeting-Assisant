import tempfile
import time
import unittest
import json
import math
import struct
import wave
from pathlib import Path

from transcription_jobs import TranscriptionJobManager
from transcription_job import analyze_pcm16_signal, normalize_requested_language, run


class TranscriptionJobTests(unittest.TestCase):
    def test_audio_preflight_distinguishes_silence_and_signal(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            silent = Path(directory) / "silent.wav"
            signal = Path(directory) / "signal.wav"
            for path, amplitude in ((silent, 0), (signal, 6000)):
                with wave.open(str(path), "wb") as target:
                    target.setnchannels(1)
                    target.setsampwidth(2)
                    target.setframerate(16000)
                    samples = [round(amplitude * math.sin(2 * math.pi * 440 * index / 16000))
                               for index in range(8000)]
                    target.writeframes(b"".join(struct.pack("<h", sample) for sample in samples))
            self.assertFalse(analyze_pcm16_signal(silent)["hasUsableSignal"])
            evidence = analyze_pcm16_signal(signal)
            self.assertTrue(evidence["hasUsableSignal"])
            self.assertGreater(evidence["activeSeconds"], 0.2)

    def test_all_silent_job_completes_without_loading_model(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            audio, model = root / "system_audio_test.wav", root / "model"
            output, status, request = root / "transcript.json", root / "status.json", root / "request.json"
            model.mkdir()
            with wave.open(str(audio), "wb") as target:
                target.setnchannels(1)
                target.setsampwidth(2)
                target.setframerate(16000)
                target.writeframes(bytes(16000 * 2))
            request.write_text(json.dumps({"audioPaths": [str(audio)], "modelPath": str(model),
                "outputPath": str(output), "statusPath": str(status), "computePreference": "automatic",
                "sourceLabels": ["system_audio"]}), encoding="utf-8")
            self.assertEqual(0, run(request))
            transcript = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual([], transcript["segments"])
            self.assertEqual("not-run", transcript["device"])
            self.assertEqual(["system_audio"], transcript["skippedSources"])
            self.assertFalse(transcript["audioEvidence"]["system_audio"]["hasUsableSignal"])

    def test_automatic_language_does_not_force_german(self) -> None:
        self.assertIsNone(normalize_requested_language(None))
        self.assertIsNone(normalize_requested_language("automatic"))
        self.assertIsNone(normalize_requested_language(" AUTO "))
        self.assertEqual("de", normalize_requested_language(" DE "))

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

    def test_incremental_finalizer_runs_as_supervised_job(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            merged, audio, output = root / "merged.json", root / "system.wav", root / "final.json"
            merged.write_text('{"schemaVersion":3,"createdAtUtc":"2026-08-20T00:00:00Z","segments":[]}', encoding="utf-8")
            audio.write_bytes(b"unused-without-diarization")
            manager = TranscriptionJobManager()
            started = manager.start_incremental_finalize({"mergedTranscriptPath": str(merged),
                "systemAudioPath": str(audio), "outputPath": str(output)})
            for _ in range(200):
                status = manager.status(started["jobId"])
                if status["status"] in ("completed", "failed"): break
                time.sleep(0.01)
            self.assertEqual("completed", status["status"], status.get("error"))
            self.assertTrue(output.is_file())


if __name__ == "__main__":
    unittest.main()
