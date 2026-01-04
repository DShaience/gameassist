# Game Screen Translator

A Windows desktop assistant that helps children (ages 5–10) access games that lack native translations.

## Setup Instructions

### 1. Configure API Keys

1. Copy `appsettings.template.json` to `appsettings.json`:
   ```bash
   copy appsettings.template.json appsettings.json
   ```

2. Edit `appsettings.json` and add your API credentials:

   **For OpenAI:**
   - Set `"Provider": "OpenAI"`
   - Replace `YOUR_OPENAI_API_KEY_HERE` with your actual OpenAI API key
   - Get your key from: https://platform.openai.com/api-keys

   **For Azure OpenAI:**
   - Set `"Provider": "AzureOpenAI"`
   - Replace `YOUR_AZURE_OPENAI_API_KEY_HERE` with your Azure OpenAI key
   - Update the `Endpoint` with your Azure resource URL
   - Update the `DeploymentName` with your deployment name

3. **Important:** The `appsettings.json` file is gitignored and will never be committed to the repository.

### 2. Customize Settings (Optional)

You can customize various settings in `appsettings.json`:

- **Target Language**: Change `Translation.TargetLanguage` (Hebrew, Russian, etc.)
- **Translation Mode**: Choose between `realistic`, `simplified`, or `explain` (default)
- **Hotkeys**: Customize keyboard shortcuts
- **Audio Ducking**: Adjust game volume reduction percentage
- **Logging**: Control log verbosity and screenshot inclusion

### 3. Run the Application

```bash
dotnet run
```

## Hotkeys (Default)

- **Translate/Cycle**: `Ctrl + Alt + T` - Captures screen and speaks translation
- **Ask Question**: `Ctrl + Alt + Q` (hold) - Record and ask a question about what's on screen
- **Toggle Mode**: `Ctrl + Alt + S` - Switch between translation modes
- **Stop Speaking**: `Ctrl + Alt + X` - Interrupt current audio

## Requirements

- .NET 10.0 SDK
- Windows 10/11
- OpenAI API key or Azure OpenAI access

## Privacy

- All logs are stored locally by default
- Screenshots are only logged when log level is set to `Debug`
- Audio recordings are never uploaded
- See `Logging` section in `appsettings.json` for privacy controls

## Development

See the main project README for development setup and contribution guidelines.
