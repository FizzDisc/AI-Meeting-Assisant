# 1.0.12 — understandable failures and local diagnosis export

Setup and model installation now give actionable guidance for network failures,
missing models/runtime components, storage and permission problems. Capture and
processing show concise guidance while existing technical activity remains local.
Unrecognized errors suggest a retry and diagnosis export rather than guessing a cause.

Settings > Diagnostics and the Welcome assistant offer a diagnosis report export.
The user chooses a local JSON file; nothing is uploaded. The report contains app
and Windows version, component file presence, the last runtime check state, and
up to 50 error categories with timestamps from the current application session.
It includes no raw logs, recordings, transcripts, tokens, device names or paths.
Personal paths are omitted entirely rather than partially masked.

Export does not start Python, load models, download components or interrupt a
recording. File presence is explicitly distinguished from successful runtime
validation. The report does not recover errors from earlier app sessions.

Includes the update-check feature from 1.0.11. The installer remains unsigned
while SignPath approval is pending.

## Verification

51 Core checks pass, including guidance classification, private-input exclusion
and bounded diagnostic history. The Release solution builds without warnings.
The Diagnostics tab and Welcome assistant at minimum window size were rendered
offscreen and visually inspected. No additional AI runtime or model was downloaded.

Windows setup regressions also pass: optional cleanup boundaries, runtime download/
activation/cancellation/integrity, system-only capture and first-run validation.
