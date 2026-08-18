# ADR 022: Meeting-scoped speaker display names

## Decision

User-entered speaker names are stored beside the transcript in `processing/speaker-names.json`. The mapping references stable technical IDs (`You`, `SPEAKER_00`, etc.) and changes presentation only; `transcript.json` remains immutable evidence produced by the AI pipeline.

The transcript viewer applies the mapping immediately and Markdown exports use the same resolved labels. Empty values remove an override. Ambiguous and unassigned segments cannot be renamed globally because they may represent different people.

## Consequences

- Re-running transcription does not destroy manual names.
- JSON transcript exports retain original model output and technical IDs.
- Deleting the meeting deletes its mapping with the session workspace.
- Cross-meeting voice identity remains a separate, explicit-consent capability.
