# Runtime layout & Configuration (P0)
Last updated: 2026-01-21

## Runtime base directory (where configs/logs live)
At runtime the app resolves a `baseDir` (see `ResolveRuntimeBaseDir()` in `GameScreenTranslator/Program.cs`):

Priority order:
1. Current working directory if it contains `appsettings.json` (dev-friendly)
2. `./GameScreenTranslator` if it contains `appsettings.json` (repo-root launch)
3. Executable directory if it contains `appsettings.json` (portable publish)
4. Executable directory fallback (portable default)

All relative config paths are resolved under this `baseDir`.

## First-run bootstrap behavior (portable EXE + dev)
On startup, `ConfigBootstrapper.EnsureConfigFilesExist(baseDir)` creates missing files from **embedded resources**:
- `appsettings.json` from embedded `appsettings.template.json`
- `audio_config.json` from embedded `audio_config.template.json`
- `configs/prompts.yaml` from embedded `prompts.template.yaml`
- `Assets/Sounds/tap-notification-357575.mp3` from embedded sound asset (earcons)

This enables a "drop the EXE into a folder and run" experience.

## Config files
### 1) `appsettings.json` (main app settings)
Template: `GameScreenTranslator/appsettings.template.json`  
Loaded by: `Core/Configuration/AppSettingsLoader.cs` into `Core/Configuration/AppSettings.cs`

Key sections (P0):
- `Providers.AzureFoundry`
  - `Endpoint` (OpenAI-compatible Foundry inference endpoint)
  - `ApiKey`
  - `ApiVersion` (default `2024-10-21`)
  - `LLM.*` (deployments, retry attempts, timeout)
- `Providers.AzureSpeech.ConfigPath`
  - defaults to `audio_config.json`
- `Hotkeys` (fully configurable as of 2026-01-15)
  - `Translate` (default: `Ctrl+Alt+T`) - Translate what's on screen
  - `Ask` (default: `Ctrl+Alt+W`) - Tap-to-talk question mode
  - `Stop` (default: `Ctrl+Alt+X`) - Stop speaking / cancel operation
  - `ResetConversation` (default: `Ctrl+Alt+R`) - Clear conversation history
  - Format: `"Modifier1+Modifier2+Key"` (e.g., `"Ctrl+Shift+Q"`)
  - Supported modifiers: `Ctrl`, `Alt`, `Shift`, `Win`
  - Invalid hotkeys cause startup failure with clear error message
  - See `HotkeyDefinitions.cs` for centralized defaults and descriptions
- `Audio`
  - Ducking settings (P0 mostly not implemented end-to-end)
  - `Earcons` (audio cues for state changes)
    - `Enabled` (master toggle)
    - `ListeningChimeEnabled` (play when STT starts)
    - `ListeningChimePath` (MP3/WAV file path, default `Assets/Sounds/tap-notification-357575.mp3`)
    - `ThinkingChimeEnabled` (play while waiting for model)
    - `ThinkingDelayMs` (delay before thinking chime, default 400ms)
    - `CancelChimeEnabled` (play on stop, default false)
    - `Volume` (0.0-1.0, default 0.12)
  - `FallbackMessagesByLanguage` + `GenericErrorMessageKey`
  - `CaptureFailedMessage`
- `ScreenCapture`
  - `DownscaleWidth` (used for model call + signature)
  - `SignatureHashAlgorithm` (e.g., `xxHash64`)
- `Logging.LogLevel.*`
- `InteractionLogging`
  - `LogFilePath` (JSONL)
  - `IncludeScreenshotsInDebug`

### 2) `audio_config.json` (Audio services - TTS, STT, and Language)
Template: `GameScreenTranslator/audio_config.template.json`  
Loaded by: `Core/Configuration/AudioConfigLoader.cs` into `Core/Configuration/AudioConfig.cs`

Key sections:
- `Language` - **Single source of truth for target language** (e.g., `"Hebrew"`, `"Russian"`, `"English"`)
  - Used by TTS for voice selection (via `VoiceByLanguage` mappings)
  - Passed to LLM prompts so responses are in the target language
- `Tts` - Text-to-Speech configuration
  - `Provider` - Which TTS service to use: `"AzureCognitive"` or `"AzureOpenAi"`
  - `AzureCognitive` - Azure Cognitive Services Speech
    - `Endpoint`, `ApiKey`, `Region`
    - `DefaultVoiceName` + `VoiceByLanguage`
  - `AzureOpenAi` - Azure OpenAI TTS (gpt-4o-mini-tts)
    - `Endpoint`, `ApiKey`, `Deployment`, `ApiVersion`
    - `DefaultVoice` + `VoiceByLanguage`
    - `Speed` (0.25-4.0, default 1.0)
    - `Instructions` (tone/style instructions, only for gpt-4o-mini-tts)
- `Stt` - Speech-to-Text configuration
  - `Provider` - Currently only `"AzureCognitive"` supported
  - `AzureCognitive`
    - `Endpoint`, `ApiKey`, `Region`
    - `DefaultRecognitionLanguage`
    - `CandidateLanguages` (for auto-detection)
    - `EnableAutoLanguageDetection`

### 3) Prompt pack (`configs/prompts.yaml`)
#### Why there are two files
- `configs/prompts.template.yaml`
  - "factory default" prompt pack
  - embedded into the EXE for first-run bootstrap
  - should be committed/updated when defaults change
- `configs/prompts.yaml`
  - runtime editable prompt pack
  - created from the template only if missing
  - intended for testers/parents to tweak without recompiling

#### Structure
Loaded by: `Core/Configuration/PromptPackLoader.cs` into `Core/Configuration/PromptPack.cs`

YAML keys (underscored) map to C# properties:
- `foundry.system` → `PromptPack.Foundry.System`
- `foundry.translate_user` → `PromptPack.Foundry.TranslateUser`
- `foundry.question_user` → `PromptPack.Foundry.QuestionUser`

Placeholders supported (string replace):
- `{windowTitle}`
- `{priority}`
- `{targetLanguage}`
- `{mode}`
- `{userQuestion}` (question flow)

Renderer: `Core/Configuration/PromptTemplate.Render()`

### 4) Sound assets (`Assets/Sounds/`)
Earcon audio files for state feedback:
- `tap-notification-357575.mp3` - default listening chime (plays when tap-to-talk starts)

Sound files are:
- Embedded in the EXE as resources
- Extracted on first run if missing
- Configurable via `Audio.Earcons.ListeningChimePath`

Supported formats: MP3, WAV (via NAudio library)

## Friends distribution package
For friends-only testing, use `scripts/publish-friends.ps1` to create a ZIP containing:
- `GameScreenTranslator.exe` (self-contained single-file)
- `appsettings.json` (with your LLM API keys)
- `audio_config.json` (with your TTS/STT API keys and language setting)
- `configs/prompts.yaml`
- `Assets/Sounds/tap-notification-357575.mp3`
- `QUICKSTART.txt` (setup instructions)

## Git / source control expectations
- Secrets (API keys) **must not be committed**.
- Repo typically commits:
  - `*.template.json` and `configs/prompts.template.yaml`
- Repo may or may not commit `configs/prompts.yaml` depending on desired workflow.
  - Current repo includes it for convenience; consider git-ignoring it if local prompt edits should not be committed.
- `dist/` directory (build outputs) should be gitignored.
