# Logging & Privacy (P0)
Last updated: 2026-01-10

## Goals
- Provide useful debugging/telemetry for P0 iterations.
- Default to **local-only** persistence.
- Avoid uploading audio and minimize sensitive data exposure.

## Sources of truth
- JSONL logger: `Core/Logging/InteractionLogger.cs`
- Log entry schema: `Core/Logging/InteractionLogEntry.cs`
- Hotkey flows emit logs: `Core/App/HotkeyAppRunner.cs`
- Config: `appsettings.json` → `InteractionLogging.*` and `Logging.*`

## Interaction log format (JSONL)
File location:
- Default: `logs/interactions.jsonl` under runtime `baseDir`
- Configurable via: `InteractionLogging.LogFilePath` (absolute or relative-to-baseDir)

Each line is a JSON object (snake_case keys) representing one interaction.

Serializer details:
- Uses `System.Text.Json` with `snake_case_lower` naming policy.
- Entries are appended with UTF-8 encoding.

## Screenshot handling (privacy-sensitive)
Screenshots are **not always logged**.

Decision rule:
- `InteractionLogger.ShouldIncludeScreenshots()` is true only when:
  - `InteractionLogging.IncludeScreenshotsInDebug == true`, **and**
  - current log level enables `Debug`

When included:
- Screenshot is encoded as PNG and stored as `screenshot_png_base64` within the log entry.

Implications:
- This can significantly increase log size and include sensitive content.
- Recommended for local debugging only.

## What gets logged (examples)
Depending on interaction type, logs can include:
- `timestamp_utc`
- `interaction_type` (e.g., `translate_cycle`, `question_tap`, `stop`)
- Window/process info:
  - `window_title`, `process_name`, `process_id`, `hwnd_hex`
- Screen/cycle info:
  - `screen_signature`, `cycle_index`, `priority`
- Model-selected region:
  - `selected_region.selected_label`
  - `selected_region.source_text`
  - `selected_region.bounding_box` (`x,y,width,height`)
- Output:
  - `translation.translated_text`, `translation.explanation`, `translation.spoken_text`
  - `user_question` (for question interactions)
- Latencies:
  - `latency_ms.capture`, `latency_ms.model_call`, `latency_ms.tts`, `latency_ms.total`
- Errors:
  - `error` (exception string)
  - `skipped_because_already_spoken` (for translate/cycle)

## Log levels
`appsettings.json` controls log level via `Logging.LogLevel.*`:
- `Default`
- `GameScreenTranslator` (app-specific override)

Console logging uses `Core/Logging/Logger.cs`.

## Data retention (P0)
- No automatic rotation implemented (assume manual cleanup).
- A future enhancement is log rotation, anonymization, and an opt-in upload tiering system.

## Explicit privacy constraints (P0)
- Audio is not written/uploaded by default.
- No network upload pipeline exists in P0.
- Any future upload feature must be opt-in and documented here (including tiers and defaults).

## Recommended operational practice
- During demos/testing, run with screenshots disabled unless explicitly needed:
  - Set `InteractionLogging.IncludeScreenshotsInDebug = false`
  - Or set log level above Debug
