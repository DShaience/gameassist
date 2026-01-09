# Game Screen Translator - Implementation Plan

## Overview
This document provides a detailed technical implementation plan for the Game Screen Translator P0, a Windows console application that captures game screens, translates text, and speaks it aloud for children ages 5-10.

**Design update:** The system uses **multiple Azure services** (not a single model):
- **LLM / Vision**: Azure AI Foundry (chat + vision deployments)
- **TTS**: Azure Speech
- **STT**: Azure Speech

---

## Technology Stack

### Core Framework
- **.NET 10.0** (Console Application)
- **C# 13**
- **Target OS**: Windows 10/11 (x64)

### Key Dependencies

#### NuGet Packages
```xml
<!-- Core Windows APIs -->
<PackageReference Include="System.Drawing.Common" Version="8.0.0" />

<!-- Image Processing -->
<PackageReference Include="SixLabors.ImageSharp" Version="3.1.0" />

<!-- Audio -->
<PackageReference Include="NAudio" Version="2.2.1" />

<!-- HTTP & API -->
<PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />

<!-- Azure AI Foundry (LLM/Vision via Azure OpenAI-compatible endpoints) -->
<PackageReference Include="Azure.AI.OpenAI" Version="2.0.0" />

<!-- Azure Speech (TTS/STT) -->
<PackageReference Include="Microsoft.CognitiveServices.Speech" Version="1.45.0" />

<!-- Configuration -->
<PackageReference Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />

<!-- Logging -->
<PackageReference Include="Serilog" Version="3.1.1" />
<PackageReference Include="Serilog.Sinks.File" Version="5.0.0" />
<PackageReference Include="Serilog.Extensions.Logging" Version="8.0.0" />

<!-- Hashing -->
<PackageReference Include="System.IO.Hashing" Version="8.0.0" />
<!-- Note: For xxHash, use System.IO.Hashing.XxHash64 (built-in .NET 8+) -->

<!-- JSON -->
<PackageReference Include="System.Text.Json" Version="8.0.0" />
```

---

## Project Structure

```
GameScreenTranslator/
├── Program.cs                          # Entry point, dependency injection setup
├── appsettings.json                    # Main config (gitignored)
├── appsettings.template.json           # Main config template for users
├── speech_language_config.json         # Speech config (gitignored)
├── speech_language_config.template.json# Speech config template for users
├── GameScreenTranslator.csproj         # Project file
│
├── Core/
│   ├── Configuration/
│   │   ├── AppSettings.cs              # Strongly-typed config
│   │   └── HotkeyConfig.cs             # Hotkey definitions
│   │
│   ├── Capture/
│   │   ├── IWindowCapture.cs           # Interface
│   │   ├── WindowCapture.cs            # Win32 API implementation
│   │   └── WindowInfo.cs               # DTO for window data
│   │
│   ├── State/
│   │   ├── ScreenSignature.cs          # Screen signature generation
│   │   ├── ScreenStateManager.cs       # Manages cycle index & spoken history
│   │   └── SpokenHistoryEntry.cs       # DTO for tracking spoken content
│   │
│   ├── Models/
│   │   ├── IModelClient.cs             # Interface for LLM providers
│   │   ├── OpenAIModelClient.cs        # OpenAI implementation
│   │   ├── AzureOpenAIModelClient.cs   # Azure OpenAI implementation
│   │   ├── ModelClientFactory.cs       # Factory pattern for provider selection
│   │   └── Schemas/
│   │       ├── TranslationRequest.cs   # Input to model
│   │       ├── TranslationResponse.cs  # Model output schema
│   │       └── QuestionRequest.cs      # Voice question input
│   │
│   ├── Audio/
│   │   ├── IAudioPlayer.cs             # Interface
│   │   ├── AudioPlayer.cs              # NAudio-based TTS playback
│   │   ├── AudioDucker.cs              # Game audio ducking logic
│   │   └── SpeechToText.cs             # STT for voice questions
│   │
│   ├── Logging/
│   │   ├── InteractionLogger.cs        # JSONL structured logging
│   │   └── LogEntry.cs                 # DTO for log entries
│   │
│   └── Hotkeys/
│       ├── HotkeyManager.cs            # Win32 RegisterHotKey wrapper
│       └── HotkeyEventArgs.cs          # Event data
│
├── Services/
│   └── TranslationOrchestrator.cs      # Main business logic coordinator
│
└── Utils/
    ├── ImageDownscaler.cs              # Image resizing for signatures
    └── HashUtility.cs                  # xxHash implementation
```

