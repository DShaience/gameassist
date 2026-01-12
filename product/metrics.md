# Metrics (draft)

## North Star (friends phase)
- **Successful Q&A per session**: count of question → answer spoken without an error.

## Experience metrics
- **Time-to-first-audio (TTFA)**: from hotkey start to first TTS audio.
- **Timeout rate**: % of requests hitting timeout.
- **Cancel effectiveness**: % of cancels that stop speaking within 250ms.

## Reliability metrics
- **Crash-free sessions**
- **Capture failure rate**
- **STT failure rate**
- **LLM failure rate**
- **TTS failure rate**

## Cost metrics
- Tokens per request (chat)
- Image size / downscale setting used
- Requests per hour

## Quality (later)
- Curated offline evaluation set: screenshot + question + expected answer.
- LLM-as-judge scoring for relevance and helpfulness.

## Telemetry principles (if enabled)
- Opt-in only
- No raw audio
- No raw screenshots
- No stable user identifiers
