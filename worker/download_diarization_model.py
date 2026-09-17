"""Download the offline pipeline without secrets in process arguments."""
import argparse
import os
import re
import sys
from pathlib import Path


class SafeArgumentParser(argparse.ArgumentParser):
    def error(self, message):
        # Do not echo a secret from a legacy --token invocation.
        super().error("Invalid arguments. Use --target PATH and optionally --token-stdin.")


def main(argv=None):
    parser = SafeArgumentParser(allow_abbrev=False)
    parser.add_argument("--token-stdin", action="store_true")
    parser.add_argument("--target", type=Path, required=True)
    args = parser.parse_args(argv)
    # Preserve the desktop installer's environment-based interface.
    token = os.environ.pop("HF_TOKEN", None)
    if args.token_stdin:
        token = sys.stdin.readline().strip()
    if not token or not re.fullmatch(r"hf_[A-Za-z0-9]{20,}", token):
        print("A complete Hugging Face token is required.", file=sys.stderr)
        return 1
    try:
        import truststore
        from huggingface_hub import snapshot_download
        truststore.inject_into_ssl()
        snapshot_download(repo_id="pyannote/speaker-diarization-community-1",
                          local_dir=args.target.resolve(), token=token)
        if not (args.target / "config.yaml").is_file():
            print("Downloaded pipeline is missing config.yaml.", file=sys.stderr)
            return 1
    except Exception:
        # Third-party exception text can contain credentials or request details.
        print("Installation failed. Verify runtime setup, network, license acceptance and token access.", file=sys.stderr)
        return 1
    finally:
        token = None
    print(args.target.resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
