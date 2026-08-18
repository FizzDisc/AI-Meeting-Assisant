import tempfile
import unittest
import wave
from pathlib import Path
from openvino_spike import chunk_windows, directory_size, load_pcm16, with_heartbeat, write_report


class OpenVinoSpikeTests(unittest.TestCase):
    def test_pcm16_stereo_is_downmixed_and_resampled(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "input.wav"
            with wave.open(str(path), "wb") as output:
                output.setnchannels(2)
                output.setsampwidth(2)
                output.setframerate(8000)
                output.writeframes((b"\x00\x10\x00\x10") * 800)
            audio, duration = load_pcm16(path)
            self.assertAlmostEqual(0.1, duration, places=3)
            self.assertEqual(1600, len(audio))

    def test_directory_size_counts_nested_files(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "nested").mkdir()
            (root / "a.bin").write_bytes(b"123")
            (root / "nested" / "b.bin").write_bytes(b"4567")
            self.assertEqual(7, directory_size(root))

    def test_heartbeat_returns_action_result(self):
        self.assertEqual("done", with_heartbeat("GPU", lambda: "done"))

    def test_chunk_windows_overlap_without_duplicate_ownership(self):
        windows = chunk_windows(65.0)
        self.assertEqual([(0.0, 30.0, 0.0, 29.0),
                          (28.0, 58.0, 29.0, 57.0),
                          (56.0, 65.0, 57.0, 65.0)], windows)

    def test_report_is_written_as_json(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "report.json"
            write_report(path, {"status": "running"})
            self.assertEqual('{\n  "status": "running"\n}', path.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
