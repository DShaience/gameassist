# PRD 0002: MVP for Friends — Portable EXE (Detailed)

**Status:** Draft  
**Author:** Product/Engineering  
**Last Updated:** 2026-01-13  
**Depends On:** Core P0 implementation (complete)

---

## Summary

- **Problem:** Friends and family want to try the Game Screen Translator, but current setup requires developer knowledge (editing JSON, understanding config structure). The app lacks audio feedback when recording starts, making it unclear if the hotkey worked.

- **Goal / Outcome:** A friend can download a ZIP, extract it, enter API keys in a simple text file, and successfully use the app within 10 minutes. The app provides clear audio feedback for state changes (listening, thinking) without spoken announcements.

- **Non-goals:**
  - Perfect accuracy or translation quality (prompt tuning is separate)
  - Console/Mac/Linux support
  - Production-grade key management (friends-only, using developer's keys)
  - Spoken state announcements (use earcons only)
  - GUI beyond system tray icon

---

## Users & Scenarios

### Primary User
Child (5–10) playing a Windows PC game (e.g., Hades, FTL, Pyre) who needs help understanding game UI/text.

### Secondary User  
Parent helping with initial setup. May not know the specific game or have technical background.

### Key Scenarios

| # | Scenario | Expected Behavior |
|---|----------|-------------------|
| S1 | Child presses Ctrl+Alt+W to ask "What do I do now?" | Earcon plays immediately → STT records → thinking chime → answer spoken |
| S2 | Child presses Ctrl+Alt+X while answer is playing | Audio stops immediately, cancel chime plays (if enabled) |
| S3 | App is slow (network/model latency) | Thinking chime plays after ~400ms, indicating processing |
| S4 | Bad API key configured | App shows clear error message with exact field name and file to fix |
| S5 | Parent extracts ZIP and runs EXE first time | Configs auto-created; parent edits keys; app works on second run |

---

## Current Behavior (Baseline)

### What Works
- Hotkey mode with STT (Ctrl+Alt+W), translate (Ctrl+Alt+T), stop (Ctrl+Alt+X), reset (Ctrl+Alt+R)
- Screenshot capture of active window
- Azure Foundry vision model call with structured JSON response
- Azure Speech TTS for spoken responses
- Config auto-bootstrap from embedded templates on first run
- Thinking chime (synthetic dual-tone) with configurable delay
- Cancel chime (optional, off by default)
- Robust config validation with actionable error messages
- Conversation context tracking with summary

### What's Missing
1. **Listening earcon:** No audio feedback when STT recording starts
2. **Distribution package:** No ready-to-share ZIP with quickstart documentation
3. **MP3 earcon support:** Current earcons are synthetic; should support MP3 files

---

## Proposed Solution

### 1. Listening Earcon (Recording Started)

**Requirement:** Play a short audio cue when STT recording begins, so the user knows the hotkey worked.

**Implementation:**
- Add `ListeningChimeEnabled` and `ListeningChimePath` to `EarconsSettings`
- Default path: `Assets/Sounds/tap-notification-357575.mp3` (already exists in repo)
- Play immediately when `TapQuestionAutoStopAsync` begins, before STT `RecognizeOnceWithEndSilenceTimeoutAsync`
- Must NOT contaminate STT (play before mic is active, or use separate output channel)

**Config addition to `appsettings.json`:**
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

**Timing diagram (tap-to-talk flow):**
```
[Hotkey Press]
    │
    ▼
[Play Listening Earcon] ──► Short delay (~100ms) to ensure playback starts
    │
    ▼
[Capture Screenshot]
    │
    ▼
[Start STT Recording] ──► User speaks
    │
    ▼
[STT Ends (silence detected)]
    │
    ├─── Start thinking delay timer (400ms)
    │
    ▼
[Model Request In Flight]
    │
    ├─── If 400ms elapses before response: play thinking chime
    │
    ▼
[Model Response Received] ──► Cancel thinking timer if not yet fired
    │
    ▼
[TTS Speaks Response]
```

**Acceptance Criteria:**
- AC-L1: When Ctrl+Alt+W is pressed, a short audio cue plays within 100ms.
- AC-L2: The listening earcon completes (or is non-blocking) before STT begins to avoid contaminating the recording.
- AC-L3: Listening earcon can be disabled via config (`ListeningChimeEnabled: false`).
- AC-L4: Custom sound file path is supported via `ListeningChimePath`.

---

### 2. MP3 Playback Support for Earcons

**Requirement:** Support playing MP3 files for earcons, not just synthetic tones.

**Implementation options:**

| Option | Pros | Cons | Recommendation |
|--------|------|------|----------------|
| A) `System.Media.SoundPlayer` (WAV only) | Built-in, no deps | No MP3 support | ❌ Not suitable |
| B) NAudio NuGet package | Full MP3 support, mature | Adds ~500KB dependency | ✅ Recommended |
| C) Windows Media Player COM | No new deps | Complex, COM interop issues | ❌ Avoid |
| D) Convert MP3 to WAV at build time | Simple playback | Extra build step, larger assets | ⚠️ Fallback option |

