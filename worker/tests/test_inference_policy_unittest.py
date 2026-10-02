import os
import unittest
from unittest.mock import patch
from inference_policy import configure_local_inference

class InferencePolicyTests(unittest.TestCase):
    def test_inference_overrides_online_flags_and_drops_inherited_tokens(self):
        with patch.dict(os.environ, {"HF_HUB_OFFLINE": "0", "TRANSFORMERS_OFFLINE": "0",
                                     "PYANNOTE_METRICS_ENABLED": "1", "HF_TOKEN": "secret", "HUGGING_FACE_HUB_TOKEN": "secret"}):
            configure_local_inference()
            self.assertEqual("0", os.environ["PYANNOTE_METRICS_ENABLED"])
            self.assertEqual("1", os.environ["HF_HUB_OFFLINE"])
            self.assertEqual("1", os.environ["TRANSFORMERS_OFFLINE"])
            self.assertEqual("1", os.environ["HF_HUB_DISABLE_IMPLICIT_TOKEN"])
            self.assertNotIn("HF_TOKEN", os.environ)
            self.assertNotIn("HUGGING_FACE_HUB_TOKEN", os.environ)
