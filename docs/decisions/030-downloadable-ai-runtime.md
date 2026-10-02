# One-click AI setup from public sources

The Welcome screen downloads a private Python runtime and installs the tested AI libraries with one button. No system Python, separate installer, account or application-hosted runtime package is required.

Sources:
- Full portable CPython 3.12.10 Windows x64 ZIP (including pip) from python.org. PublicRuntimeSetup pins its URL, SHA-256 and archive sizes.
- pip 25.3 from PyPI, for machine-readable download progress.
- PyTorch 2.8.0 CPU, torchaudio 2.8.0 CPU and torchvision 0.23.0 CPU from https://download.pytorch.org/whl/cpu, without dependency resolution.
- Remaining dependencies pinned in worker/runtime-requirements-win-x64.txt, installed from https://pypi.org/simple. This snapshot targets Windows x64.
- FFmpeg from imageio-ffmpeg. Speech models remain separate Hugging Face downloads.

The full Python ZIP has a standard library and pip without the restricted ._pth configuration of the embedded distribution. Adjacent worker modules are importable. External pip configuration and Python search-path overrides are excluded during setup.

Four stages cover Python, the AI engine, remaining libraries and final validation. Python has a known download total. pip --progress-bar raw reports bytes for the CURRENT FILE, labelled accordingly in the UI with speed and a per-file time estimate. Resolution, builds and installation use an indeterminate bar. Setup requires 6 GiB free including temporary files; no fabricated overall download size is shown.

A hash-checked Python archive is safely extracted to an app-owned staging directory, then moved to its final private location before executing it to avoid Windows DLL rename locks. The active-runtime pointer is atomically replaced only after package installation, imports and FFmpeg validation succeed. Cancellation stops child processes and removes the new incomplete runtime while preserving the prior pointer. An exclusive file lock prevents concurrent installers. Existing venv installations remain usable; models and recordings are untouched. A completion marker permits reuse without another package download.

The MSI includes the requirements file. No runtime-package.json, hosting configuration or GitHub publication is needed. A new requirements snapshot should be verified with a clean installation before release.

Verification: dotnet run --project tests/AiMeetingAssistant.Windows.SmokeTests -c Release -- --runtime-regression

Tests cover pip byte progress, per-file reset, unknown totals, phase changes, activation/reuse, cancellation, integrity failures, truncated downloads, ZIP traversal and validation failure preserving the previous runtime. The complete public-source installation is also checked in an isolated local test directory with a worker health request.