**Recommended:** Option B (NAudio) — minimal dependency, reliable MP3 playback.

**Implementation notes:**
- Add `NAudio` package reference to `GameScreenTranslator.csproj`
- Create `Mp3EarconPlayer` class or extend `EarconPlayer`
- Fire-and-forget playback (non-blocking)
- Graceful fallback if file not found (log warning, continue)

**Acceptance Criteria:**
- AC-M1: App can play `.mp3` files specified in earcon config paths.
- AC-M2: Missing earcon file logs a warning but does not crash the app.
- AC-M3: Earcon volume setting applies to MP3 playback.

---

### 3. Distribution Package

**Requirement:** Create a ready-to-share ZIP file for friends.

**Package contents:**
```
GameScreenTranslator-Friends-v0.1.0/
├── GameScreenTranslator.exe          # Self-contained single-file EXE
├── appsettings.json                   # Pre-filled with your API keys
├── speech_language_config.json        # Pre-filled with your Speech keys
├── configs/
│   └── prompts.yaml                   # Default prompts (no changes needed)
├── Assets/
│   └── Sounds/
│       └── tap-notification-357575.mp3
└── QUICKSTART.txt                     # Setup instructions
```

**QUICKSTART.txt content:**
```
=== Game Screen Translator - Quick Start ===

1. EXTRACT this entire folder somewhere (e.g., Desktop)
   - Keep all files together!

2. RUN GameScreenTranslator.exe
   - A system tray icon appears (bottom-right, near clock)
   - If Windows SmartScreen warns, click "More info" → "Run anyway"

3. START YOUR GAME in windowed or borderless-windowed mode
   - Fullscreen exclusive may not capture correctly

4. USE HOTKEYS:
   - Ctrl+Alt+W  = Ask a question (speak after the beep)
   - Ctrl+Alt+T  = Translate what's on screen
   - Ctrl+Alt+X  = Stop speaking
   - Ctrl+Alt+R  = Reset conversation

5. TROUBLESHOOTING:
   - No sound? Check Windows volume and output device
   - Black screen capture? Try windowed mode instead of fullscreen
   - "API key" error? Contact [your name] for help

6. FEEDBACK:
   - Tell us what worked and what didn't!
   - Note which game you tested

=== Enjoy! ===
```

