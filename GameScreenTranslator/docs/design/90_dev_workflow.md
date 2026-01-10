# Dev Workflow (P0) + Documentation Update Rule
Last updated: 2026-01-10

## Mandatory rule: update design docs with code changes
Any change that affects behavior, UX, configuration keys/defaults, prompts, logging schema, model contract, or architecture **must include updating** the relevant file(s) under:
- `GameScreenTranslator/docs/design/`

PR checklist item (required):
- [ ] Updated `GameScreenTranslator/docs/design/*` to match the change (or confirmed explicitly “no doc impact” in PR description)

If you add a new subsystem or config file:
- add a new design doc (or extend an existing one), and link it from `00_index.md`.

## Build
From repo root:
```cmd
dotnet build GameScreenTranslator/GameScreenTranslator.csproj -c Debug
```

## Run (dev)
Hotkey mode (default):
```cmd
dotnet run --project GameScreenTranslator/GameScreenTranslator.csproj --
```

Capture once:
```cmd
dotnet run --project GameScreenTranslator/GameScreenTranslator.csproj -- --capture-once
```

Cycle demo:
```cmd
dotnet run --project GameScreenTranslator/GameScreenTranslator.csproj -- --cycle-demo
```

Single Foundry call (+ optional raw):
```cmd
dotnet run --project GameScreenTranslator/GameScreenTranslator.csproj -- --foundry-translate-once
dotnet run --project GameScreenTranslator/GameScreenTranslator.csproj -- --foundry-translate-once --raw
```

## Tests
```cmd
dotnet test GameScreenTranslator.Tests/GameScreenTranslator.Tests.csproj -c Debug
```

## Configuration (local-only secrets)
Required local files (gitignored):
- `GameScreenTranslator/appsettings.json` (copy from `appsettings.template.json`)
- `GameScreenTranslator/speech_language_config.json` (copy from `speech_language_config.template.json`)

Prompts:
- Defaults: `GameScreenTranslator/configs/prompts.template.yaml` (embedded / committed)
- Runtime editable: `GameScreenTranslator/configs/prompts.yaml`

## Publish (portable single EXE)
```cmd
dotnet publish GameScreenTranslator\\GameScreenTranslator.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true
```

Output:
- `GameScreenTranslator\\bin\\Release\\net10.0-windows\\win-x64\\publish\\GameScreenTranslator.exe`

First run next to the EXE will auto-create (if missing):
- `appsettings.json`
- `speech_language_config.json`
- `configs/prompts.yaml`

## Operational notes (P0)
- Some games may prevent capture (black frames). Capture uses `PrintWindow` with `CopyFromScreen` fallback.
- Screenshot logging may contain sensitive data; see `60_logging_and_privacy.md` before enabling.
