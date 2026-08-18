import tempfile,unittest
from pathlib import Path
from manage_speech_model import target_for,validate
class ModelManagerTests(unittest.TestCase):
 def test_catalog_target_is_scoped(self):
  with tempfile.TemporaryDirectory() as value:
   root=Path(value);self.assertEqual(root.resolve()/"faster-whisper-small",target_for(root,"small"))
 def test_unknown_model_is_rejected(self):
  with tempfile.TemporaryDirectory() as value:
   with self.assertRaises(ValueError):target_for(Path(value),"large")
 def test_validation_requires_real_artifacts(self):
  with tempfile.TemporaryDirectory() as value:
   root=Path(value);(root/"config.json").write_text("{}");(root/"tokenizer.json").write_text("{}");(root/"model.bin").write_bytes(b"x")
   with self.assertRaises(ValueError):validate(root)
if __name__=="__main__":unittest.main()
