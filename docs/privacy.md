# Privacy and downloads

AI Meeting Assistant records the selected audio sources and optional screen to
local files. Transcription and speaker processing run locally. The application
has no implemented meeting-upload or analytics service. Exports and recordings
remain at the paths chosen by the user; a cloud-synchronized folder can upload
those files through that folder's provider independently of this application.

Explicit setup/download actions contact external services:

- Python runtime: python.org.
- Python packages: PyPI and its file hosts; CPU AI packages: download.pytorch.org.
- Speech and optional speaker models: Hugging Face and its download/CDN hosts.
- Installer downloads: GitHub and its release/CDN hosts.

These services receive network request information such as the client IP address
and requested files. An optional Hugging Face token authorizes gated model
downloads. Do not include tokens or private recordings in bug reports.

Provider privacy information: [Python/PSF and PyPI](https://www.python.org/privacy/),
[PyTorch/Linux Foundation](https://www.linuxfoundation.org/legal/privacy-policy),
[Hugging Face](https://huggingface.co/privacy),
[GitHub](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).
Downloaded packages and models are subject to their respective licenses and terms.

Managed AI components are stored under `%LOCALAPPDATA%\AI Meeting Assistant`.
Interactive uninstallation offers optional removal of managed models and Python
components for the current Windows user. Recordings and settings are retained.

From version 1.0.9, application-launched Python processes explicitly disable
pyannote usage metrics and Hugging Face telemetry. Inference also enables the
Hugging Face/Transformers offline flags; explicit setup downloads remain online.
