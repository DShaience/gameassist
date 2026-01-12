# Product Management System

This folder is the product “source of truth” for **Game Screen Translator / Gamer Assistant**.

## What this app is (current)
A Windows desktop assistant that helps children (ages 5–10) play games that are primarily English-only.

Primary flow today:
- Child asks a question (STT)
- App captures the active game window (screenshot)
- LLM answers using the question + screenshot + prior context
- App speaks back (TTS)

## How we work

### Artifacts
- `vision.md`: why we exist, for whom, and product principles
- `roadmap.md`: Now/Next/Later priorities
- `backlog.md`: ranked list of items we pull into work
- `prds/`: PRDs for any meaningful feature/change
- `decisions/decision-log.md`: key decisions + rationale
- `research/user-notes.md`: feedback, assumptions, experiments
- `releases/release-notes.md`: human-readable changes over time

### Cadence (suggested)
- Weekly (30–45 min): review roadmap, cut scope, choose next 1–3 backlog items
- Per change: update PRD (if needed), then implement
- Per build shipped to friends: add a release note entry

### Definitions
- **MVP (friends)**: stable enough for family/friends; failures are understandable; setup is easy; performance acceptable.
- **P0 bug**: crashes, hotkeys broken, cannot speak, cannot capture, or repeated hangs.

### Prioritization rules (default)
We use a lightweight score to avoid bikeshedding:

`Priority = (Impact × Reach × Confidence) / Effort`

- **Impact**: 1–5 (how much better the experience is)
- **Reach**: 1–5 (how often it happens / how many users)
- **Confidence**: 0.5–1.0
- **Effort**: 1–5 (engineering time + risk)

If two items tie, we choose:
1) stability/latency > usability > new capabilities
2) easier friend-testing > advanced architecture

## Guardrails
- Privacy: default to local-only data; explicit opt-in for telemetry.
- Safety: assume child is the user; avoid collecting personal data; avoid unsafe advice.
- Cloud dependence is acceptable (we want to avoid competing with the game’s CPU/GPU).

## Links to engineering docs
- Engineering setup + publishing: `GameScreenTranslator/README.md`
- “Post development operations”: `POST_DEVELOPMENT_OPERATIONS.md`
