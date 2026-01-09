# Game Screen Translator
A Windows desktop assistant that helps children (ages 5–10) access games that lack native translations.

## Setup Instructions

### 1. Configure Model & Speech Credentials

This app can now speak output via Azure Speech TTS when you run `--foundry-translate-once`.
To enable this, you must also configure `speech_language_config.json`.

This project uses **multiple services**:
- **Azure Foundry** for the LLM / vision model (translation + explain)
- **Azure Speech** for **TTS** and **STT**

Both are configured via local JSON files that are gitignored.

#### A) Main app config (Azure Foundry + general settings)

1. Copy `appsettings.template.json` to `appsettings.json`:
   ```cmd
   copy appsettings.template.json appsettings.json
   ```

2. Edit `appsettings.json` and fill in:

- `Providers.AzureFoundry.Endpoint`
- `Providers.AzureFoundry.ApiKey`
- `Providers.AzureFoundry.LLM.ChatDeployment`
- `Providers.AzureFoundry.LLM.VisionDeployment`

#### B) Speech config (Azure Speech TTS + STT + languages)

This is required for TTS in the current dev demo.

1. Copy `speech_language_config.template.json` to `speech_language_config.json`:
   ```cmd
   copy speech_language_config.template.json speech_language_config.json
   ```

2. Edit `speech_language_config.json` and fill in:
- `AzureSpeech.Endpoint`
- `AzureSpeech.ApiKey`
- `AzureSpeech.Region` (recommended)

3. **Important:** `appsettings.json` and `speech_language_config.json` are gitignored and will never be committed to the repository.

### 2. Customize Settings (Optional)

You can customize various settings in `appsettings.json`:

- **Target Language**: Change `Translation.TargetLanguage` (Hebrew, Russian, etc.)
- **Translation Mode**: Choose between `realistic`, `simplified`, or `explain` (default)
- **Hotkeys**: Customize keyboard shortcuts
- **Audio Ducking**: Adjust game volume reduction percentage
- **Logging**: Control log verbosity and screenshot inclusion

---

## Quick run commands (dev demos)

All commands below assume you run them from the **repo root**.

### Capture once (prints signature)
```cmd
dotnet run --project GameScreenTranslator/GameScreenTranslator.csproj -- --capture-once
```

### Cycle demo (prints signature + cycle index + priority)
```cmd
dotnet run --project GameScreenTranslator/GameScreenTranslator.csproj -- --cycle-demo
```

### Foundry translate once (vision call)
#### 1) Ensure you have a real config file
Create `GameScreenTranslator\appsettings.json` from the template:

```cmd
copy GameScreenTranslator\appsettings.template.json GameScreenTranslator\appsettings.json
```

Then edit `GameScreenTranslator\appsettings.json` and set real values:
- `Providers.AzureFoundry.Endpoint`  
  Format: `https://<resource-name>.<region>.inference.ai.azure.com`
- `Providers.AzureFoundry.ApiKey`
- `Providers.AzureFoundry.LLM.VisionDeployment`
- `Providers.AzureFoundry.LLM.ChatDeployment`

#### 2) Run
```cmd
dotnet run --project GameScreenTranslator/GameScreenTranslator.csproj -- --foundry-translate-once
```

To print the raw JSON returned by the model (debugging):

```cmd
dotnet run --project GameScreenTranslator/GameScreenTranslator.csproj -- --foundry-translate-once --raw
```

### Hotkey mode (interactive)
Runs a message loop and listens for global hotkeys:

- `Ctrl+Alt+T` = Translate/Cycle (capture active window → Foundry → TTS)
- `Ctrl+Alt+Q` (hold) = Ask Question (STT while held → capture → Foundry answer → TTS)
- `Ctrl+Alt+X` = Stop speaking / cancel

```cmd
dotnet run --project GameScreenTranslator/GameScreenTranslator.csproj -- --hotkey-mode
```

#### Troubleshooting: `No such host is known`
This is almost always a **bad Endpoint** (still a placeholder or typo).

- If your endpoint contains `your-foundry-resource-name` it’s still the template placeholder.
- In Azure Portal / Azure AI Foundry: open your Foundry resource and copy the **inference endpoint** URL. It must be a real DNS name that resolves.

---

## Hotkeys (Default)

- **Translate/Cycle**: `Ctrl + Alt + T` - Captures screen and speaks translation
- **Ask Question**: `Ctrl + Alt + Q` (hold) - Record and ask a question about what's on screen
- **Toggle Mode**: `Ctrl + Alt + S` - Switch between translation modes
- **Stop Speaking**: `Ctrl + Alt + X` - Interrupt current audio

## Requirements

- .NET 10.0 SDK
- Windows 10/11
- Azure Foundry access (LLM / vision)
- Azure Speech resource (TTS + STT)

## Privacy

- All logs are stored locally by default
- Screenshots are only logged when log level is set to `Debug`
- Audio recordings are never uploaded
- See `Logging` section in `appsettings.json` for privacy controls

## Development

See the main project README for development setup and contribution guidelines.