---

## Configuration Schema

This project uses **two local config files**:

### 1) appsettings.json (main app config; gitignored)
```json
{
  "Providers": {
    "AzureFoundry": {
      "Endpoint": "https://YOUR-FOUNDRY-RESOURCE-NAME.YOUR-REGION.inference.ai.azure.com",
      "ApiKey": "YOUR_AZURE_FOUNDRY_API_KEY_HERE",
      "ApiVersion": "2024-10-21",
      "LLM": {
        "ChatDeployment": "YOUR_CHAT_DEPLOYMENT_NAME",
        "VisionDeployment": "YOUR_VISION_DEPLOYMENT_NAME",
        "RetryAttempts": 2,
        "TimeoutSeconds": 30
      }
    },
    "AzureSpeech": {
      "ConfigPath": "speech_language_config.json"
    }
  },
  "Translation": {
    "TargetLanguage": "Hebrew",
    "Mode": "explain"
  }
}
```

### 2) speech_language_config.json (speech config; gitignored)
```json
{
  "AzureSpeech": {
    "Endpoint": "https://YOUR-SPEECH-RESOURCE-NAME.cognitiveservices.azure.com/",
    "ApiKey": "YOUR_AZURE_SPEECH_API_KEY_HERE",
    "Region": "westus"
  },
  "TextToSpeech": {
    "DefaultVoiceName": "he-IL-HilaNeural",
    "VoiceByLanguage": {
      "he-IL": "he-IL-HilaNeural",
      "ru-RU": "ru-RU-SvetlanaNeural",
      "en-US": "en-US-JennyNeural"
    }
  },
  "SpeechToText": {
    "DefaultRecognitionLanguage": "he-IL",
    "CandidateLanguages": ["he-IL", "en-US", "ru-RU"],
    "EnableAutoLanguageDetection": true
  }
}
```

Notes:
- **LLM/Vision** and **Speech** credentials are intentionally separated.
- `speech_language_config.json` enables multi-language STT and per-language TTS voice selection.

---

## JSON Schema for Vision Model

### TranslationResponse.cs
```csharp
public class TranslationResponse
{
    public string GameName { get; set; }
    public SelectedRegion SelectedRegion { get; set; }
    public Translation Translation { get; set; }
}

public class SelectedRegion
{
    public string VerbatimText { get; set; }
    public BoundingBox BoundingBox { get; set; }
    public int PriorityLevel { get; set; }
    public double Confidence { get; set; }
}

public class BoundingBox
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public class Translation
{
    public string TargetLanguage { get; set; }
    public string TranslatedText { get; set; }
    public string SimplifiedText { get; set; }
    public string ExplainOneSentence { get; set; }
}
```

---

## Implementation Phases

### Phase 1: Project Setup & Configuration (Milestone 1)
**Goal**: Working console app with configuration loading for **multiple services** (LLM/Vision + Speech)

**Tasks**:
1. Create .NET console project
2. Add NuGet packages (incl. `Microsoft.CognitiveServices.Speech`)
3. Implement `AppSettings.cs` with configuration binding for:
   - `Providers.AzureFoundry` (LLM/Vision)
   - `Providers.AzureSpeech.ConfigPath` (speech config path)
4. Implement `SpeechLanguageConfig.cs` to load `speech_language_config.json`
5. Set up Serilog logging to JSONL
6. Create templates:
   - `appsettings.template.json`
   - `speech_language_config.template.json`
7. Add `.gitignore` rules for:
   - `appsettings.json`
   - `speech_language_config.json`

**Validation**: App starts, loads both configs, writes a test log entry (without secrets)

---

### Phase 2: Global Hotkeys (Milestone 1)
**Goal**: Register and respond to hotkeys

**Tasks**:
1. Implement `HotkeyManager.cs` using Win32 `RegisterHotKey`
2. Create message pump for hotkey events
3. Parse hotkey strings from config ("Ctrl+Alt+T" → VK codes)
4. Add event handlers for each hotkey
5. Implement graceful shutdown (unregister hotkeys)

