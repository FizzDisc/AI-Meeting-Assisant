# Sprint 1.3 completion summary

## Outcome

Sprint 1.3 adds real capture from one selected Windows microphone endpoint. The desktop app shows a live input level and writes a timestamped, finalized PCM16 WAV file under `artifacts/captures/`.

## Implementation

- `IAudioCaptureProvider` keeps the Core layer independent of Windows APIs.
- `WasapiAudioCapture` owns the native WASAPI session and buffer loop.
- `WasapiAudioCaptureProvider` resolves the selected endpoint and forwards capture events.
- `RealCaptureCoordinator` owns the active provider and serializes start, stop and cleanup.
- `RecordingSession` funnels user stop, capture fault and application shutdown through an idempotent completion path.
- `Pcm16WavWriter` is shared production code for RIFF header creation and finalization.
- `MainWindowViewModel` exposes recording state, actionable nested errors and live RMS level updates to WPF.

The implementation uses WASAPI shared mode and the endpoint mix format. PCM16, PCM24 and IEEE Float32 input samples are converted to PCM16 output. `WAVEFORMATEXTENSIBLE` subformats and multichannel buffers are supported.

## Validation

- Solution build: 0 errors, 0 warnings.
- Core test harness: 20/20 passed.
- Windows smoke harness: 3/3 passed.
- Repeated Core race/cleanup runs passed.
- The WAV test invokes the production `Pcm16WavWriter`, not a test-local copy.

The tests cover stereo Float32 and PCM24 conversion, channel order and output length, capture-fault cleanup, a concurrent fault/stop path, repeated shutdown and WAV header finalization.

Manual Windows hardware validation passed on 2026-08-17 using an Intel microphone array. The resulting file was playable and its header reported 48,000 Hz, 16-bit PCM output, four channels, 8.35 seconds and 3,206,400 audio-data bytes (3,206,444 bytes including the WAV header).

## Important fixes found during hardware validation

- WPF level binding made explicitly one-way.
- `WAVEFORMATEX` and `WAVEFORMATEXTENSIBLE` use native 2-byte packing.
- WASAPI initialization remains in the COM apartment that obtained the endpoint.
- The optional audio-session GUID is marshalled as a null pointer.
- The correct `IAudioCaptureClient` interface ID is used.
- Application closing waits for capture cleanup without recursively closing the window inside the active closing event.

## Deferred work

- System/Teams audio capture (Sprint 1.4).
- Screen/window capture (Sprint 1.5).
- Synchronized multi-stream session manifests (Sprint 1.6).
- Extended manual failure testing such as unplugging a USB microphone mid-recording.
- Transcription, diarization and meeting intelligence.

## Sign-off

AMA-104 is complete for Sprint 1.3. The basic real-hardware path—select, start, live level, stop, finalized WAV and playback—has passed. Device-loss coverage remains automated for this sprint and should be expanded in the later capture smoke-test matrix.
