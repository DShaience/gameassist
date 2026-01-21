# Model & Audio Design (P0)
Last updated: 2026-01-21

## Model: Azure AI Foundry (vision LLM)
### Source of truth
- Client: `Core/Foundry/AzureFoundryClient.cs`
- Response model: `Core/Models/TranslateCycleResponse.cs`
- Prompt pack: `configs/prompts.yaml` (bootstrapped from `configs/prompts.template.yaml`)

### API style
Azure Foundry “model inference” endpoint is OpenAI-compatible:
- `POST /openai/deployments/{deployment}/chat/completions?api-version=...`

Authentication:
- `api-key: <key>` header
- base URL from `appsettings.json` → `Providers.AzureFoundry.Endpoint`

Deployments:
- Vision calls use `Providers.AzureFoundry.LLM.VisionDeployment`
- (ChatDeployment exists in config; currently the client uses vision deployment for the main flow.)

Timeouts/retries:
- HTTP client timeout: `Providers.AzureFoundry.LLM.TimeoutSeconds`
- Retries: `Providers.AzureFoundry.LLM.RetryAttempts` + initial attempt

### Request composition
For each interaction:
1. Downscale screenshot to a configured width (default 640, override via `ScreenCapture.DownscaleWidth`).
2. Encode PNG bytes and send as `image_url` using a base64 data URL.

3. **Translate/Cycle (non-conversational)** sends 2 messages:
   - `system`: prompt text from YAML (`foundry.system`)
   - `user`: rendered template (`foundry.translate_user`) + image content

4. **Question flows (conversational, when enabled)** send a message list built by `Core/Conversation/ConversationContextBuilder`:
   - `system`: prompt text from YAML (`foundry.system`)
   - optional `system`: `[Previous conversation summary: ...]` (rolling summary of older turns)
   - recent conversation turns verbatim (text-only; no historical images)
   - current `user`: rendered template (`foundry.question_user`) + current screenshot image content

Summarization:
- Older turns can be summarized using the same Foundry deployment via `AzureFoundryClient.GenerateSummaryAsync(...)` (text-only request).
- If summarization fails, the builder falls back to truncation.

Temperature/tokens:
- Starts with `temperature = 0.2`
- Sets `max_tokens = 800` initially
- Includes `response_format: { type: "json_object" }` initially

Compatibility auto-fallback:
If Foundry returns HTTP 400 with certain “unsupported parameter” messages, the client retries after adjusting:
- Replace `max_tokens` with `max_completion_tokens`
- Remove `response_format`
- Omit `temperature` (some models only accept default)

### Prompt selection
`BuildUserPrompt(...)` chooses template:
- If `userQuestion` is empty → `foundry.translate_user`
- Else → `foundry.question_user`

Supported template placeholders:
- `{windowTitle}`, `{priority}`, `{targetLanguage}`, `{userQuestion}`
- Note: `{mode}` is still in templates for backward compatibility but always resolves to "explain"

### Expected model output contract
`AzureFoundryClient` expects a JSON object that deserializes into `TranslateCycleResponse`.
Fields used by the app:
- `selectedLabel` (string)
- `sourceText` (string)
- `translatedText` (string)
- `explanation` (optional string)
- `boundingBox` (optional with `x,y,width,height`)

Normalization:
- Trims and cleans quotes/backticks
- Attempts to clean “key:value” fragments into readable speech

## Audio Services
### Source of truth
- Config: `audio_config.json` (loaded by `Core/Configuration/AudioConfigLoader.cs`)
- TTS Factory: `Core/Audio/TtsServiceFactory.cs`
- STT Factory: `Core/Audio/SttServiceFactory.cs`
- TTS Implementations:
  - `Core/Audio/AzureCognitiveTtsService.cs` (Azure Cognitive Services Speech)
  - `Core/Audio/AzureOpenAiTtsService.cs` (Azure OpenAI TTS - gpt-4o-mini-tts)
- STT Implementation: `Core/Audio/AzureCognitiveSttService.cs`
- Fallback messages: `Core/App/FallbackSpeech.cs` + `appsettings.json` `Audio.*`

### Language Configuration
Target language is set in `audio_config.json`:
- `Language` field (e.g., `"Hebrew"`, `"Russian"`, `"English"`)
- This is the **single source of truth** for:
  - TTS voice selection (via `VoiceByLanguage` mappings)
  - LLM response language (passed to prompts as `{targetLanguage}`)

### TTS Providers
The app supports two TTS providers, selectable via `Tts.Provider` in `audio_config.json`:

#### Azure Cognitive Services (`"AzureCognitive"`)
- Uses Azure Cognitive Services Speech SDK
- Voice selection via `Tts.AzureCognitive.VoiceByLanguage`
- Supports neural voices (e.g., `he-IL-HilaNeural`)

#### Azure OpenAI TTS (`"AzureOpenAi"`)
- Uses Azure OpenAI REST API
- Supports `tts-1`, `tts-1-hd`, and `gpt-4o-mini-tts` models
- Voice selection via `Tts.AzureOpenAi.VoiceByLanguage`
- Additional features:
  - `Speed` (0.25-4.0, default 1.0)
  - `Instructions` (tone/style instructions, only for gpt-4o-mini-tts)

### TTS behavior (P0)
- `SpeakAsync(text, languageOrLocale)`
  - Resolves voice name via `VoiceByLanguage` mapping in config
  - Supports both locale codes (`he-IL`) and language names (`Hebrew`)
- `StopAsync()` interrupts current speech
- Concurrency control: a `SemaphoreSlim` gate ensures one TTS operation at a time

### STT behavior (P0)
Currently only Azure Cognitive Services STT is supported.

Interaction style:
- **Tap-to-talk** (`Ctrl+Alt+W`)
  - Recognize once with an end-silence timeout (default used: ~1500ms)

Language handling:
- `Stt.AzureCognitive.DefaultRecognitionLanguage` - primary language
- `Stt.AzureCognitive.CandidateLanguages` - languages for auto-detection
- `Stt.AzureCognitive.EnableAutoLanguageDetection` - enable multi-language detection

### Ducking (planned)
`appsettings.json` contains `Audio.DuckingEnabled` and volume parameters. End-to-end ducking of game audio is not a core requirement for current P0 implementation and may not be implemented fully.

### Error behavior
- TTS/STT failures should throw exceptions that are caught in the hotkey runner.
- On failure, app attempts to speak a short fallback message:
  - `Audio.FallbackMessagesByLanguage` keyed by target language name
  - fallback key via `Audio.GenericErrorMessageKey`
