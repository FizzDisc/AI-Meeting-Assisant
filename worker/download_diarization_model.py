import argparse, os
from pathlib import Path
import truststore
from huggingface_hub import snapshot_download
p=argparse.ArgumentParser();p.add_argument("--token",default=os.environ.get("HF_TOKEN"));p.add_argument("--target",type=Path,required=True);a=p.parse_args()
if not a.token or not a.token.startswith("hf_") or len(a.token) < 23: raise SystemExit("A complete Hugging Face token is required.")
truststore.inject_into_ssl();snapshot_download(repo_id="pyannote/speaker-diarization-community-1",local_dir=a.target.resolve(),token=a.token)
if not (a.target/"config.yaml").is_file():raise SystemExit("Downloaded pipeline is missing config.yaml.")
print(a.target.resolve())
