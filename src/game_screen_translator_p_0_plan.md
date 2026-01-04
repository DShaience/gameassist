# Game Screen Translator (P0)

## Overview
A Windows desktop assistant that helps children (ages 5–10) access games that lack native translations.

Interaction model:
- Press a hotkey → the app captures the active game window, uses a vision-capable language model to identify the most relevant on-screen text, translates and optionally explains it, and speaks it aloud.
- Repeated presses on the same screen cycle through other important UI regions.
- Holding a second hotkey allows the child or parent to ask a spoken question about what’s on screen.

The P0 focuses on **zero game integration**, **cloud-based models**, **text-to-speech**, and **low-friction UX**.

---

## Goals (P0)
- Windows-only support.
- Hotkey-based interaction (no mouse region selection).
- One model call per request (vision + translation + explanation).
- Voice output (Hebrew / Russian).
- Local logging, with future opt-in uploads.

## Non-goals (P0)
- Perfect detection for every game.
- Overlays or polished UI.
- Console support.
- Full terminology consistency across sessions.

---

## User Experience

### Hotkeys (example)
- **Translate / Cycle**: `Ctrl + Alt + T`
- **Ask a Question (hold)**: `Ctrl + Alt + Q`
- **Toggle Mode**: `Ctrl + Alt + S`
- **Stop Speaking**: `Ctrl + Alt + X`

### Modes
- **realistic** – faithful translation
- **simplified** – child-friendly phrasing
- **explain (default)** – translation plus one short explanatory sentence

### Audio Behavior
- Spoken output via TTS.
- Duck game audio while speaking.
- Interrupt current speech if a new request is triggered.

---

## Technical Approach

### Technology Stack
- **.NET 10.0 Console Application** (C#)
- **Deployment**: Single-file self-contained executable (~120-150 MB)
- **No installation required**: Just download, configure API key, and run
- **Runs as regular user**: No admin privileges needed

### LLM Providers
- Support for both **OpenAI** and **Azure OpenAI**
- Modular design for easy expansion to other providers
- Structured JSON output enforced via response schemas

### Architecture

#### Components
1. **Window Capture**
   - Capture the active window only using Win32 APIs
   - Collect window title and process name
   - Works with fullscreen and windowed games

2. **Screen State Manager**
   - Generate a screen signature from the window title and a downscaled screenshot
   - Track a cycle index per signature
   - Maintain history of already-spoken content (to avoid repetition)

3. **Model Client**
   - Single vision-capable LLM call with structured JSON output
   - OpenAI TTS API for natural voice synthesis
   - OpenAI Whisper for speech-to-text (voice questions)

4. **Audio Playback**
   - NAudio-based TTS playback with streaming support
   - Interrupt current speech when new request arrives
   - Gradual audio ducking (reduces game volume to 25% while speaking)

5. **Logger**
   - Writes structured JSONL logs locally
   - Privacy-aware: screenshots only logged at Debug level

---

## Screen Signature
- Downscale screenshot to **640px width** (maintains text readability)
- Maintain aspect ratio
- Compute:
  ```
  signature = hash(window_title + downscaled_image_bytes)
  ```
- If the signature changes:
  - Reset cycle index to 0.
  - Clear per-screen memory.

---

## Priority Cycling Logic

When the same screen signature is detected, repeated hotkey presses cycle through UI priorities:

1. Selected / highlighted element
2. Central dialog or story panel
3. Tooltip or side panel
4. Menu header + description
5. Other large readable text

If a priority is not present, fall through to the next.
If content was already spoken on this screen, skip it.

---

## Voice Question Fallback

- User holds the question hotkey.
- Audio is recorded and transcribed.
- The same screenshot plus the transcribed question is sent to the model.
- The model answers using only what is visible on screen (or says it cannot see it).

---

## Logging

### Always Local
Each interaction logs:
- Timestamp
- Window title and process name
- Screen signature
- Cycle index
- Selected UI label and bounding box
- Spoken text
- Latency metrics

### Future Upload (Opt-in)
Privacy tiers:
1. Off
2. Text-only metadata
3. Metadata + screenshot

Audio is never uploaded by default.

---

## Evaluation (P0)

Track:
- Time to first spoken audio
- Whether the spoken text was useful (manual testing)
- Effectiveness of cycling on complex screens

Test against a small set of games with different UI styles.

---

## Implementation Milestones

1. **Project Setup & Configuration**: Console app, config loading, Serilog logging
2. **Global Hotkeys**: Win32 hotkey registration with configurable key bindings
3. **Window Capture & Signatures**: Screenshot capture, image downscaling, xxHash-based signatures
4. **Model Integration**: OpenAI/Azure vision API with structured output, retry logic
5. **Audio System**: TTS playback, audio ducking, playback interruption
6. **State Management & Cycling**: Screen state tracking, priority cycling logic
7. **Voice Questions**: Hold-to-talk functionality, Whisper STT integration
8. **Logging & Observability**: JSONL logging with privacy controls
9. **Error Handling & Polish**: Retry logic, fallback messages, user-friendly errors

**Estimated Development Time**: 30-40 hours

---

## Configuration

### Settings (appsettings.json)
- API keys and endpoints (OpenAI/Azure OpenAI)
- Target language (Hebrew, Russian, others)
- Translation mode (realistic, simplified, explain)
- Configurable hotkeys
- Audio ducking preferences
- Privacy/logging levels

### Hotkeys (Configurable)
All hotkeys can be customized in the configuration file to avoid conflicts with other applications.

---

## Open Questions (Post-P0)
- Per-game prompt packs
- Caching translations
- Debug overlays
- Long-term terminology memory
