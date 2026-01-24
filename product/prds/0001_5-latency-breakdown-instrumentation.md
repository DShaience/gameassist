# PRD 0001.5: Latency Breakdown Instrumentation (STT → LLM → TTS)

**Status:** 🟡 In progress (analysis needs start-vs-finish split) (2026-01-23)

---

## Summary
- **Problem:** "Time to beginning of the answer" feels slow. Current reports can accidentally use **time-to-finish-speaking** as “TTFA”, which hides the real start delay.
- **Goal / Outcome:** Every Ask interaction emits structured timing metrics so we can attribute latency and make an evidence-based call on streaming TTS and other optimizations.
- **Non-goals:** Shipping telemetry to a server, performance micro-optimizations, or changing model/provider behavior beyond adding timing markers.

## Users & scenarios
- Primary user: child (5–10) playing a Windows PC game.
- Secondary: developer/parent analyzing logs after a test session.

Key scenarios:
1. Developer runs a session in Hades or Space Quest IV and can quickly see which stage dominates TTFA.
2. "Slow start" is reproducible and attributable (e.g., TTS network vs playback buffering).
3. Cancellation is recorded with a clear "cancel stage" so we know where we're losing time.

## Current behavior
- We have stage timings, but the current latency report can still be misleading for "slow start" because **TTS totals can include playback duration** (longer answers naturally take longer to finish speaking).

### Why we need "first audible sample"
Recent measurements showed TTS dominating when using **TTS total time** (speak start → speak end). That metric answers "how long did speaking take overall?"
It does **not** answer "how long until the user hears the first sound?"

To make a correct decision about streaming TTS (or playback pipeline changes), we need to separate:
- **Start latency:** Ask pressed → playback starts (proxy: `Play()` invoked)
- **Provider/network gating:** TTS request start → full audio available (and therefore `Play()` can be invoked in our current implementation)
- **Answer length / speak duration:** playback starts → playback finished

Decision context (from Clay): in our current audio implementation, `Play()` is called **only after the full MP3 has arrived**.
Therefore, “time until `Play()` is called” is a good proxy for “time to first audible sound” (the remaining DAC/buffer delay is small and not the primary lever).

## Proposed solution
Add per-interaction timing markers and a summary record at the end of each Ask.

### UX / Interaction
- No user-facing UX changes required.
- (Optional) For developer builds, allow printing a one-line timing summary to console.

### Functional requirements
- **FR1: Interaction correlation.** Each Ask interaction has a stable `interaction_id` used across all stage logs.
- **FR2: Stage markers.** Record timestamps for key stage boundaries (see "Required markers").
- **FR3: Summary record.** Emit one end-of-interaction summary event containing durations, providers, and outcome.
- **FR4: Cancellation tracking.** If Stop is pressed, record `cancelled=true` and `cancel_stage` (stt/llm/tts/playback/other).
- **FR5: Playback start proxy (required).** Record when playback is initiated (timestamp when the code calls `Play()`).
- **FR6: Playback finished (required).** Record when playback finishes (playback-stopped event).

### Non-functional requirements
- **Overhead:** Logging adds negligible latency (< 5ms per interaction on typical hardware).
- **Reliability:** Logging must never crash the app; failures are swallowed and noted as a warning.
- **Privacy:** Logs must not include raw audio, raw screenshots, or API keys.

## Required markers
Record timestamps (UTC) and compute durations (ms) for:
- `t0_hotkey` (Ask pressed)
- `t1_capture_done` (screenshot ready; include capture strategy and image size)
- `t_image_downscale` (time to downscale and encode image for LLM) ✅ **Added during implementation**
- `t2_stt_start`
- `t3_stt_final` (final transcript available)
- `t4_llm_request_start`
- `t5_llm_first_token` (if available; else omit) - Not implemented (would require streaming API)
- `t6_llm_done`
- `t7_tts_request_start`
- `t8_tts_first_audio_bytes` (first audio bytes available from provider / SDK) (optional)
- `t9_play_called` (**required**; proxy for “audio starts” in our current implementation)
- `t10_playback_finished` (**required**; playback stopped/event)

