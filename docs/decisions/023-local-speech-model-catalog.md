# ADR 023: Curated local speech-model catalog

## Decision

The application owns a small, versioned catalog of supported speech-model profiles (`tiny`, `small`, `medium`). Each entry defines its stable settings ID, local directory name, user-facing quality class, indicative download size and hardware guidance. Settings persist the stable ID, while the composition root resolves it to an installed local directory when the application starts.

The catalog never downloads implicitly. Sprint 3.4.1 displays installed state and rejects selecting an unavailable model unless an advanced custom directory overrides the catalog. Installation and removal are delegated to Sprint 3.4.2.

## Consequences

- Model choice is explicit and survives restarts without persisting machine-specific catalog paths.
- Existing custom model directories and the development environment override remain compatible.
- Catalog metadata is guidance, not a performance guarantee.
- Transcription continues to fail closed when no selected local model exists.
