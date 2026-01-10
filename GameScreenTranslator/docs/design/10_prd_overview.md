# PRD — Overview (P0)
Last updated: 2026-01-10

## Problem statement
Many children (ages 5–10) play games that do not offer translations (e.g., Hebrew/Russian). They need a low-friction way to understand what is on screen without game integration.

## Target users
- Primary: child (5–10) using a Windows PC to play games
- Secondary: parent/tester configuring the tool and prompts

## Goals (P0)
- Windows-only desktop assistant.
- Hotkey-based interaction (no mouse region selection).
- Capture the active window only, including window title and process info.
- Vision-capable model call with structured JSON output.
- Spoken output via Azure Speech TTS.
- STT for spoken questions (hold-to-talk + tap-to-talk test).
- Priority cycling on repeated presses for the same “screen”.
- Local structured logs (JSONL) with privacy controls.

## Non-goals (P0)
- Game-specific integration (no memory reading / no hooks).
- Overlays, in-game UI, or a polished GUI (tray icon only).
- Console support.
- Perfect OCR/detection for every game UI.
- Long-term terminology consistency across sessions (beyond simple cycle state).

## Key user experience
### Primary hotkeys (implemented in `Core/App/HotkeyAppRunner.cs`)
- `Ctrl+Alt+T`: Translate/Cycle (capture → model → speak)
- `Ctrl+Alt+Q` (hold): Hold-to-talk question (capture at key-down → record STT while held → answer → speak)
- `Ctrl+Alt+W`: Tap-to-talk test (capture → record STT until silence → answer → speak)
- `Ctrl+Alt+X`: Stop speaking / cancel current operation

### Modes (configured via `appsettings.json`)
- `Translation.Mode`: `realistic` | `simplified` | `explain` (default is `explain`)

Current prompt behavior:
- If `userQuestion` is present, the client uses the “question” prompt template.
- Otherwise it uses the “translate/cycle” prompt template.

## External services (P0)
- Azure AI Foundry (OpenAI-compatible) for the vision LLM call.
- Azure Speech for:
  - TTS (speak response)
  - STT (speech-to-text for questions)

## Success criteria (P0)
- “Time to first spoken audio” is acceptable for real use.
- Capture is stable across common games, including cases where the OS is screen-recording.
- Translation/cycle is subjectively helpful on a small set of games.
- Logs are produced locally and are useful for debugging.

## Out of scope but planned (post-P0)
- Better UI/overlay for highlighting selected regions
- Per-game prompt packs
- Caching translations across sessions
- Opt-in upload tiers for diagnostics