Derived metrics to compute in the summary:
- `time_to_play_ms = t9_play_called - t0_hotkey` (**primary UX metric**)
- `time_to_finish_ms = t10_playback_finished - t0_hotkey` (useful for understanding answer length impact)
- `stt_ms = t3 - t2`
- `llm_ms = t6 - t4`
- `tts_network_ms = t9_play_called - t7_tts_request_start` (in our current pipeline: “request → full audio ready”)
- `tts_first_byte_ms = t8_tts_first_audio_bytes - t7_tts_request_start` (if `t8` exists)
- `playback_duration_ms = t10_playback_finished - t9_play_called`

**Additional metrics added during implementation:**
- `image_downscale_ms` - CPU time for image downscaling
- `llm_input_tokens` / `llm_output_tokens` - For normalizing LLM time to tokens/sec
- `tts_char_count` - For normalizing TTS time to chars/sec

## Logging format
- Logs should be **structured JSON** (existing `.jsonl` is fine): one event per line.
- Include, where applicable:
  - `interaction_id`
  - `event_name`
  - `timestamp_utc`
  - `duration_ms` (for summary)
  - `providers`: `stt_provider`, `llm_provider`, `tts_provider`
  - `model`/`voice` identifiers (safe strings only)
  - `game/window` context if available (window title is acceptable; do not log file paths)
  - `outcome`: `success|timeout|error|cancelled`
  - `error_code` (coarse) and `error_stage`

Example summary event (illustrative):
```json
{
  "event_name": "ask.summary",
  "interaction_id": "c4c0f5e8-6b90-4ec2-8d07-3f7c6232f1bb",
  "ttfa_user_ms": 4120,
  "stt_ms": 980,
  "llm_ms": 1760,
  "tts_network_ms": 620,
  "playback_start_delay_ms": 120,
  "outcome": "success",
  "providers": { "stt": "AzureSpeech", "llm": "Foundry", "tts": "AzureOpenAI" }
}
```

## Edge cases / failure behavior
- If a stage fails (STT/LLM/TTS/playback), emit summary with `outcome=error` and the last completed marker.
- If markers aren't available (e.g., `first_token`), omit them rather than faking values.

## Rollout
- Dev testing:
  - Run 10 Ask interactions in Hades and 10 in Space Quest IV.
  - Verify summary events exist and have sane durations.
- Friend testing (later):
  - Keep logs local-only; ask testers to send the log file if they opt-in.

Exit criteria:
- For 100% of Ask interactions, a single `ask.summary` log line is produced.
- `time_to_play_ms` and `playback_duration_ms` are present and non-negative for every successful Ask.
- `tts_network_ms` is present and non-negative for every successful Ask.
- Cancelled interactions record `cancel_stage`.

## Analysis recipe (how we decide what to optimize)
This is the "what to do with the logs" checklist. It's intentionally simple so we can make a decision quickly.

### 1) Build a small dataset
- Collect at least **20 successful** `ask.summary` events across **Hades** and **Space Quest IV**.
- Tag each run with whether it's a **cold start** (first Ask after app launch or >10 minutes idle).

### 2) Compute distribution, not single samples
For each metric, compute at least **p50** and **p90**:
- `time_to_play_ms` (primary)
- `time_to_finish_ms` (secondary)
- `stt_ms`, `llm_ms`
- `tts_network_ms` and `playback_duration_ms`

### 3) Attribute the bottleneck
For each event, compute stage shares (as percentages of `time_to_play_ms`):
- `stt_share = stt_ms / time_to_play_ms`
- `llm_share = llm_ms / time_to_play_ms`
- `tts_network_share = tts_network_ms / time_to_play_ms`

Then classify the dominant stage for each event as the max share.
Report: "Top bottleneck stage rate" (e.g., LLM dominant in 65% of cases).

### 4) Decision rubric: do we need streaming TTS?
Only consider streaming TTS if:
- `tts_network_ms` is frequently dominant (or near-dominant), AND
- LLM is already fast enough (e.g., p50 `llm_ms` is not the main driver).

Streaming TTS is unlikely to help if:
- `llm_ms` dominates (optimize model/prompt/image first).

Streaming TTS may still be useful even when `playback_duration_ms` is long, because streaming primarily targets **start latency** (time-to-play), not “finish speaking”.

### 5) Quick outputs to paste into a dev note
- p50/p90 `ttfa_user_ms` per game
- bottleneck stage rate (overall)
- cold-start vs warm-start p50 `ttfa_user_ms`
- 2–3 representative slow traces (interaction_id + stage durations)

