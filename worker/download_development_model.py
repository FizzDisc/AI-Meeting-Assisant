from pathlib import Path
import truststore
from huggingface_hub import snapshot_download

# Use the native Windows certificate store, including organization-managed CAs.
# TLS verification remains enabled; never replace this with a disable flag.
truststore.inject_into_ssl()

target = Path(__file__).resolve().parent / "models" / "faster-whisper-tiny"
snapshot_download(repo_id="Systran/faster-whisper-tiny", local_dir=target)
print(target)
