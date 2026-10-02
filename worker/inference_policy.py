"""Offline Hugging Face policy for inference, separate from explicit setup downloads.

This is defense in depth, not an OS network sandbox or a dependency patch.
"""
import os


def configure_local_inference():
    # Override inherited opt-outs before importing ML libraries.
    for name in ("HF_HUB_OFFLINE", "TRANSFORMERS_OFFLINE", "HF_HUB_DISABLE_TELEMETRY",
                 "HF_HUB_DISABLE_IMPLICIT_TOKEN", "DO_NOT_TRACK"):
        os.environ[name] = "1"
    os.environ["PYANNOTE_METRICS_ENABLED"] = "0"
    for name in ("HF_TOKEN", "HUGGING_FACE_HUB_TOKEN"):
        os.environ.pop(name, None)
