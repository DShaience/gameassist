# Large-rock cleanups & refactors (backlog)

Purpose: capture high-level refactor opportunities to improve readability, testability, and maintainability. Keep this list "big rocks" only; break into smaller tasks when we prioritize an item.

---

## 1) Split `HotkeyAppRunner` into flows + a thin orchestrator

**Status:** Done (hold-to-talk removed; tap-to-talk retained; pipeline scaffolding + tests added)

**Problem:** `HotkeyAppRunner` owns hotkey wiring, keyboard hooks, screen capture, screen-state cycling, conversation history, STT/TTS lifecycles, Foundry calls, debug capture saving, and interaction logging. This creates a "god class" that is difficult to test and evolve safely.

**Suggestion:**
- Extract dedicated "flow" classes (single responsibility, easy to unit test):
  - `TranslateCycleFlow` (capture → signature/state → Foundry → TTS → interaction log)
  - `TapToTalkQuestionFlow`
  - `ResetConversationFlow`
  - `StopFlow` (cancel + stop TTS + log)
- Keep `HotkeyAppRunner` as a coordinator that maps hotkeys/events to flows.

**Design note (to reduce duplication): Pipeline + Context**
Instead of "one big method per flow" (which duplicates timers, try/catch, logging, disposal), use a small pipeline framework:

- Each flow has a `*Context` (holds capture, question, response, timers, error, etc.)
- Each flow is composed from reusable `IOperationStep<TContext>` steps (capture, model call, speak, log, cleanup, etc.)
- The pipeline runner owns the consistent try/catch/finally + timing hooks (or you add explicit `LogStep` / `CleanupStep` at the end).

This is **TDD-friendly**: steps are small units with fakes.

**Decision:** skip `HoldToTalkQuestionFlow` (tap-to-talk is preferred).

---

## 2) Introduce interfaces for IO/external dependencies + simple DI

**Status:** Done – interfaces added and implemented; ready for injection

**Problem:** Many dependencies are constructed directly (`new AzureFoundryClient`, `new ActiveWindowCapture`, etc.), which locks in implementations and makes testing orchestration logic hard.

**Suggestion:**
- Add small interfaces and inject them into flows/runner:
  - `ICaptureService`, `IFoundryClient`, `ITtsService`, `ISttService`, `IInteractionLogger`, `IScreenStateManager`, `IClock` (optional)
- Use either:
  - "manual DI" (construct in `Program.cs`), or
  - `Microsoft.Extensions.DependencyInjection` for composition (no need for full Generic Host if you want to keep it light).

**What was done:**
- Created `GameScreenTranslator/Core/Abstractions/` folder with interfaces:
  - `ICaptureService`, `IFoundryClient`, `ITtsService`, `ISttService`, `IInteractionLogger`, `IScreenStateManager`
- Implemented interfaces on existing concrete classes:
  - `ActiveWindowCapture : ICaptureService`
  - `ScreenStateManager : IScreenStateManager`
  - `InteractionLogger : IInteractionLogger`
  - `AzureSpeechTtsService : ITtsService`
  - `AzureSpeechSttService : ISttService`
  - `AzureFoundryClient : IFoundryClient`
- Build + tests pass. HotkeyAppRunner and Program.cs still use concrete types (optional follow-up: wire through interfaces for full DI).

---

## 3) Convert `Program.cs` to command/handler structure (remove the if/else chain)

**Status:** Done – command pattern + centralized bootstrapping

**Problem:** `Program.cs` mixes CLI mode routing with business logic and has repeated bootstrapping (resolve base dir, ensure config, load settings/prompts/speech, configure logging).

**What "repeated bootstrapping" means:**
Before the refactor, every if/else branch in Program.cs ran the same setup steps (resolve base dir → ensure template configs → load settings/prompts/speech → configure logging). If you added a new mode or changed the load order, you had to repeat or update multiple code paths. That's "repeated bootstrapping".

**Suggestion:**
- Introduce a simple command router pattern (or adopt `System.CommandLine`):
  - `hotkey` (default)
  - `capture-once`
  - `cycle-demo`
  - `foundry-translate-once`
- Centralize startup in a `RuntimeBootstrapper` that returns a typed `RuntimeContext`:
  - baseDir, settings, prompts, speech config, logs dir, etc.

