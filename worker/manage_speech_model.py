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

def install(root: Path, model_id: str) -> None:
    import truststore
    from huggingface_hub import HfApi, snapshot_download
    from tqdm.auto import tqdm
    truststore.inject_into_ssl()
    root.mkdir(parents=True, exist_ok=True)
    target = target_for(root, model_id)
    if target.exists(): raise FileExistsError(f"Model is already installed: {target}")
    temporary = root / f".{target.name}.installing-{uuid.uuid4().hex}"
    try:
        repo_id=CATALOG[model_id][0]
        info=HfApi().model_info(repo_id,files_metadata=True)
        total=sum(int(sibling.size or 0) for sibling in info.siblings)
        class ReportingTqdm(tqdm):
            lock=threading.Lock();downloaded=0;started=time.monotonic();last_emit=0.0
            def update(self,n=1):
                changed=super().update(n)
                now=time.monotonic()
                with self.lock:
                    type(self).downloaded+=n
                    if now-type(self).last_emit>=.25:
                        elapsed=max(now-type(self).started,.001);done=min(type(self).downloaded,total)
                        emit("progress",modelId=model_id,downloadedBytes=done,totalBytes=total,bytesPerSecond=int(done/elapsed),file=str(getattr(self,"desc","") or ""));type(self).last_emit=now
                return changed
        emit("downloading", modelId=model_id,totalBytes=total)
        snapshot_download(repo_id=repo_id, local_dir=temporary,tqdm_class=ReportingTqdm)
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
