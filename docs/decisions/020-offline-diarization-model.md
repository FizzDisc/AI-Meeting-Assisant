# ADR 020: Explicit offline diarization model

Sprint 3 uses `pyannote/speaker-diarization-community-1` only after the user accepts its gated conditions and explicitly installs it. The token is entered through a masked prompt, used only by the downloader and never persisted by the app. Runtime loading uses the local directory. Diarization runs in an isolated process on `system_audio` and prefers exclusive speaker turns; microphone remains the known local participant.

On the pinned Windows CPU stack, TorchCodec cannot load its native decoder DLL. Since capture masters are guaranteed PCM16 WAV, the job reads them directly and passes an in-memory waveform dictionary to pyannote. This avoids a fragile decoder dependency without changing audio content.