**Build script addition (PowerShell):**
```powershell
# publish-friends.ps1
$version = "0.1.0"
$outDir = "dist\GameScreenTranslator-Friends-v$version"

# Build
dotnet publish GameScreenTranslator\GameScreenTranslator.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true

# Prepare output directory
Remove-Item -Recurse -Force $outDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $outDir | Out-Null
New-Item -ItemType Directory -Path "$outDir\configs" | Out-Null
New-Item -ItemType Directory -Path "$outDir\Assets\Sounds" | Out-Null

# Copy files
Copy-Item "GameScreenTranslator\bin\Release\net10.0-windows\win-x64\publish\GameScreenTranslator.exe" $outDir
Copy-Item "GameScreenTranslator\appsettings.json" $outDir  # Your actual config with keys!
Copy-Item "GameScreenTranslator\speech_language_config.json" $outDir
Copy-Item "GameScreenTranslator\configs\prompts.yaml" "$outDir\configs\"
Copy-Item "GameScreenTranslator\Assets\Sounds\tap-notification-357575.mp3" "$outDir\Assets\Sounds\"
Copy-Item "product\prds\QUICKSTART.txt" $outDir

# Create ZIP
Compress-Archive -Path $outDir -DestinationPath "dist\GameScreenTranslator-Friends-v$version.zip" -Force

Write-Host "Created: dist\GameScreenTranslator-Friends-v$version.zip"
```

**Acceptance Criteria:**
- AC-D1: Running the build script produces a complete ZIP file.
- AC-D2: Extracting and running the EXE works without additional setup (keys pre-filled).
- AC-D3: QUICKSTART.txt is included and readable.

---

### 4. Asset Embedding for Portable EXE

**Requirement:** Ensure MP3 earcon files are accessible when running from the portable EXE.

**Current behavior:** Config templates are embedded as resources and extracted on first run.

**Options:**

| Option | Pros | Cons | Recommendation |
|--------|------|------|----------------|
| A) Embed MP3 as resource, extract on first run | Consistent with current pattern | Slightly larger EXE | ✅ Recommended |
| B) Require MP3 files alongside EXE | Simpler build | User might delete/move files | ⚠️ Current approach |
| C) Embed MP3 and play from memory | No extraction | More complex code | ❌ Overkill for P0 |

**Recommended:** Option A — extend `ConfigBootstrapper` to extract `Assets/Sounds/*.mp3` from embedded resources if missing.

**Acceptance Criteria:**
- AC-A1: Running the EXE in an empty folder auto-creates `Assets/Sounds/tap-notification-357575.mp3`.
- AC-A2: If user provides their own sound file, it is not overwritten.

---

## Functional Requirements Summary

| ID | Requirement | Priority | Status |
|----|-------------|----------|--------|
| FR1 | Play listening earcon when tap-to-talk begins | Must | New |
| FR2 | Support MP3 file playback for earcons | Must | New |
| FR3 | Provide distribution ZIP with pre-filled configs | Must | New |
| FR4 | Include QUICKSTART.txt in distribution | Must | New |
| FR5 | Embed earcon assets for portable extraction | Should | New |
| FR6 | Config validation shows actionable error messages | Must | ✅ Done |
| FR7 | Thinking chime plays after STT ends (with delay) | Must | ✅ Done |
| FR8 | Stop hotkey cancels in-flight operations | Must | ✅ Done |

---

## Non-Functional Requirements

### Latency
- **Listening earcon:** < 100ms from hotkey press to audio start
- **Time to first audio (TTFA system):** Median < 3s, P95 < 8s (unchanged)

### Reliability
- No crashes during 30-minute session
- Hotkeys remain responsive even if model call is slow
- Missing earcon file logs warning but doesn't crash

### Accessibility
- Default answer style: short, kid-friendly (2-3 sentences max)
- Clear audio feedback for state changes

### Privacy
- Interaction logs remain local-only
- Screenshots in logs off by default (`IncludeScreenshotsInDebug: false`)
- No telemetry in friends build

### Cost
- Default image downscale: 640px width (minimize tokens)
- No changes to current cost controls

---

## Edge Cases / Failure Behavior

| Scenario | Behavior |
|----------|----------|
| Earcon MP3 file missing | Log warning, skip earcon, continue flow |
| Capture fails (black frame) | Speak fallback: "I couldn't see the game. Try clicking the game window." |
| STT returns empty | Speak fallback: "I didn't catch that. Try again." |
| LLM timeout (30s) | Speak fallback: "It's taking too long. Try again." |
| LLM returns error | Speak fallback: "I couldn't get an answer right now. Try again." |
| TTS fails | Print answer to console/log; user not completely blocked |
| Config missing required field | Show exact field name and file path in error message |

