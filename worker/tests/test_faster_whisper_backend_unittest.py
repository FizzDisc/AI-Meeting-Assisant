import tempfile, unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock, patch
from faster_whisper_backend import FasterWhisperTranscriber

class BackendTests(unittest.TestCase):
    def test_local_model_lazy_segments_timestamps_and_language(self):
        with tempfile.TemporaryDirectory() as folder:
            library=Mock(); pipeline=library.BatchedInferencePipeline.return_value
            consumed=[]
            def segments():
                consumed.append(True)
                yield SimpleNamespace(start=1.25,end=3.5,text=" Test sentence.")
                yield SimpleNamespace(start=3.5,end=4,text=" ")
            pipeline.transcribe.return_value=(segments(),SimpleNamespace(language="de"))
            with patch.dict("sys.modules",{"faster_whisper":library}):
                backend=FasterWhisperTranscriber(folder,"cpu","int8")
                result=backend.transcribe(Path(folder)/"audio.wav",batch_size=4)
            self.assertEqual([True],consumed)
            self.assertEqual({"language":"de","segments":[{"start":1.25,"end":3.5,"text":" Test sentence."}]},result)
            self.assertTrue(library.WhisperModel.call_args.kwargs["local_files_only"])
            self.assertTrue(pipeline.transcribe.call_args.kwargs["vad_filter"])
            self.assertIsNone(pipeline.transcribe.call_args.kwargs["language"])
            self.assertEqual(4,pipeline.transcribe.call_args.kwargs["batch_size"])

    def test_missing_model_fails_without_network_lookup(self):
        library=Mock()
        with tempfile.TemporaryDirectory() as folder, patch.dict("sys.modules",{"faster_whisper":library}):
            with self.assertRaises(FileNotFoundError):FasterWhisperTranscriber(Path(folder)/"missing","cpu","int8")
            library.WhisperModel.assert_not_called()
