# PRD 0001: MVP for Friends (Portable EXE)

## Summary
- **Problem:** Friends/family can’t reliably set up and use the app quickly; failures feel “mysterious” and latency breaks the play flow.
- **Goal / Outcome:** A friend can download a build, configure it in <10 minutes, and successfully ask 3 questions in-game with understandable responses.
- **Non-goals:** Perfect accuracy, console support, full translation of every UI element.

## Users & scenarios
- Primary user: child (5–10) playing a Windows PC game
- Secondary: parent helping with setup; parent may not know the game

Key scenarios:
1. Child holds Ask hotkey, asks “What do I do now?”, releases; app answers and speaks.
2. Child hits Stop hotkey; speech stops quickly.
3. App is slow (cloud); child gets feedback that it’s “thinking”.
4. Common misconfig (bad endpoint/key) produces a clear spoken + logged message.

## Current behavior (baseline)
- Hotkey mode exists with STT, screenshot capture, Foundry answer, and Speech TTS.
- Config files are created next to EXE on first run.
- Logging exists; screenshots can be embedded in debug logs.

## Proposed solution
### UX / interaction
- Add consistent state announcements (spoken or short console log):
  - “Listening…” → “Thinking…” → “Speaking…”
- Add timeouts with clear messages:
  - If LLM exceeds timeout: “It’s taking too long. Try again.”
- Ensure Ctrl+Alt+X interrupts:
  - Stops audio immediately
  - Cancels in-flight LLM request
  - Cancels STT if running

### Earcons (“thinking chime”)
Goal: remove the “silent waiting = did it break?” feeling without adding extra spoken chatter.

Scope:
- Earcons apply to **tap-to-talk** only (question flow).
- Translate/Cycle stays unchanged for now.

Rules:
- Do not play earcons while the microphone is actively recording (avoid STT contamination).
- After STT ends and while the model request runs, play a **soft thinking chime**.
- Avoid annoyance: if the model responds very fast, the chime should not play.
- Only play once per request.

Suggested defaults (tunable):
- Thinking chime delay: ~400ms (delay starts when STT ends)
- Volume: low (soft by default)
- Cancel/stop chime: optional (off by default), configured via `Audio.Earcons.CancelChimeEnabled`

Implementation notes:
- The thinking delay timer starts at **STT end** (recognized text available), not at hotkey press.
- The thinking chime must be canceled/suppressed if the model response returns before the delay elapses.

### Functional requirements
- FR1: In hotkey flows, always emit a user-facing state change (log and/or optional short TTS).
- FR2: Provide a single “setup quickstart” doc for friends with screenshots optional.
- FR3: On configuration load failure, show exactly which file is wrong and which field is missing.

### Non-functional requirements
- Latency: target median time-to-first-audio < 3 seconds on good internet; p95 < 8 seconds.
- Reliability: no crashes during a 30-minute session; hotkeys never stop working.
- Accessibility: default answer style “short, kid-friendly”; avoid long paragraphs.
- Privacy: keep interaction logs local-only; screenshots off by default.
- Cost: default to conservative max tokens and image downscale to protect the $100/month budget.

Timeouts (defaults):
- LLM hard timeout: 30 seconds
- On timeout, user-facing message (TTS): “It’s taking too long. Try again.”

## Edge cases / failure behavior
- Capture fails: speak a short fallback (“I couldn’t see the game. Try clicking the game window.”)
- STT fails: speak fallback (“I didn’t catch that. Try again.”)
- LLM fails: speak fallback (“I couldn’t get an answer right now. Try again.”)
- TTS fails: at minimum print the answer text in console/log so user isn’t blocked.

## Telemetry (opt-in, later)
- Metrics (no PII): latency buckets, error codes, feature usage counts, model token totals.
- Do not collect: raw audio, raw screenshots, user identifiers.

## Rollout
- Dev: run hotkey mode for 30 minutes across 2–3 games.
- Friends: ship to 3–5 testers with a short survey.

Exit criteria:
- 80% of testers can complete setup without a live call.
- At least 1 full session per tester with 3 successful Q&A.

## Acceptance criteria
- AC-E1: On tap-to-talk, if TTFA exceeds the chime delay, the thinking chime plays once after STT ends.
- AC-E2: If the model response returns before the delay, no thinking chime is played.
- AC-E3: No earcon plays while STT is actively recording.

## Metrics (definitions)
We track two time-to-first-audio measures to avoid confusing “kid talk time” with “system latency”.

- **TTFA (user-perceived)**: from tap-to-talk hotkey press → first audible TTS audio.
- **TTFA (system)**: from STT end (recognized text available) → first audible TTS audio.

## Risks & mitigations
- Risk: kids ask very vague questions → model answers unhelpfully.
  - Mitigation: prompt for clarifying question once; “Ask: What should I click?” suggestions.
- Risk: API keys exposure in distributed EXE.
  - Mitigation: explicitly label as “friends-only build”; plan a broker/proxy for real distribution.

## Open questions
- Should state announcements be spoken (kid-friendly) or only logged (less noise)?
- What’s the ideal default “answer length” (1 sentence vs 2–3)?
