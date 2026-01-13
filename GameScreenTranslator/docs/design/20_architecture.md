# Architecture (P0)
Last updated: 2026-01-10

## High-level system diagram
Core loop is hotkey-driven:

1) **Hotkey** → 2) **Capture active window** → 3) **Screen signature + cycle priority** →  
4) **Vision LLM call** → 5) **Speak via TTS** → 6) **Log interaction**

Question flow adds STT:
- Capture (at key-down) → STT recording → LLM answer using screenshot+question → TTS → log

## Main runtime entrypoints (`GameScreenTranslator/Program.cs`)
Command modes:
- `--capture-once`: capture active window + print signature
- `--cycle-demo`: repeatedly capture + compute signature + show cycle index + priority
- `--foundry-translate-once [--raw]`: one vision call + print/tts
- `--hotkey-mode` (default): global hotkeys and background loop

Runtime base directory selection:
- Prefers config in current working directory for dev
- Falls back to executable directory for portable publish
- See `ResolveRuntimeBaseDir()` in `Program.cs`

First-run bootstrap:
- `ConfigBootstrapper.EnsureConfigFilesExist(baseDir)` creates missing config files from embedded templates.

## Key components (code map)

### Capture (`Core/Capture`)
- `ActiveWindowCapture`
  - `CaptureActiveWindow()` gets foreground HWND + title + process, captures window bitmap, converts to ImageSharp `Image<Rgba32>`.
  - Capture strategy:
    1) Try `PrintWindow(hwnd, ...)`
    2) If fail or black frame, fallback to `Graphics.CopyFromScreen(...)`
  - Note: CopyFromScreen uses a compatibility retry if `SourceCopy|CaptureBlt` is rejected.
- `WindowCaptureResult`: (hwnd/title/pid/process/screenshot)

### State & cycling (`Core/State`)
- `ScreenSignature`
  - Computes screen signature from (window title + downscaled screenshot bytes) using configured hash algorithm.
- `ScreenStateManager`
  - Tracks current signature
  - Maintains per-signature cycle index and “already spoken” keys for skip logic
- `PriorityCycler`
  - Maps cycle index → `UiPriority` enum (e.g., Highlighted, Dialog, etc.)
  - Used to focus model attention on different UI regions across repeated presses.

### Model client (`Core/Foundry`)
- `AzureFoundryClient`
  - OpenAI-compatible HTTP `chat/completions` call to Azure Foundry inference endpoint.
  - Sends:
    - System prompt (from YAML prompt pack)
    - User prompt (rendered template)
    - Screenshot as `image_url` (data URL with base64 PNG)
  - Requests JSON response format (`response_format: json_object`) when supported.
  - Resilience: retries and auto-fallback for common “unsupported parameter” errors.

### Audio (`Core/Audio`)
- `AzureSpeechTtsService`: speak text
- `AzureSpeechSttService`: STT recognize-once-with-end-silence (tap-to-talk)
- `EarconPlayer`: short non-speech cues (e.g., "thinking" chime)
- `FallbackSpeech`: resolves configured fallback strings and capture-failed messages

### Runtime / interaction loop (`Core/App`)
- `HotkeyAppRunner`
  - Registers global hotkeys (translate/cycle, stop, tap-to-talk)
  - Manages cancellation and “interrupt speaking”
  - Produces `InteractionLogEntry` records per interaction

- `TrayAppContext`
  - Runs a system-tray icon on STA thread (best-effort, optional)
  - Right-click tray opens logs folder / exits

### Logging (`Core/Logging`)
- `InteractionLogger`: appends JSONL entries to configured path
- `InteractionLogEntry`: structured schema (window/process/sig/cycle/priority/latencies/tts text/errors/screenshot base64)
- `Logger`: console logger + levels

### Configuration (`Core/Configuration`)
- `AppSettings` + `AppSettingsLoader`: loads `appsettings.json`
- `SpeechLanguageConfig` + loader: loads `speech_language_config.json`
- `PromptPack` + `PromptPackLoader`: loads `configs/prompts.yaml`
- `PromptTemplate`: string placeholder substitution `{key}` → value
- `ConfigBootstrapper`: writes missing config files from embedded templates on first run
- `HotkeyConfig`: parses strings like `Ctrl+Alt+T` into modifier + key

## Threading & cancellation model (P0)
- Hotkey callbacks kick off async tasks (`Task.Run(...)`).
- A shared `_operationCts` is used to cancel the current operation when a new one begins or Stop is pressed.
- TTS is interrupted via `_tts.StopAsync()` and cancellation tokens passed to speech/model calls when supported.

## Error handling philosophy (P0)
- Fail closed per interaction: if any step fails, speak a short fallback message (language-specific).
- Continue running: a single failure should not crash the process.
- Log errors to JSONL for debugging.