**Key Win32 APIs**:
- `RegisterHotKey`
- `UnregisterHotKey`
- `GetMessage` / `DispatchMessage`

**Validation**: Press Ctrl+Alt+T → logs "Translate hotkey pressed"

---

### Phase 3: Window Capture (Milestone 2)
**Goal**: Capture active window screenshot

**Tasks**:
1. Implement `WindowCapture.cs`:
   - `GetForegroundWindow()` → get active window handle
   - `GetWindowText()` → get window title
   - `GetWindowThreadProcessId()` → get process name
   - `BitBlt()` → capture window bitmap
2. Convert bitmap to byte array
3. Implement `ImageDownscaler.cs`:
   - Use ImageSharp to resize to 640px width
   - Maintain aspect ratio
4. Implement `ScreenSignature.cs`:
   - Combine window title + downscaled image bytes
   - Compute xxHash64

**Validation**: Press hotkey → captures screenshot, logs signature

---

### Phase 4: Model Client Integration (Milestone 3)
**Goal**: Call Azure AI Foundry (Azure OpenAI-compatible) **vision + chat** endpoints with structured output

**Tasks**:
1. Implement `IModelClient` interface (single abstraction used by the app)
2. Implement `AzureFoundryModelClient.cs`:
   - Use `Azure.AI.OpenAI`
   - Configure endpoint + API key from `Providers.AzureFoundry`
   - Use chat/vision deployment names from `Providers.AzureFoundry.LLM`
   - Request structured JSON via response_format / JSON schema
3. (Optional future) Additional providers behind the same interface
4. Create prompt template from `vision_translate.yaml`
5. Parse JSON response into `TranslationResponse`
6. Implement retry logic with exponential backoff using `RetryAttempts`

Note:
- Even though this uses `Azure.AI.OpenAI`, the provider is **Azure Foundry-only** for now.

**API Call Pattern**:
```csharp
var chatClient = new ChatClient(model, apiKey);
var messages = new List<ChatMessage>
{
    new SystemChatMessage(systemPrompt),
    new UserChatMessage(
        ChatMessageContentPart.CreateTextPart(userPrompt),
        ChatMessageContentPart.CreateImagePart(imageBytes, "image/png")
    )
};

var options = new ChatCompletionOptions
{
    ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
        "TranslationResponse", 
        schema, 
        strictSchemaEnabled: true
    )
};

var response = await chatClient.CompleteChatAsync(messages, options);
```

**Validation**: Capture screenshot → API returns structured translation

---

### Phase 5: TTS & Audio Playback (Milestone 2)
**Goal**: Speak translated text aloud using Azure Speech

**Tasks**:
1. Implement `TextToSpeechService.cs`:
   - Use `Microsoft.CognitiveServices.Speech`
   - Select voice using `speech_language_config.json` (`VoiceByLanguage`)
   - Synthesize to an audio stream or byte buffer
2. Implement playback with NAudio (or let Speech SDK play directly; prefer NAudio for interruption control)
3. Implement interrupt functionality:
   - Stop current playback when new request arrives
4. Implement `AudioDucker.cs`:
   - Use NAudio to enumerate audio sessions
   - Find game process audio session
   - Gradually reduce volume to 25% over 300ms
   - Restore after TTS completes
5. Ensure language/voice selection supports Hebrew/Russian/English

**Validation**: Translation spoken aloud, game audio ducks during speech

**NAudio Pattern for Ducking**:
```csharp
using NAudio.CoreAudioApi;

var enumerator = new MMDeviceEnumerator();
var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

foreach (var session in device.AudioSessionManager.Sessions)
{
    if (session.GetProcessID == gameProcessId)
    {
        // Gradually lower volume
        session.SimpleAudioVolume.Volume = 0.25f;
    }
}
```

**Validation**: Translation spoken aloud, game audio ducks during speech

---

### Phase 6: Screen State & Cycling (Milestone 4)
**Goal**: Track screen state and cycle through UI priorities

**Tasks**:
1. Implement `ScreenStateManager.cs`:
   - Store current screen signature
   - Track cycle index per signature
   - Store hashes of already-spoken content (xxHash64)
