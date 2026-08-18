# ADR 019: Versioned application settings

## Status
Accepted for Sprint 2.9.

## Decision
Settings are stored atomically as schema version 1 under local application data. Recording defaults apply immediately where safe. Capture-library, model-directory and compute-profile changes are validated on save and applied after restart because they define constructor-time resource boundaries. The configured capture root is shared by recording, recovery, meeting discovery and deletion.

No model is bundled or downloaded by the settings screen. A blank model path retains development-model discovery.
