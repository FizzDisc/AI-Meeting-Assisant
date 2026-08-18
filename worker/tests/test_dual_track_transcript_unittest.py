import unittest
from pathlib import Path
from transcription_job import merge_segments, source_name

class DualTrackTranscriptTests(unittest.TestCase):
    def test_capture_filenames_map_to_stable_sources(self):
        self.assertEqual("microphone", source_name(Path("microphone_123.wav"), 0))
        self.assertEqual("system_audio", source_name(Path("system_audio_123.wav"), 1))

    def test_segments_are_merged_without_dropping_either_source(self):
        merged = merge_segments([
            ("microphone", {"segments": [{"start": 1.0, "end": 2.0, "text": "own voice"}]}),
            ("system_audio", {"segments": [{"start": 0.5, "end": 1.5, "text": "remote voice"}]})])
        self.assertEqual(["system_audio", "microphone"], [item["source"] for item in merged])
        self.assertEqual(["remote voice", "own voice"], [item["text"] for item in merged])

if __name__ == "__main__": unittest.main()