---

## Testing Plan

### Developer Testing
- [ ] Run hotkey mode for 30 minutes across Hades, FTL, Pyre
- [ ] Verify listening earcon plays on Ctrl+Alt+W
- [ ] Verify thinking chime timing (should not play if model responds < 400ms)
- [ ] Test stop hotkey (Ctrl+Alt+X) interrupts all states
- [ ] Test with intentionally bad API key (verify error message)
- [ ] Build distribution ZIP and test on a clean Windows machine

### Friends Testing (3-5 testers)
- [ ] Can extract ZIP and run without developer assistance
- [ ] Can complete 3 successful Q&A interactions per session
- [ ] Earcons are audible and not annoying
- [ ] Error messages (if any) are understandable

### Exit Criteria
- 80% of testers complete setup without live help
- At least 1 full 30-minute session per tester
- No crashes reported during testing
- Feedback collected on earcon volume/timing preferences

---

## Rollout Plan

1. **Phase 1: Implementation** (This PRD)
   - Add listening earcon with MP3 support
   - Create build script for distribution ZIP
   - Create QUICKSTART.txt

2. **Phase 2: Internal Testing**
   - Developer tests across target games
   - Fix any issues found

3. **Phase 3: Friends Distribution**
   - Upload ZIP to OneDrive
   - Share link with 3-5 testers
   - Collect feedback via informal chat/survey

4. **Phase 4: Iterate**
   - Address critical feedback
   - Rotate API keys if needed
   - Consider broader distribution (separate PRD)

---

## Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Kids ask vague questions | High | Medium | Prompt tuning (separate); suggest "Ask: What should I click?" |
| API keys leak if ZIP shared publicly | Low | High | Clearly label as friends-only; rotate keys monthly |
| Earcon too loud/annoying | Medium | Low | Default volume low (0.12); configurable |
| Game blocks capture (DRM/anti-cheat) | Medium | Medium | Document known issues; fallback message |
| NAudio dependency issues | Low | Medium | Test thoroughly; fallback to synthetic tones |

---

## Open Questions (Resolved)

| Question | Resolution |
|----------|------------|
| State announcements spoken or silent? | **Silent with earcons only** — avoid adding latency and noise |
| Answer length default? | **Not specified in app** — handled via prompt tuning |
| Quickstart format? | **QUICKSTART.txt** — simple text for non-developers |
| Which sound file for listening? | **tap-notification-357575.mp3** — already in Assets |

---

## Implementation Checklist

- [x] Add `NAudio` package to `GameScreenTranslator.csproj`
- [x] Extend `EarconsSettings` with `ListeningChimeEnabled`, `ListeningChimePath`
- [x] Create/extend `EarconPlayer` to support MP3 playback
- [x] Update `TapQuestionAutoStopAsync` to play listening earcon before STT
- [x] Update `ConfigBootstrapper` to extract `Assets/Sounds/*.mp3` from embedded resources
- [x] Update `appsettings.template.json` with new earcon settings
- [x] Create `QUICKSTART.txt`
- [x] Create `scripts/publish-friends.ps1` build script
- [x] Update design docs (`30_runtime_and_configs.md`, `40_hotkeys_and_interaction.md`)
- [ ] Test on target games (Hades, FTL, Pyre) — *manual testing by developer*
- [ ] Create distribution ZIP with pre-filled API keys — *run `scripts/publish-friends.ps1` with your keys*

---

## Appendix: Target Games for Testing

| Game | Notes |
|------|-------|
| Hades | Roguelike with dialogue, ability descriptions, quest text |
| FTL: Faster Than Light | Strategy with event text, ship status, combat messages |
| Pyre | RPG with extensive dialogue and lore text |

These games have significant text UI elements that benefit from translation/explanation for non-English-speaking children.
