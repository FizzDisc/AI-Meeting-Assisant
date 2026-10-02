# First-run setup for installed applications

Historical design for 1.0.2. The manual Python setup described below is superseded by [one-click public-source setup](030-downloadable-ai-runtime.md) in installer 1.0.6.

The desktop opens a four-step welcome assistant before constructing the recording
workspace when `SetupCompleted` is absent or false. Existing settings remain valid;
completion preserves unrelated settings. "Set up later" leaves the assistant
pending and opens recording-only mode. "Recording only" completes setup without
requiring Python, a model, or downloads. Closing the assistant exits the app.
Settings can schedule the assistant for the next application launch.

The assistant validates writable recording storage, offers Tiny/Small/Medium with
their existing size/quality guidance, and checks worker health before enabling AI.
It can create a private Python environment from an existing supported 64-bit Python
installation, install the pinned WhisperX/truststore dependencies, and provision
FFmpeg through imageio-ffmpeg 0.6.0. If Python is missing, the user is directed to
the official download page and can select the installed executable. No downloads
start automatically. Cancellation terminates the setup process tree; interrupted
runtime installation can be retried. Model installation uses the existing model
manager and its temporary-directory validation/promotion.

New runtimes/models live under `%LOCALAPPDATA%\AI Meeting Assistant`, outside the
MSI directory. The default recording directory also lives in this writable user
root rather than being relative to the working directory. Legacy runtime/model
locations and explicit environment overrides remain supported. Settings downloads
use the same user model root; the worker receives the private FFmpeg path.

The initial assistant covers automatic/CPU/NVIDIA preferences. Intel runtime setup
and optional speaker identification remain advanced Settings workflows. Python
itself is not bundled or automatically installed. The MSI is installer revision
1.0.2; the application version remains 1.0.0.

Validation: solution build, Windows smoke tests including storage/model checks and
setup cancellation, and offscreen WPF renders of all four assistant pages. Real AI
downloads and GPU inference are not part of these tests.
