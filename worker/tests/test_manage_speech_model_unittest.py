import tempfile,unittest
from pathlib import Path
from manage_speech_model import target_for,validate,local_download_bytes,DownloadProgress
from unittest.mock import patch, Mock
from types import SimpleNamespace
import manage_speech_model
import threading
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
 def test_progress_counts_partial_payload_not_file_count_or_metadata(self):
  with tempfile.TemporaryDirectory() as value:
   root=Path(value);cache=root/".cache"/"huggingface"/"download";cache.mkdir(parents=True)
   (root/"config.json").write_bytes(b"x"*20)
   partial=cache/"model.hash.incomplete";partial.write_bytes(b"x"*4096)
   (cache/"model.metadata").write_bytes(b"x"*8000)
   (cache/"model.lock").write_bytes(b"x"*100)
   self.assertEqual(4116,local_download_bytes(root))
   partial.rename(root/"model.bin")
   self.assertEqual(4116,local_download_bytes(root))
 def test_monitor_emits_while_download_runs_and_stops_on_error(self):
  with tempfile.TemporaryDirectory() as value:
   root=Path(value);(root/"model.bin").write_bytes(b"x"*1024)
   update=threading.Event();events=[]
   def capture(status,**data):
    events.append(data)
    if len(events)>1:update.set()
   with patch("manage_speech_model.emit",side_effect=capture):
    with self.assertRaisesRegex(ValueError,"network"):
     with DownloadProgress(root,"tiny",2048) as progress:
      self.assertTrue(update.wait(3))
      raise ValueError("network")
   self.assertFalse(progress.thread.is_alive())
   self.assertTrue(all(event["downloadedBytes"]==1024 for event in events))
 def test_download_pins_revision_and_excludes_repository_code(self):
  with tempfile.TemporaryDirectory() as value:
   root=Path(value)
   files=[SimpleNamespace(rfilename=n,size=100) for n in manage_speech_model.PAYLOAD]
   files.append(SimpleNamespace(rfilename="custom_generate/generate.py",size=999999))
   hub=Mock();hub.HfApi.return_value.model_info.return_value=SimpleNamespace(sha="fixed-revision",siblings=files)
   def download(**kwargs):
    target=kwargs["local_dir"];target.mkdir()
    for name in manage_speech_model.REQUIRED:(target/name).write_bytes(bytes(1000000) if name=="model.bin" else b"{}")
   hub.snapshot_download.side_effect=download
   with patch.dict("sys.modules", {"truststore":Mock(),"huggingface_hub":hub}), patch("manage_speech_model.emit") as emit:
    manage_speech_model.install(root,"tiny")
   kwargs=hub.snapshot_download.call_args.kwargs
   self.assertEqual("fixed-revision",kwargs["revision"])
   self.assertEqual(list(manage_speech_model.PAYLOAD),kwargs["allow_patterns"])
   self.assertNotIn("custom_generate/generate.py",kwargs["allow_patterns"])
   self.assertEqual(len(manage_speech_model.PAYLOAD)*100,emit.call_args_list[0].kwargs["totalBytes"])
if __name__=="__main__":unittest.main()
