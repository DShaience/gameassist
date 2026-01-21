# PRD 0001: MVP for Friends (Portable EXE)

**Status:** ✅ Implemented (2026-01-13)  
**Implementation:** PRD 0002 (detailed implementation spec)

---

## Summary
- **Problem:** Friends/family can't reliably set up and use the app quickly; failures feel "mysterious" and latency breaks the play flow.
- **Goal / Outcome:** A friend can download a build, configure it in <10 minutes, and successfully ask 3 questions in-game with understandable responses.
- **Non-goals:** Perfect accuracy, console support, full translation of every UI element.

## Implementation Status

| Requirement | Status | Notes |
|-------------|--------|-------|
| Hotkey mode with STT/TTS | ✅ Done | Ctrl+Alt+W (ask), T (translate), X (stop), R (reset) |
| Thinking chime (earcon) | ✅ Done | Plays ~400ms after STT ends if model hasn't responded |
| Listening chime (earcon) | ✅ Done | MP3 plays immediately on tap-to-talk |
| Config validation with clear errors | ✅ Done | Shows exact file/field on failure |
| QUICKSTART.txt for friends | ✅ Done | `product/prds/QUICKSTART.txt` |
| Build script for distribution ZIP | ✅ Done | `scripts/publish-friends.ps1` |
| Stop hotkey cancels operations | ✅ Done | Stops TTS, cancels in-flight requests |
| Conversation context (follow-ups) | ✅ Done | Rolling summary + recent turns |

### Open Questions Resolved

| Question | Resolution |
|----------|------------|
| State announcements spoken or silent? | **Silent with earcons only** — avoids latency/noise |
| Default answer length? | **Handled via prompt** — not app config |

---

## Users & scenarios
- Primary user: child (5–10) playing a Windows PC game
- Secondary: parent helping with setup; parent may not know the game

Key scenarios:
1. Child presses Ask (tap-to-talk) hotkey, asks "What do I do now?"; app answers and speaks. ✅
2. Child hits Stop hotkey; speech stops quickly. ✅
3. App is slow (cloud); child gets feedback that it's "thinking". ✅
4. Common misconfig (bad endpoint/key) produces a clear spoken + logged message. ✅

## Current behavior (baseline)
- Hotkey mode exists with STT, screenshot capture, Foundry answer, and Speech TTS. ✅
- Config files are created next to EXE on first run. ✅
- Logging exists; screenshots can be embedded in debug logs. ✅

## Proposed solution
### UX / interaction
- Add consistent state announcements (spoken or short console log):
  - "Listening…" → "Thinking…" → "Speaking…"
  - **Resolution:** Implemented as earcons (non-speech audio cues) ✅
- Add timeouts with clear messages:
  - If LLM exceeds timeout: "It's taking too long. Try again." ✅
- Ensure Ctrl+Alt+X interrupts:
  - Stops audio immediately ✅
  - Cancels in-flight LLM request ✅
  - Cancels STT if running ✅

### Earcons ("thinking chime")
Goal: remove the "silent waiting = did it break?" feeling without adding extra spoken chatter.

**Implementation Complete:**
- **Listening chime:** MP3 file plays immediately on tap-to-talk (before STT) ✅
- **Thinking chime:** Synthetic dual-tone plays ~400ms after STT ends ✅
- **Cancel chime:** Optional (off by default) ✅

Configuration (`appsettings.json` → `Audio.Earcons`):
```json
"Earcons": {
  "Enabled": true,
  "ListeningChimeEnabled": true,
  "ListeningChimePath": "Assets/Sounds/tap-notification-357575.mp3",
  "ThinkingChimeEnabled": true,
  "ThinkingDelayMs": 400,
  "CancelChimeEnabled": false,
  "Volume": 0.12
}
```

### Functional requirements
- FR1: In hotkey flows, always emit a user-facing state change (log and/or optional short TTS). ✅
- FR2: Provide a single "setup quickstart" doc for friends with screenshots optional. ✅
- FR3: On configuration load failure, show exactly which file is wrong and which field is missing. ✅

### Non-functional requirements
- Latency: target median time-to-first-audio < 3 seconds on good internet; p95 < 8 seconds. *(testing pending)*
- Reliability: no crashes during a 30-minute session; hotkeys never stop working. *(testing pending)*
- Accessibility: default answer style "short, kid-friendly"; avoid long paragraphs. ✅ *(via prompts)*
- Privacy: keep interaction logs local-only; screenshots off by default. ✅
- Cost: default to conservative max tokens and image downscale to protect the $100/month budget. ✅

Timeouts (defaults):
- LLM hard timeout: 30 seconds ✅
- On timeout, user-facing message (TTS): "It's taking too long. Try again." ✅

## Edge cases / failure behavior
- Capture fails: speak a short fallback ("I couldn't see the game. Try clicking the game window.") ✅
- STT fails: speak fallback ("I didn't catch that. Try again.") ✅
- LLM fails: speak fallback ("I couldn't get an answer right now. Try again.") ✅
- TTS fails: at minimum print the answer text in console/log so user isn't blocked. ✅

## Telemetry (opt-in, later)
- Metrics (no PII): latency buckets, error codes, feature usage counts, model token totals.
- Do not collect: raw audio, raw screenshots, user identifiers.
- **Status:** Not implemented (future)

## Rollout
- Dev: run hotkey mode for 30 minutes across 2–3 games. *(pending)*
- Friends: ship to 3–5 testers with a short survey. *(pending)*

Exit criteria:
- 80% of testers can complete setup without a live call.
- At least 1 full session per tester with 3 successful Q&A.

## Acceptance criteria
- AC-E1: On tap-to-talk, if TTFA exceeds the chime delay, the thinking chime plays once after STT ends. ✅
- AC-E2: If the model response returns before the delay, no thinking chime is played. ✅
- AC-E3: No earcon plays while STT is actively recording. ✅
- **AC-L1:** Listening chime plays immediately on tap-to-talk. ✅ *(added)*

## Metrics (definitions)
We track two time-to-first-audio measures to avoid confusing "kid talk time" with "system latency".

- **TTFA (user-perceived)**: from tap-to-talk hotkey press → first audible TTS audio.
- **TTFA (system)**: from STT end (recognized text available) → first audible TTS audio.

## Risks & mitigations
- Risk: kids ask very vague questions → model answers unhelpfully.
  - Mitigation: prompt for clarifying question once; "Ask: What should I click?" suggestions.
- Risk: API keys exposure in distributed EXE.
  - Mitigation: explicitly label as "friends-only build"; plan a broker/proxy for real distribution.
  - **Future:** See `product/backlog/future-secure-azure-architecture.md` for production solution.

---

## Files Created/Modified

| File | Purpose |
|------|---------|
| `scripts/publish-friends.ps1` | Build script for distribution ZIP |
| `product/prds/QUICKSTART.txt` | Setup instructions for friends |
| `product/prds/0002-mvp-friends-portable-exe-detailed.md` | Detailed implementation PRD |
| `GameScreenTranslator/Core/Audio/EarconPlayer.cs` | MP3/WAV earcon playback |
| `GameScreenTranslator/Core/Configuration/AppSettings.cs` | Added earcon settings |
| `GameScreenTranslator/Core/Configuration/ConfigBootstrapper.cs` | Extract earcon assets |
| `GameScreenTranslator/appsettings.template.json` | New earcon config fields |
| `GameScreenTranslator/docs/design/30_runtime_and_configs.md` | Updated docs |
| `GameScreenTranslator/docs/design/40_hotkeys_and_interaction.md` | Updated docs |
