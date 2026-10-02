# Dependency security review — 2026-10-02

## Result

Six dependency alerts remain open. This change does not patch the libraries or
claim the application is free of these vulnerabilities. No alerts were dismissed.

The pinned WhisperX 3.8.6 distribution requires huggingface-hub < 1.0.0.
Transformers 5.10.0 (the minimum addressing the listed tokenizer save issue)
requires huggingface-hub >= 1.5.0. A forced upgrade or --no-deps installation
would violate the supported dependency graph. NLTK 3.10.3 is the latest release
queried and the listed advisory has no patched version.

Metadata evidence: https://pypi.org/pypi/whisperx/3.8.6/json and
https://pypi.org/pypi/transformers/5.10.0/json.

## Reachability review

| Advisory | Affected operation | Current application path |
| --- | --- | --- |
| [GHSA-x9r9-c232-4q39](https://github.com/advisories/GHSA-x9r9-c232-4q39) | Custom generation-code retrieval | No custom generation API used; inference now explicitly offline |
| [GHSA-xrqw-3rrv-vx5w](https://github.com/advisories/GHSA-xrqw-3rrv-vx5w) | Saving attacker-controlled chat templates | No tokenizer/processor save_pretrained path used |
| [GHSA-fgcw-684q-jj6r](https://github.com/advisories/GHSA-fgcw-684q-jj6r) | LightGlue model initialization | No LightGlue model used |
| [GHSA-29pf-2h5f-8g72](https://github.com/advisories/GHSA-29pf-2h5f-8g72) | AutoModelForCausalLM/config-controlled kernels | Speech inference uses a local CTranslate2 model via WhisperX, not this loader |
| [GHSA-69w3-r845-3855](https://github.com/advisories/GHSA-69w3-r845-3855) | Trainer checkpoint RNG loading | No Trainer or training checkpoint restore; runtime pins Torch 2.8.0 |
| [GHSA-8mgp-746c-j5xp](https://github.com/advisories/GHSA-8mgp-746c-j5xp) | NLTK model artifact path sandbox bypass | No NLTK model training/import/export APIs or pathsec sandbox used |

This is a source-path assessment, not a complete transitive dependency audit.
WhisperX uses transformers Pipeline for ASR and NLTK in its alignment module;
the application does not call WhisperX alignment. Reassess before adding model
architectures, alignment, training, custom repositories or checkpoint support.

## Defense in depth added

- Inference sets Hugging Face/Transformers offline flags before ML imports,
  disables implicit token use and telemetry, and removes inherited HF tokens.
  These flags are library controls, not an operating-system network sandbox.
- WhisperX receives local_files_only=True explicitly.
- Speech model setup downloads only known CTranslate2 payload filenames and license notices, at the
  exact revision returned by the allow-listed repository metadata. It does not
  download arbitrary Python code or other repository files.
- Explicit setup downloads run separately; they are not disabled by inference.
- Runtime activation checks pip dependency consistency and the actual WhisperX
  ASR import. The welcome screen checks ASR loading and FFmpeg before readiness.

Do not load untrusted user-supplied models. Offline mode does not make arbitrary
local model artifacts safe. Replacing/upgrading the affected dependency chain
remains outstanding; do not advertise the alerts as resolved.

Pyannote usage metrics are explicitly disabled for application-launched Python
processes and standalone inference entry points, overriding inherited settings.
Reference: https://github.com/pyannote/pyannote-audio#telemetry .

The downloaded imageio-ffmpeg 0.6.0 Windows binary reports FFmpeg 7.1,
--enable-gpl and --enable-version3, and GPL-3.0-or-later via ffmpeg -L.
The wrapper package BSD-2-Clause declaration does not describe that binary.
It is downloaded from the vendor package at setup time, not bundled in the MSI.
Do not redistribute a combined runtime without reviewing native component
source/notices obligations.