**What was done:**
- Created `RuntimeBootstrapper` + `RuntimeContext` to run all startup once.
- Added `ICliCommand` interface with `RequiresFullBootstrap` flag.
- Implemented four command classes:
  - `HotkeyModeCommand` (`--hotkey-mode`, default)
  - `CaptureOnceCommand` (`--capture-once`)
  - `CycleDemoCommand` (`--cycle-demo`)
  - `FoundryTranslateOnceCommand` (`--foundry-translate-once`)
- Rewrote `Program.cs` to:
  1. Register all commands
  2. Match the first arg (or default to `--hotkey-mode`)
  3. Bootstrap once (only if the matched command needs it)
  4. Run the matched command

Command-line behavior is identical (`GameScreenTranslator.exe`, `--capture-once`, `--help`, etc.).

---

## 4) Centralize configuration loading + validation

**Problem:** Defaults/paths/validation are spread across templates and multiple call sites; errors are handled differently per mode.

**Suggestion:**
- Add `RuntimeConfigLoader` (or extend `AppSettingsLoader`) to:
  - resolve base directory
  - ensure first-run template creation
  - load + validate settings/prompts/speech config
  - provide actionable validation errors (missing endpoint, missing deployment, missing speech config path, etc.)

---

## 5) Unify cross-cutting concerns: logging, debug output, error handling

**Problem:** Mixed `Console.WriteLine` and `Log.*`, plus scattered "best-effort ignore" try/catch blocks, plus debug capture saving sprinkled into flow logic.

**Suggestion:**
- Pick one logging approach:
  - keep the existing `Log` wrapper, but route all output through it, OR
  - move to `Microsoft.Extensions.Logging` (single abstraction)
- Gate debug-only behaviors behind config + log level:
  - raw Foundry output
  - saving screenshot captures
  - embedding screenshots in interaction logs
  - including conversation context in logs
- Introduce a tiny helper for intentional best-effort operations (so ignores are explicit and consistent).

---

## 6) Split `AzureFoundryClient` responsibilities (API vs prompts vs parsing)

**Problem:** `AzureFoundryClient` handles HTTP, retries, compatibility fallbacks, screenshot processing, prompt selection/rendering, response parsing, response normalization, and also conversation summarization prompting.

**Suggestion:**
- Break into:
  - `FoundryHttpClient` (HTTP + retries + compatibility fallbacks)
  - `FoundryRequestBuilder` (templates/messages/response format)
  - `FoundryResponseParser` (deserialize + normalize)
  - `ConversationSummarizer` (text-only summarization strategy)
- Outcome: smaller classes, fewer reasons to change, easier unit testing.

---

## 7) ~~Remove duplication between "hold-to-talk" and "tap-to-talk" question flows~~

**Status:** N/A – hold-to-talk was removed in item (1), so there's only one question flow (tap-to-talk). No duplication exists.

---

## 8) Tests: re-balance toward "logic under change" and pure units

**Problem:** Core risk/complexity sits in orchestration and normalization logic, but those are hard to unit test without seams (interfaces).

**Suggestion:**
- Add unit tests for:
  - Foundry output normalization (`CleanUiText`, key/value heuristic, wrapper stripping)
  - conversation logging text construction (privacy flags + log-level gates)
  - flows using fakes (enabled by interfaces)
- Keep existing tests for pure utilities (conversation builder, state, signature) as-is.

---

## 9) Repo/project hygiene (structure)

**Suggestion:**
- Decide whether `samples/` are:
  - build artifacts of the repo (keep in solution but separate solution folder), or
  - reference examples (exclude from main build pipeline)
- Consider keeping `Core` strictly "domain + services" and isolating platform-specific wiring (WinForms tray, keyboard hooks) in the app project.
- Align naming (`gamer-assisstant` vs `GameScreenTranslator`) if this repo will be long-lived.

---

## Suggested prioritization (initial)

1. **Split `HotkeyAppRunner` into flows** (largest readability/testability win)
2. **Add interfaces + injection** (enables safe refactors + proper unit tests)
3. **Refactor `Program.cs` into command handlers + shared bootstrap** (removes branching + duplication)
4. **Split `AzureFoundryClient` responsibilities** (clean boundaries)
5. ~~**Unify the question flows** (remove duplication)~~ – N/A, only one question flow remains
