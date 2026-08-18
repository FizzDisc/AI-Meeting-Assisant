# ADR 016: Transcript view and local exports

## Status

Accepted for Sprint 2.6.

## Decision

Transcript parsing, validation, timestamp/source formatting and atomic Markdown/JSON export live in Core. The WPF transcript window loads a validated schema 1 or 2 document, displays chronologically ordered segments and filters microphone/system-audio sources without changing the underlying artifact. The main window re-discovers the newest local transcript after restart and opens the viewer explicitly.

Markdown exports include processing metadata followed by timestamped, source-labelled sections. JSON exports preserve the supported structured document. Both formats use a sibling temporary file followed by atomic replacement. Save locations are user-selected through the native Windows dialog; no export leaves the machine automatically.

## Consequences

- Transcript content remains outside diagnostic logs.
- Corrupt/unsupported documents fail visibly instead of producing partial views.
- Filtering affects only the view; exports contain the complete transcript to avoid accidental data loss.
- Editing, search, seek-to-media and partial/filtered export remain later work.
