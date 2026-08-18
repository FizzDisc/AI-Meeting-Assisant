from __future__ import annotations
import argparse
from pathlib import Path
import truststore
from huggingface_hub import snapshot_download
parser = argparse.ArgumentParser()
parser.add_argument("--repository", default="OpenVINO/whisper-small-fp16-ov")
parser.add_argument("--target", type=Path, required=True)
args = parser.parse_args()
truststore.inject_into_ssl()
snapshot_download(repo_id=args.repository, local_dir=args.target.resolve())
