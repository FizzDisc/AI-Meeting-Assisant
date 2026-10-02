"""Explicit, allow-listed installer/remover for local faster-whisper models."""
from __future__ import annotations
import argparse, json, os, shutil, sys, threading, time, uuid
from pathlib import Path

CATALOG = {
    "tiny": ("Systran/faster-whisper-tiny", "faster-whisper-tiny"),
    "small": ("Systran/faster-whisper-small", "faster-whisper-small"),
    "medium": ("Systran/faster-whisper-medium", "faster-whisper-medium"),
}
REQUIRED = ("config.json", "model.bin", "tokenizer.json")

def emit(status: str, **values: object) -> None:
    print(json.dumps({"status": status, **values}), flush=True)

def target_for(root: Path, model_id: str) -> Path:
    if model_id not in CATALOG: raise ValueError(f"Unsupported speech model ID: {model_id}")
    root = root.resolve()
    target = root / CATALOG[model_id][1]
    if target.parent != root: raise ValueError("Model target escaped the managed model root.")
    return target

def validate(path: Path) -> None:
    missing = [name for name in REQUIRED if not (path / name).is_file()]
    if missing: raise ValueError(f"Downloaded model is incomplete: missing {', '.join(missing)}")
    if (path / "model.bin").stat().st_size < 1_000_000: raise ValueError("Downloaded model weights are unexpectedly small.")

def local_download_bytes(directory: Path) -> int:
    """Count payload and unfinished transfers, excluding Hub bookkeeping."""
    total = 0
    for path in directory.rglob("*"):
        try:
            if not path.is_file() or path.is_symlink(): continue
            relative = path.relative_to(directory)
            if ".cache" in relative.parts and path.suffix != ".incomplete": continue
            total += path.stat().st_size
        except OSError:
            pass  # Downloads atomically rename files while we inspect them.
    return total


class DownloadProgress:
    def __init__(self, directory: Path, model_id: str, total: int):
        self.directory, self.model_id, self.total = directory, model_id, total
        self.stop = threading.Event()
        self.thread = threading.Thread(target=self._monitor, daemon=True)

    def report(self):
        done = local_download_bytes(self.directory)
        emit("progress", modelId=self.model_id, downloadedBytes=min(done, self.total) if self.total else done,
             totalBytes=self.total, bytesPerSecond=0, measurement="local-file-size")

    def _monitor(self):
        while not self.stop.wait(.5): self.report()

    def __enter__(self):
        self.report()
        self.thread.start()
        return self

    def __exit__(self, *args):
        self.stop.set()
        self.thread.join()
        self.report()


def install(root: Path, model_id: str) -> None:
    import truststore
    from huggingface_hub import HfApi, snapshot_download
    truststore.inject_into_ssl()
    root.mkdir(parents=True, exist_ok=True)
    target = target_for(root, model_id)
    if target.exists(): raise FileExistsError(f"Model is already installed: {target}")
    temporary = root / f".{target.name}.installing-{uuid.uuid4().hex}"
    try:
        repo_id=CATALOG[model_id][0]
        info=HfApi().model_info(repo_id,files_metadata=True)
        total=sum(int(sibling.size or 0) for sibling in info.siblings)
        emit("downloading", modelId=model_id,totalBytes=total)
        with DownloadProgress(temporary, model_id, total):
            snapshot_download(repo_id=repo_id, revision=info.sha, local_dir=temporary)
        emit("validating", modelId=model_id)
        validate(temporary)
        os.replace(temporary, target)
        emit("completed", modelId=model_id, path=str(target))
    finally:
        if temporary.exists(): shutil.rmtree(temporary, ignore_errors=True)

def remove(root: Path, model_id: str) -> None:
    target = target_for(root, model_id)
    if not target.exists(): raise FileNotFoundError(f"Model is not installed: {target}")
    if target.is_symlink() or target.resolve().parent != root.resolve():
        raise ValueError("Refusing to remove a linked or unmanaged model directory.")
    emit("removing", modelId=model_id)
    shutil.rmtree(target)
    emit("completed", modelId=model_id)

def main() -> int:
    parser=argparse.ArgumentParser();parser.add_argument("operation",choices=("install","remove"));parser.add_argument("--model-id",required=True,choices=tuple(CATALOG));parser.add_argument("--models-root",required=True,type=Path);args=parser.parse_args()
    try:
        (install if args.operation=="install" else remove)(args.models_root.resolve(),args.model_id);return 0
    except Exception as error:
        emit("failed",modelId=args.model_id,error=str(error));print(str(error),file=sys.stderr);return 1
if __name__=="__main__":raise SystemExit(main())
