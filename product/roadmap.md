# Roadmap (Now / Next / Later)

This roadmap is optimized for “portable EXE shipped to friends” and a kid-friendly experience.

## Now (0–2 weeks): ship to friends safely
- Reduce time-to-answer (capture→LLM→TTS) and make latency visible when slow
- Add stage-level latency instrumentation (STT→LLM→TTS) to drive evidence-based optimization (PRD 0001.5)
- Setup UX: first-run config creation is already present; make it hard to misconfigure
- Robust cancellations: stop speaking / stop in-flight request should be reliable
- Friend-test package: one EXE + clear instructions + lightweight troubleshooting
- Privacy defaults: screenshots off in logs by default; clear opt-in if we add telemetry

## Next (2–6 weeks): increase usefulness + reduce parent intervention
- Better “what should I ask?” UX (prompting kid to ask a clearer question)
- Better memory/context handling (per-game and per-session)
- Language UX: quick toggle for target language and kid voice preference
- Reliability: better capture for fullscreen/anti-capture games (Windows Graphics Capture fallback)

## Later (6+ weeks): scale and distribution
- Safer key management for non-dev distribution (no raw API keys on client)
- Telemetry (opt-in): failure and latency metrics without PII
- Console expansion exploration (requires capture + audio approach changes)
- Offline evaluation set + LLM-as-judge pipeline for quality regression checks
