# Hotkeys & Interaction Model (P0)
Last updated: 2026-01-10

## Sources of truth
- Runtime hotkey implementation: `Core/App/HotkeyAppRunner.cs`
- Win32 hotkey registration: `Core/Hotkeys/Win32HotkeyManager.cs`
- Low-level hook for “hold” behavior: `Core/Hotkeys/GlobalKeyboardHook.cs`
- Config parsing utilities (not fully wired): `Core/Configuration/HotkeyConfig.cs`
- Config template: `appsettings.template.json` (`Hotkeys.*`)

## Important note on configuration vs implementation (current state)
`appsettings.json` contains a `Hotkeys` section, but `HotkeyAppRunner` currently registers **hard-coded** hotkeys (`Ctrl+Alt+T/Q/W/X`) rather than reading from config.

PRD expectation:
- If hotkeys become configurable, update this doc and ensure `HotkeyAppRunner` uses `HotkeyConfig.Parse()` + config values.

## Hotkeys (implemented)
- **Translate/Cycle**: `Ctrl+Alt+T`
  - Flow: capture active window → compute signature/cycle priority → model call → speak → log
- **Stop Speaking / Cancel**: `Ctrl+Alt+X`
  - Cancels current operation and stops TTS playback
- **Hold-to-talk question**: `Ctrl+Alt+Q` (hold)
  - On key-down:
    - Capture screenshot immediately (to prevent focus change)
    - Start continuous STT
  - On key-up:
    - Stop STT → get transcribed question
    - If no question, exit
    - Send screenshot + question to model → speak → log
- **Tap-to-talk test**: `Ctrl+Alt+W`
  - Capture screenshot immediately
  - Run “recognize once” STT with an end-silence timeout (~1500ms)
  - Send screenshot + question to model → speak → log

## Interaction state machine (simplified)
Shared state:
- `_operationCts`: cancellation token source for the current in-flight operation (capture/model/tts/stt).
- `_isRecordingQuestion`: guard for hold-to-talk to avoid re-entrancy.
- `_questionSnapshot`: stores the captured window screenshot for the hold-to-talk flow.

Rules:
- New hotkey action begins by calling `CancelCurrentOperation()`.
- Each action allocates a new CTS via `NewOperationCts()` and passes `cts.Token` down where supported.
- Stop hotkey:
  - Cancels CTS
  - Calls `_tts.StopAsync()`
  - Logs a “stop” interaction entry.

## Priority cycling model
Triggered by repeated `Translate/Cycle` on the same screen signature:
- `ScreenSignature.Compute(windowTitle, screenshot)` creates a stable signature for “same screen”.
- `ScreenStateManager` keeps:
  - current signature
  - cycle index per signature
  - a set of “spoken keys” to avoid repeating the same text

`PriorityCycler.GetPriorityForCycleIndex(cycleIndex)` maps cycle index → `UiPriority`.

The `UiPriority` is passed to the model and used to guide “what part of the UI to focus on”.

## Speak text composition
`HotkeyAppRunner.BuildSpeakText(response)`:
- Prefer `TranslatedText`
- If `Explanation` exists, speak: `${translated}. ${explanation}`
- If translation is empty, fallback to explanation or source text
- Ultimate fallback (Hebrew): “לא הצלחתי למצוא טקסט קריא במסך הזה.”

## Error behavior
- Any exception in a hotkey flow should:
  - be printed to console (including inner exception for Foundry HTTP)
  - trigger a short fallback TTS message based on settings (`FallbackSpeech.ResolveGenericErrorMessage(...)`)
  - be logged into JSONL (see `60_logging_and_privacy.md`)