2. On hotkey press:
   - Generate screen signature
   - If signature changed: reset cycle index to 0, clear spoken history
   - If signature same: increment cycle index
3. Pass cycle index to model prompt
4. After speaking: store hash of spoken text
5. Model prompt includes already-spoken hashes to avoid repetition

**Validation**: Multiple presses on same screen → cycles through different UI elements

---

### Phase 7: Voice Questions (Milestone 5)
**Goal**: Hold-to-talk voice questions using Azure Speech STT

**Tasks**:
1. Implement `SpeechToTextService.cs`:
   - Use `Microsoft.CognitiveServices.Speech`
   - Support:
     - single language (`DefaultRecognitionLanguage`)
     - optional auto language detection (`CandidateLanguages` + `EnableAutoLanguageDetection`)
2. Record audio while hotkey held (NAudio capture to WAV/PCM)
3. Modify hotkey handling:
   - Detect key down vs key up
   - Start recording on key down
   - Stop and transcribe on key up
4. Send screenshot + transcribed question to Azure Foundry model
5. Use `vision_question.yaml` prompt template
6. Speak answer aloud via Azure Speech TTS

**Validation**: Hold Ctrl+Alt+Q → ask question → hear answer

---

### Phase 8: Logging & Observability (Milestone 6)
**Goal**: Structured JSONL logging for analysis

**Tasks**:
1. Implement `InteractionLogger.cs`:
   - Log each interaction to JSONL
2. Include fields:
   - Timestamp (ISO 8601)
   - Window title & process name
   - Screen signature (hash)
   - Cycle index
   - Verbatim text & bounding box
   - Translated text
   - Spoken text (simplified or explained)
   - Latency metrics (API call, TTS generation, total time)
   - Screenshot (base64) only if log level = Debug
3. Respect privacy levels via log levels

**Log Entry Example**:
```json
{
  "timestamp": "2026-01-04T00:30:00Z",
  "window_title": "Minecraft",
  "process_name": "javaw.exe",
  "screen_signature": "a1b2c3d4e5f6...",
  "cycle_index": 0,
  "selected_region": {
    "verbatim_text": "Create New World",
    "bounding_box": { "x": 0.4, "y": 0.5, "width": 0.2, "height": 0.08 },
    "priority_level": 1
  },
  "translation": {
    "target_language": "Hebrew",
    "translated_text": "צור עולם חדש",
    "spoken_text": "צור עולם חדש - זה הכפתור שיוצר משחק חדש"
  },
  "latency_ms": {
    "capture": 45,
    "api_call": 1230,
    "tts_generation": 890,
    "total": 2165
  }
}
```

**Validation**: Each interaction logged to file, parseable as JSONL

---

### Phase 9: Error Handling & Polish (Milestone 7)
**Goal**: Robust error handling and user feedback

**Tasks**:
1. Implement retry logic for API failures (configurable)
2. Add fallback TTS message: "Sorry, I couldn't connect right now"
3. Handle edge cases:
   - No active window
   - Window too small to capture
   - API rate limits
   - Invalid JSON from model
4. Add console logging for debugging (user-friendly messages)
5. Create README with setup instructions
6. Add system tray icon (optional):
   - Show app is running
   - Right-click menu: Exit, View Logs, Settings

**Validation**: Disconnect internet → retry → fallback message spoken

---

## Key Technical Challenges & Solutions

### Challenge 1: Global Hotkeys in Console App
**Solution**: Use Win32 message pump in console app thread:
```csharp
while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
{
    if (msg.message == WM_HOTKEY)
    {
        // Handle hotkey
    }
    DispatchMessage(ref msg);
}
```

### Challenge 2: Audio Ducking Without Admin
**Solution**: NAudio's `SimpleAudioVolume` works per-session without elevation. Only requires same user context.

### Challenge 3: Hebrew/Russian TTS Quality
**Solution**: Use Azure Speech Neural voices. Select per-language voice via `speech_language_config.json` (`VoiceByLanguage`). This keeps the app multilingual and avoids changing code when switching voices.

### Challenge 4: Structured Output from Vision Models
**Solution**: Use `response_format` with JSON Schema (available in GPT-4o and later). Enforces structure without prompt engineering.

