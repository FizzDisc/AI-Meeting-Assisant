# Recording & Transcription 1.0 release contract

Version 1.0 covers capture, local transcription, diarization, transcript review/export, meeting library and guarded storage lifecycle. Minutes, action items, risks, decisions, knowledge-base features and general-purpose LLMs are post-1.0.

Release gates: clean Windows x64 install/upgrade/uninstall; self-contained .NET desktop; no development environments/models/caches in the installer; optional AI runtime and models outside Program Files; recording remains usable without ML; all automated suites pass; manual capture/recovery/transcription/diarization/export/FLAC smoke passes; production rollout requires organization-owned code signing.

The MSI build pins WiX Toolset 6.0.2. WiX 7 introduces a separate OSMF EULA/maintenance-fee gate, which is not accepted implicitly by this repository.
