# Game Screen Translator — Design / PRD Index (P0)
Last updated: 2026-01-10  
Scope: **P0** (Windows-only, hotkey-driven capture → vision LLM → TTS; optional STT questions; local logging)

## Purpose
These documents are a **roadmap + reference** so future contributors/AI assistants can work effectively **without re-reading the whole codebase**.

## Mandatory maintenance rule (applies to all changes)
Any change that affects behavior, UX, configuration, APIs, logging, prompts, or architecture **must include an update to these docs**:
- Update the relevant file(s) under `GameScreenTranslator/docs/design/`
- If the change introduces a new concept/component, add a new design doc and link it here
- Keep examples/paths/hotkeys/config keys in sync with the code

## What to read (quick navigation)
- **Product goals, scope, UX** → `10_prd_overview.md`
- **System architecture + end-to-end flows** → `20_architecture.md`
- **Runtime layout + configs (appsettings, speech config, prompts)** → `30_runtime_and_configs.md`
- **Hotkeys + interaction state machine** → `40_hotkeys_and_interaction.md`
- **Model contract + audio (Foundry + Azure Speech)** → `50_model_and_audio.md`
- **Logging + privacy controls** → `60_logging_and_privacy.md`
- **Dev workflow (build/run/test/publish) + doc update checklist** → `90_dev_workflow.md`

## Code map (current structure)
Top-level entrypoint:
- `GameScreenTranslator/Program.cs`

Core modules:
- Capture: `Core/Capture/*`
- Hotkeys + runtime loop: `Core/App/*`, `Core/Hotkeys/*`
- Foundry client (vision LLM): `Core/Foundry/*`
- Audio (TTS/STT): `Core/Audio/*`
- State/cycling/signature: `Core/State/*`
- Configuration loaders/types: `Core/Configuration/*`
- Logging (JSONL): `Core/Logging/*`

## Out of scope
These docs are not meant to be marketing material or a tutorial; they are a precise engineering reference.