## Risks & mitigations
- Risk: too many log lines become noisy.
  - Mitigation: keep per-stage markers minimal; rely on the single summary for analysis.
- Risk: "first audio played" is hard to detect accurately.
  - Mitigation: record best-available proxy (audio device write start) and document its meaning; improve later if needed.

## Acceptance criteria
- AC1: Each Ask generates exactly one `ask.summary` record. ✅
- AC2: Summary includes `time_to_play_ms`, `stt_ms`, `llm_ms`, and `tts_network_ms` for successful asks.
- AC3: Summary includes `outcome` and, for failures/cancels, a `error_stage`/`cancel_stage` field. ✅
- AC4: Summary includes `playback_duration_ms` for successful asks.

## Decision Note: “Play() called” as the proxy for start (2026-01-23)

**Decision:** Use the timestamp when our code calls `Play()` (`t9_play_called`) as the proxy for “audio start”.

**Reasoning:**
1. In our current implementation, `Play()` is invoked **only after the full MP3 is available**, so it is tightly coupled to “audio can begin”.
2. Any remaining audio-device latency (~tens of milliseconds) is not the main optimization lever compared to multi-second STT/LLM/TTS network delays.
3. This gives us a clean split between **start latency** (`time_to_play_ms`) and **speak duration** (`playback_duration_ms`).

**Future consideration:**
- If/when we add streaming TTS (play while downloading/generating), we may need a different “audio start” signal (first buffer queued).

## Request: update scripts/analyze_latency.py (report correctness)
The analyzer currently treats `latency_ms.total` as “TTFA”. That is effectively “time-to-finish-speaking” and will overstate start latency.

Update the script to:
- Report **two metrics** for `question_tap`:
  - `time_to_play_ms` (primary; start latency)
  - `time_to_finish_ms` (secondary)
- Show `playback_duration_ms` explicitly (so answer length is visible)
- Base all “% breakdown” computations on `time_to_play_ms` (not `latency_ms.total`)

### Script review notes (what to change)
- In the main table and summaries, rename/replace “TTFA” with “TimeToPlay” (and optionally add “TimeToFinish”).
- For “SUMMARY BY GAME”, stop using `latency_ms.total`; use `latency_ms.detailed.time_to_play_ms` (or equivalent field name in the log schema).
- For “COLD START vs WARM”, compare `time_to_play_ms` p50/p90 (not total).
- Bottleneck analysis should be computed on the start-latency breakdown, not on playback duration.

## Implementation Notes (2026-01-23)

### Files Changed
- `Core/Logging/InteractionLogEntry.cs` - Added `DetailedTimings` class with all timing fields
- `Core/Abstractions/ITtsService.cs` - Added `TtsTimingInfo` return type with network/playback timing
- `Core/Audio/AzureCognitiveTtsService.cs` - Returns timing info from `SpeakAsync`
- `Core/Audio/AzureOpenAiTtsService.cs` - Returns timing info from `SpeakAsync`
- `Core/Models/TranslateCycleResponse.cs` - Added `PromptTokens`/`CompletionTokens` fields
- `Core/Foundry/AzureFoundryClient.cs` - Extracts token usage from API response
- `Core/App/HotkeyAppRunner.cs` - Wired up all timing markers in `TapQuestionAutoStopAsync`

### Log Output Location
Detailed timing is logged in `LatencyMs.Detailed` within each `question_tap` interaction in `logs/interactions.jsonl`.

### Limitations
- `t5_llm_first_token` is not implemented (would require streaming API support)
- `PlaybackStartDelayMs` is estimated as ~0ms. Per Decision Note above, precise measurement is not needed because:
  - Expected delay is 30-100ms (NAudio/Windows/hardware buffers)
  - TTS network time (2-10s) dominates the TTFA
  - The playback init time is <1% of total and not actionable

### Key Finding from Initial Data (2026-01-23)
From 7 `question_tap` interactions in Hades:
- **STT:** ~3.2s (7-10% of TTFA)
- **LLM:** ~8.9s (20-25% of TTFA)
- **TTS total:** ~32s average (50-70% of TTFA) — dominated by playback duration, not network

**Bottleneck:** TTS network time (2.4-10.4s) + long response length (149-752 chars → long playback)

**Optimization options to explore:**
1. Shorter responses via prompt engineering
2. Response truncation (cap spoken text)
3. Streaming TTS (start playback while generating) — if TTS network time remains high after #1/#2
