# Model & Audio Design (P0)
Last updated: 2026-01-10

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
- `{windowTitle}`, `{priority}`, `{targetLanguage}`, `{mode}`, `{userQuestion}`

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

## Audio: Azure Speech
### Source of truth
- TTS: `Core/Audio/AzureSpeechTtsService.cs`
- STT: `Core/Audio/AzureSpeechSttService.cs` (see also `speech_language_config.json`)
- Fallback messages: `Core/App/FallbackSpeech.cs` + `appsettings.json` `Audio.*`

### TTS behavior (P0)
- `SpeakAsync(text, languageOrLocale)`
  - Resolves voice name via `speech_language_config.json`:
    - exact locale mapping (`he-IL`, `ru-RU`, `en-US`, etc.)
    - also supports language labels (`Hebrew`, `Russian`, `English`) via simple mapping
  - Uses `SpeechSynthesizer.SpeakTextAsync(text)`
- `StopAsync()` interrupts current speech
- Concurrency control: a `SemaphoreSlim` gate ensures one TTS operation at a time

### STT behavior (P0)
Two interaction styles:
1. **Hold-to-talk** (`Ctrl+Alt+Q`)
   - Start continuous STT on key-down
   - Stop and return recognized text on key-up
2. **Tap-to-talk** (`Ctrl+Alt+W`)
   - Recognize once with an end-silence timeout (default used: ~1500ms)

Language handling (expected):
- Candidate languages and optional auto language detection configured in `speech_language_config.json`.

### Ducking (planned)
`appsettings.json` contains `Audio.DuckingEnabled` and volume parameters. End-to-end ducking of game audio is not a core requirement for current P0 implementation and may not be implemented fully.

### Error behavior
- TTS/STT failures should throw exceptions that are caught in the hotkey runner.
- On failure, app attempts to speak a short fallback message:
  - `Audio.FallbackMessagesByLanguage` keyed by target language name
  - fallback key via `Audio.GenericErrorMessageKey`
