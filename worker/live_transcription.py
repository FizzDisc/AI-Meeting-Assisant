"""One serial, disposable model process for live transcription batches."""
from __future__ import annotations
import json
import os
import signal
import subprocess
import sys
import threading
from pathlib import Path


def stop_tree(process):
    if process.poll() is not None:
        return
    if sys.platform == "win32":
        command = Path(os.environ["SystemRoot"]) / "System32" / "taskkill.exe"
        result = subprocess.run([str(command), "/PID", str(process.pid), "/T", "/F"],
                                capture_output=True, timeout=10,
                                creationflags=subprocess.CREATE_NO_WINDOW)
        if result.returncode and process.poll() is None:
            raise RuntimeError("Could not terminate the transcription process tree.")
    else:
        try: os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError: pass
    process.wait(timeout=5)


class LiveProcess:
    def __init__(self, idle_seconds=120, server_script=None):
        self.server_script = server_script or Path(__file__).resolve()
        self.process = None
        self.key = None
        self.active = None
        self.timer = None
        self.lock = threading.RLock()
        self.idle_seconds = idle_seconds

    def submit(self, key, request_path, log_path):
        with self.lock:
            if self.active is not None:
                raise ValueError("A live transcription batch is already running.")
            if self.timer:
                self.timer.cancel()
                self.timer = None
            if self.key != key or self.process is None or self.process.poll() is not None:
                self.close()
                self.process = subprocess.Popen(
                    [sys.executable, "-u", str(self.server_script), "--serve"],
                    stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                    text=True, encoding="utf-8", start_new_session=sys.platform != "win32",
                    creationflags=0x00004000 if sys.platform == "win32" else 0)
                self.key = key
            self.active = str(request_path)
            try:
                self.process.stdin.write(json.dumps({"request": str(request_path), "log": str(log_path)}) + "\n")
                self.process.stdin.flush()
            except Exception:
                self.close()
                raise
            return self.process

    def finished(self, request_path):
        with self.lock:
            if self.active != str(request_path):
                return
            self.active = None
            # Serialize timer eviction and submission; stale timers cannot evict a new job.
            timer = threading.Timer(self.idle_seconds, lambda: self._expire(timer))
            timer.daemon = True
            self.timer = timer
            timer.start()

    def _expire(self, timer):
        with self.lock:
            if self.timer is timer and self.active is None:
                self.close()

    def close(self):
        with self.lock:
            if self.timer:
                self.timer.cancel()
                self.timer = None
            if self.process:
                stop_tree(self.process)
                self.process.stdin.close()
            self.process = None
            self.active = None
            self.key = None


def serve():
    from transcription_job import run, write_atomic
    cache = {}
    for line in sys.stdin:
        message = json.loads(line)
        request_path = Path(message["request"])
        request = json.loads(request_path.read_text(encoding="utf-8"))
        status_path = Path(request["statusPath"])
        failed = False
        # Redirect the OS descriptor as well as Python writes, including native diagnostics.
        with open(message["log"], "w", encoding="utf-8") as log:
            saved = os.dup(2)
            os.dup2(log.fileno(), 2)
            try:
                run(request_path, model_cache=cache)
            except Exception as exc:
                failed = True
                write_atomic(status_path, {"status": "failed", "progress": 0.0, "error": str(exc)})
            finally:
                sys.stderr.flush()
                os.dup2(saved, 2)
                os.close(saved)
        # The parent must not report completion until log handles are released.
        status_path.with_suffix(".done").touch()
        if failed:
            return 1  # Never reuse potentially corrupted native model state.
    return 0


if __name__ == "__main__":
    raise SystemExit(serve())
