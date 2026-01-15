# Hotkeys & Interaction Model (P0)
Last updated: 2026-01-15

## Sources of truth
- Centralized hotkey definitions: `Core/Configuration/HotkeyDefinitions.cs`
- Hotkey config settings: `Core/Configuration/AppSettings.cs` (`HotkeysSettings`)
- Runtime hotkey implementation: `Core/App/HotkeyAppRunner.cs`
- Win32 hotkey registration: `Core/Hotkeys/Win32HotkeyManager.cs`
- Hotkey string parsing: `Core/Configuration/HotkeyConfig.cs`
- Earcon playback: `Core/Audio/EarconPlayer.cs`
- Config template: `appsettings.template.json`

## Hotkey configuration
As of 2026-01-15, all hotkeys are fully configurable via `appsettings.json`:

```json
"Hotkeys": {
  "Translate": "Ctrl+Alt+T",
  "Ask": "Ctrl+Alt+W",
  "Stop": "Ctrl+Alt+X",
  "ResetConversation": "Ctrl+Alt+R"
}
```

### Format
- Modifiers: `Ctrl`, `Alt`, `Shift`, `Win` (or `Control`, `Windows`)
- Keys: Single letters (`A`-`Z`), numbers (`0`-`9`), function keys (`F1`-`F12`), or special keys (`Space`, `Enter`, `Escape`, `Tab`)
- Separator: `+`
- Example: `"Ctrl+Shift+F5"`

### Centralized definitions
`HotkeyDefinitions.cs` provides:
- Default key combinations
- Human-readable names for logging
- Descriptions for documentation

All log messages and error messages use these centralized definitions.

### Fallback behavior
If a hotkey is not specified in config (or config section is missing), the app falls back to defaults from `HotkeyDefinitions`.

### Error handling
- Invalid hotkey string → app fails to start with clear error message
- Hotkey already in use by another app → warning logged, hotkey not registered

## Hotkeys (implemented)
- **Translate/Cycle**: Default `Ctrl+Alt+T` (configurable as `Hotkeys.Translate`)
  - Flow: capture active window → compute signature/cycle priority → model call → speak → log
  - No earcons (silent operation)
- **Stop Speaking / Cancel**: Default `Ctrl+Alt+X` (configurable as `Hotkeys.Stop`)
  - Cancels current operation and stops TTS playback
  - Plays cancel chime (if `Audio.Earcons.CancelChimeEnabled`, default off)
- **Tap-to-talk (Ask)**: Default `Ctrl+Alt+W` (configurable as `Hotkeys.Ask`)
  - Play listening earcon immediately (if `Audio.Earcons.ListeningChimeEnabled`)
  - Capture screenshot immediately
  - Run "recognize once" STT with an end-silence timeout (~1500ms)
  - Start thinking chime timer (fires after `ThinkingDelayMs` if model hasn't responded)
  - Send screenshot + question (+ conversation context when enabled) to model
  - Cancel thinking chime timer on model response
  - Speak response → log
- **Reset conversation**: Default `Ctrl+Alt+R` (configurable as `Hotkeys.ResetConversation`)
  - Clears in-memory conversation history and rolling summary
  - Speaks a short confirmation and logs `conversation_reset`

## Earcons (audio cues)
Non-speech audio feedback to indicate state changes. Configured via `Audio.Earcons` in `appsettings.json`.

| Earcon | When | Configurable | Default |
|--------|------|--------------|---------|
| Listening chime | Tap-to-talk hotkey pressed, before STT starts | `ListeningChimeEnabled`, `ListeningChimePath` | On, `Assets/Sounds/tap-notification-357575.mp3` |
| Thinking chime | After STT ends, while waiting for model (delayed) | `ThinkingChimeEnabled`, `ThinkingDelayMs` | On, 400ms delay |
| Cancel chime | Stop hotkey pressed | `CancelChimeEnabled` | Off |

Implementation:
- `EarconPlayer.PlayListeningChime()` - plays MP3/WAV file via NAudio
- `EarconPlayer.PlayThinkingChime()` - plays synthetic dual-tone (880Hz → 1320Hz)
- `EarconPlayer.PlayCancelChime()` - plays synthetic low dual-tone (220Hz → 165Hz)

All earcons are fire-and-forget (non-blocking) and best-effort (failures are logged but don't crash the app).

### Tap-to-talk timing diagram
```
[Hotkey Press]
    │
    ▼
[Play Listening Earcon] ──► Fire-and-forget (non-blocking)
    │
    ▼
[Capture Screenshot]
    │
    ▼
[Start STT Recording] ──► User speaks
    │
    ▼
[STT Ends (silence detected)]
    │
    ├─── Start thinking delay timer (400ms default)
    │
    ▼
[Model Request In Flight]
    │
    ├─── If timer elapses before response: play thinking chime
    │
    ▼
[Model Response Received] ──► Cancel thinking timer
    │
    ▼
[TTS Speaks Response]
```

## Interaction state machine (simplified)
Shared state:
- `_operationCts`: cancellation token source for the current in-flight operation (capture/model/tts/stt).
- Conversation state (in-memory):
  - `_conversationStore`: ordered list of user/assistant turns
  - `_conversationContextBuilder`: builds request messages (rolling summary + recent turns + current screenshot)

Rules:
- New hotkey action begins by calling `CancelCurrentOperation()`.
- Each action allocates a new CTS via `NewOperationCts()` and passes `cts.Token` down where supported.
- Stop hotkey:
  - Cancels CTS
  - Calls `_tts.StopAsync()`
  - Plays cancel chime (if enabled)
  - Logs a "stop" interaction entry.

## Priority cycling model
Triggered by repeated `Translate/Cycle` on the same screen signature:
- `ScreenSignature.Compute(windowTitle, screenshot)` creates a stable signature for "same screen".
- `ScreenStateManager` keeps:
  - current signature
  - cycle index per signature
  - a set of "spoken keys" to avoid repeating the same text

`PriorityCycler.GetPriorityForCycleIndex(cycleIndex)` maps cycle index → `UiPriority`.

The `UiPriority` is passed to the model and used to guide "what part of the UI to focus on".

## Speak text composition
`HotkeyAppRunner.BuildSpeakText(response)`:
- Prefer `TranslatedText`
- If `Explanation` exists, speak: `${translated}. ${explanation}`
- If translation is empty, fallback to explanation or source text
- Ultimate fallback (Hebrew): "לא הצלחתי למצוא טקסט קריא במסך הזה."

## Error behavior
- Any exception in a hotkey flow should:
  - be printed to console (including inner exception for Foundry HTTP)
  - trigger a short fallback TTS message based on settings (`FallbackSpeech.ResolveGenericErrorMessage(...)`)
  - be logged into JSONL (see `60_logging_and_privacy.md`)
