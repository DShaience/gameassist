# Runtime layout & Configuration (P0)
Last updated: 2026-01-10

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
- `speech_language_config.json` from embedded `speech_language_config.template.json`
- `configs/prompts.yaml` from embedded `prompts.template.yaml`

This enables a “drop the EXE into a folder and run” experience.

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
  - defaults to `speech_language_config.json`
- `Translation`
  - `TargetLanguage` (e.g., `"Hebrew"`, `"Russian"`)
  - `Mode` (`realistic|simplified|explain`)
- `Hotkeys`
  - `Translate`, `AskQuestion`, `ToggleMode`, `StopSpeaking`
  - Note: not all keys are currently wired in hotkey runtime (see `40_hotkeys_and_interaction.md`)
- `Audio`
  - Ducking settings (P0 mostly not implemented end-to-end)
  - `FallbackMessagesByLanguage` + `GenericErrorMessageKey`
  - `CaptureFailedMessage`
- `ScreenCapture`
  - `DownscaleWidth` (used for model call + signature)
  - `SignatureHashAlgorithm` (e.g., `xxHash64`)
- `Logging.LogLevel.*`
- `InteractionLogging`
  - `LogFilePath` (JSONL)
  - `IncludeScreenshotsInDebug`

### 2) `speech_language_config.json` (Azure Speech settings + voices)
Template: `GameScreenTranslator/speech_language_config.template.json`  
Loaded by: `Core/Configuration/SpeechLanguageConfigLoader.cs` into `Core/Configuration/SpeechLanguageConfig.cs`

Key sections:
- `AzureSpeech.Endpoint`, `ApiKey`, `Region`
- `TextToSpeech.DefaultVoiceName` + `VoiceByLanguage`
- `SpeechToText.DefaultRecognitionLanguage`
- `SpeechToText.CandidateLanguages`
- `SpeechToText.EnableAutoLanguageDetection`

### 3) Prompt pack (`configs/prompts.yaml`)
#### Why there are two files
- `configs/prompts.template.yaml`
  - “factory default” prompt pack
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

## Git / source control expectations
- Secrets (API keys) **must not be committed**.
- Repo typically commits:
  - `*.template.json` and `configs/prompts.template.yaml`
- Repo may or may not commit `configs/prompts.yaml` depending on desired workflow.
  - Current repo includes it for convenience; consider git-ignoring it if local prompt edits should not be committed.
