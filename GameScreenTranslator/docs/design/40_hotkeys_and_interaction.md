# Hotkeys & Interaction Model (P0)
Last updated: 2026-01-13

## Sources of truth
- Runtime hotkey implementation: `Core/App/HotkeyAppRunner.cs`
- Win32 hotkey registration: `Core/Hotkeys/Win32HotkeyManager.cs`
- Earcon playback: `Core/Audio/EarconPlayer.cs`
- Config parsing utilities (not fully wired): `Core/Configuration/HotkeyConfig.cs`
- Config template: `appsettings.template.json`

## Important note on configuration vs implementation (current state)
`HotkeyAppRunner` currently registers **hard-coded** hotkeys (`Ctrl+Alt+T/W/X/R`) rather than reading from config.

PRD expectation:
- If hotkeys become configurable, update this doc and ensure `HotkeyAppRunner` uses `HotkeyConfig.Parse()` + config values.

## Hotkeys (implemented)
- **Translate/Cycle**: `Ctrl+Alt+T`
  - Flow: capture active window → compute signature/cycle priority → model call → speak → log
  - No earcons (silent operation)
- **Stop Speaking / Cancel**: `Ctrl+Alt+X`
  - Cancels current operation and stops TTS playback
  - Plays cancel chime (if `Audio.Earcons.CancelChimeEnabled`, default off)
- **Tap-to-talk test**: `Ctrl+Alt+W`
  - Play listening earcon immediately (if `Audio.Earcons.ListeningChimeEnabled`)
  - Capture screenshot immediately
  - Run "recognize once" STT with an end-silence timeout (~1500ms)
  - Start thinking chime timer (fires after `ThinkingDelayMs` if model hasn't responded)
  - Send screenshot + question (+ conversation context when enabled) to model
  - Cancel thinking chime timer on model response
  - Speak response → log
- **Reset conversation**: `Ctrl+Alt+R`
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
