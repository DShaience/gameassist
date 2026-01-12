# Privacy & Telemetry (draft)

## Defaults
- Keep data local-only by default.
- Avoid collecting or storing personal data.

## What the app may handle during use
- Screenshots of the active window (for vision)
- Audio from the microphone (for STT)
- Text questions and model outputs

## Storage policy (recommended defaults)
- Local logs may store:
  - timestamps
  - error codes
  - latency timings
  - model response text (optional; can be off)
- Local logs should NOT store screenshots unless explicitly enabled for debugging.

## Telemetry (future)
If we add telemetry, it should be:
- Explicit opt-in
- Easy to disable
- Limited to aggregates and error/latency events

Telemetry must NOT include:
- Raw screenshots
- Raw audio
- Names, emails, or any stable personal identifiers

## Child safety lens
Assume the child is the primary user even if the parent configures the app. Prefer conservative data practices.