### Challenge 5: Fast Cycle Detection
**Solution**: xxHash64 is extremely fast (~GB/s). Downscaling to 640px keeps images small (~100KB). Total signature time: <50ms.

---

## Testing Strategy (P0)

### Manual Testing Checklist
1. **Hotkey Registration**:
   - [ ] App starts without errors
   - [ ] Hotkeys respond (log messages)
   - [ ] Unregisters hotkeys on exit

2. **Window Capture**:
   - [ ] Captures active game window
   - [ ] Works with fullscreen games
   - [ ] Works with windowed games
   - [ ] Handles multi-monitor setups

3. **Translation Quality**:
   - [ ] Identifies correct text region
   - [ ] Translation is accurate
   - [ ] Explanation is child-friendly
   - [ ] Hebrew/Russian output is correct

4. **Cycling Logic**:
   - [ ] First press: most important element
   - [ ] Second press: different element
   - [ ] No repetition of already-spoken content
   - [ ] Resets when screen changes

5. **Audio**:
   - [ ] TTS sounds natural
   - [ ] Game audio ducks smoothly
   - [ ] Playback can be interrupted
   - [ ] Volume restores after speaking

6. **Voice Questions**:
   - [ ] Records while key held
   - [ ] Transcribes correctly
   - [ ] Answers are relevant
   - [ ] Says "I can't see that" when appropriate

7. **Logging**:
   - [ ] All interactions logged
   - [ ] JSONL format valid
   - [ ] Screenshots logged only in Debug mode
   - [ ] No sensitive data in Info logs

### Test Games
Start with games with clear UI text:
- Minecraft (menus, inventory)
- Stardew Valley (dialog boxes)
- Terraria (tooltips, menus)

---

## Deployment (P0)

### Build Command
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

### Output
- Single executable: `GameScreenTranslator.exe` (~120-150 MB)
- Includes .NET runtime
- No installation required
- User just needs to:
  1. Download .exe
  2. Create `appsettings.json` from template
  3. Add API key
  4. Run

---

## Future Enhancements (Post-P0)

1. **Settings UI**: WPF window for configuration (no manual JSON editing)
2. **Game-Specific Prompts**: Detect game and load custom prompt pack
3. **Translation Caching**: Cache translations by screen signature
4. **Debug Overlay**: Show bounding boxes on screen (optional)
5. **Terminology Memory**: Learn game-specific terms across sessions
6. **Multi-Language Support**: Add more target languages
7. **Cloud Logging**: Opt-in upload for improvement (with privacy controls)
8. **Auto-Update**: Check for new versions
9. **Performance Optimization**: Reduce API calls with smarter caching

---

## Estimated Development Time

| Phase | Tasks | Estimated Hours |
|-------|-------|-----------------|
| Phase 1: Setup | Project, config, logging | 2-3h |
| Phase 2: Hotkeys | Win32 integration, events | 3-4h |
| Phase 3: Capture | Screenshot, downscaling | 4-5h |
| Phase 4: Model Client | OpenAI/Azure, retry logic | 6-8h |
| Phase 5: Audio | TTS, ducking, playback | 5-6h |
| Phase 6: Cycling | State management, signatures | 3-4h |
| Phase 7: Voice Questions | STT, hold-to-talk | 3-4h |
| Phase 8: Logging | JSONL, structured data | 2-3h |
| Phase 9: Polish | Error handling, README | 2-3h |
| **Total** | | **30-40 hours** |

---

## Success Criteria

✅ **P0 is complete when**:
1. User can press hotkey to hear translation of game screen
2. Repeated presses cycle through different UI elements
3. Hold-to-talk voice questions work
4. Game audio ducks during speech
5. All interactions logged to JSONL
6. Works with Azure Foundry (LLM/Vision) and Azure Speech (TTS/STT)
7. Supports Hebrew and Russian output (and is extensible to more languages)
8. Handles API errors gracefully
9. Single-file deployment works

---

## Next Steps

1. **Review this plan** with stakeholders
2. **Toggle to Act Mode** to begin implementation
3. **Start with Phase 1**: Create project structure
4. **Iterate through phases** sequentially
5. **Test after each phase** before proceeding
6. **Document learnings** for future enhancements

---

*This implementation plan is a living document. Update as technical decisions change or new requirements emerge.*
