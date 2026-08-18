import unittest
from pathlib import Path
from transcription_job import merge_segments, restore_turn_timestamps, source_name
from openvino_backend import reconcile_segments

class DualTrackTranscriptTests(unittest.TestCase):
    def test_openvino_boundary_reconciliation_sorts_and_clamps(self):
        result = reconcile_segments([
            {"start": 5.0, "end": 8.0, "text": "second"},
            {"start": 1.0, "end": 6.0, "text": "first"},
        ])
        self.assertEqual(["first", "second"], [item["text"] for item in result])
        self.assertEqual(5.0, result[0]["end"])
    def test_openvino_reconciliation_caps_hallucinated_exact_repetition(self):
        repeated = [{"start": float(index), "end": float(index+1), "text": "loop"} for index in range(12)]
        self.assertEqual(2, len(reconcile_segments(repeated)))
    def test_compressed_diarization_turns_map_back_to_original_timeline(self):
        mapping=[{"compressedStart":0.0,"compressedEnd":2.0,"originalStart":10.0,"originalEnd":12.0},
                 {"compressedStart":2.25,"compressedEnd":4.25,"originalStart":30.0,"originalEnd":32.0}]
        turns=restore_turn_timestamps([{"start":1.5,"end":2.75,"speaker":"S1"}],mapping)
        self.assertEqual(2,len(turns));self.assertEqual(11.5,turns[0]["start"]);self.assertEqual(30.5,turns[1]["end"])
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
