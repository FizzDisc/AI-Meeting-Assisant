# ADR 024: Managed speech-model lifecycle

## Decision

Catalog speech models are installed by an isolated Python process with a hard-coded model-ID/repository/directory allow-list. Downloads target a unique hidden sibling directory, validate the required CTranslate2 artifacts and minimum weight size, then atomically promote the directory. Cancellation terminates the process and the desktop removes only the matching temporary directory.

Removal uses the same allow-list and rejects links or resolved paths outside the managed `worker/models` root. The currently active default model cannot be removed; the user must select another installed model, save, restart and then remove it. Custom model directories are never managed or deleted.

## Consequences

- Interrupted downloads never appear installed.
- No UI value can redirect deletion outside the managed root.
- Repository file metadata supplies an indicative total while a custom progress adapter emits downloaded bytes and aggregate speed. Completion validation remains authoritative because cached/retried transfers can make network progress approximate.
- Model and compute settings hot-apply between jobs; changing the capture-library root still requires restart.
- Installed models remain explicit user-managed payloads and are not committed or bundled.
