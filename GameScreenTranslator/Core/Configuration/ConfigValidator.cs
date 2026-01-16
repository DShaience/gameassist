using System;
using System.Collections.Generic;
using System.IO;

namespace GameScreenTranslator.Core.Configuration;

/// <summary>
/// Thrown when configuration is invalid or incomplete.
/// Message is user-friendly and actionable.
/// </summary>
public sealed class ConfigValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ConfigValidationException(IReadOnlyList<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    private static string BuildMessage(IReadOnlyList<string> errors)
    {
        if (errors.Count == 0)
            return "Configuration validation failed.";

        if (errors.Count == 1)
            return errors[0];

        return $"Configuration validation failed with {errors.Count} errors:\n- " + string.Join("\n- ", errors);
    }
}

/// <summary>
/// Validates loaded configuration and provides actionable error messages.
/// </summary>
public static class ConfigValidator
{
    /// <summary>
    /// Validates AppSettings, PromptPack, and AudioConfig.
    /// Throws <see cref="ConfigValidationException"/> if any required fields are missing.
    /// </summary>
    public static void Validate(
        string baseDir,
        AppSettings settings,
        PromptPack prompts,
        AudioConfig audioConfig)
    {
        var errors = new List<string>();

        ValidateAppSettings(settings, errors);
        ValidatePrompts(prompts, errors);
        ValidateAudioConfig(baseDir, audioConfig, errors);

        if (errors.Count > 0)
            throw new ConfigValidationException(errors);
    }

    private static void ValidateAppSettings(AppSettings settings, List<string> errors)
    {
        // Azure Foundry
        if (string.IsNullOrWhiteSpace(settings.Providers.AzureFoundry.Endpoint))
        {
            errors.Add(
                "Providers.AzureFoundry.Endpoint is empty in appsettings.json. " +
                "Please add your Azure Foundry (or Azure OpenAI) endpoint URL.");
        }

        if (string.IsNullOrWhiteSpace(settings.Providers.AzureFoundry.ApiKey))
        {
            errors.Add(
                "Providers.AzureFoundry.ApiKey is empty in appsettings.json. " +
                "Please add your Azure Foundry API key.");
        }

        if (string.IsNullOrWhiteSpace(settings.Providers.AzureFoundry.LLM.VisionDeployment))
        {
            errors.Add(
                "Providers.AzureFoundry.LLM.VisionDeployment is empty in appsettings.json. " +
                "Please add the name of your vision-capable model deployment (e.g., 'gpt-4o').");
        }

        // Translation
        if (string.IsNullOrWhiteSpace(settings.Translation.TargetLanguage))
        {
            errors.Add(
                "Translation.TargetLanguage is empty in appsettings.json. " +
                "Please set the language you want translations spoken in (e.g., 'Hebrew', 'Russian').");
        }
    }

    private static void ValidatePrompts(PromptPack prompts, List<string> errors)
    {
        if (prompts.Foundry is null)
        {
            errors.Add(
                "prompts.yaml is missing the 'Foundry' section. " +
                "Please ensure configs/prompts.yaml contains a 'foundry:' block with system and user templates.");
            return;
        }

        if (string.IsNullOrWhiteSpace(prompts.Foundry.System))
        {
            errors.Add(
                "prompts.yaml is missing 'foundry.system'. " +
                "Please add a system prompt for the vision model.");
        }

        if (string.IsNullOrWhiteSpace(prompts.Foundry.TranslateUser))
        {
            errors.Add(
                "prompts.yaml is missing 'foundry.translate_user'. " +
                "Please add a user prompt template for translate/cycle operations.");
        }
    }

    private static void ValidateAudioConfig(
        string baseDir,
        AudioConfig audioConfig,
        List<string> errors)
    {
        const string configFileName = "audio_config.json";
        var fullPath = Path.Combine(baseDir, configFileName);

        if (!File.Exists(fullPath))
        {
            errors.Add(
                $"Audio config file not found at '{fullPath}'. " +
                "Copy audio_config.template.json to audio_config.json and fill in your API keys.");
            return;
        }

        // Validate TTS config based on selected provider
        var ttsProvider = audioConfig.Tts.Provider?.Trim() ?? "AzureCognitive";

        if (ttsProvider.Equals("AzureCognitive", StringComparison.OrdinalIgnoreCase))
        {
            var ttsCfg = audioConfig.Tts.AzureCognitive;

            if (string.IsNullOrWhiteSpace(ttsCfg.ApiKey))
            {
                errors.Add(
                    $"Tts.AzureCognitive.ApiKey is empty in '{configFileName}'. " +
                    "Please add your Azure Cognitive Services Speech API key.");
            }

            if (string.IsNullOrWhiteSpace(ttsCfg.Endpoint) && string.IsNullOrWhiteSpace(ttsCfg.Region))
            {
                errors.Add(
                    $"Tts.AzureCognitive requires either Endpoint or Region in '{configFileName}'. " +
                    "Please add your Azure Speech endpoint URL or region code (e.g., 'swedencentral').");
            }
        }
        else if (ttsProvider.Equals("AzureOpenAi", StringComparison.OrdinalIgnoreCase))
        {
            var ttsCfg = audioConfig.Tts.AzureOpenAi;

            if (string.IsNullOrWhiteSpace(ttsCfg.Endpoint))
            {
                errors.Add(
                    $"Tts.AzureOpenAi.Endpoint is empty in '{configFileName}'. " +
                    "Please add your Azure OpenAI endpoint URL.");
            }

            if (string.IsNullOrWhiteSpace(ttsCfg.ApiKey))
            {
                errors.Add(
                    $"Tts.AzureOpenAi.ApiKey is empty in '{configFileName}'. " +
                    "Please add your Azure OpenAI API key.");
            }
        }

        // Validate STT config based on selected provider
        var sttProvider = audioConfig.Stt.Provider?.Trim() ?? "AzureCognitive";

        if (sttProvider.Equals("AzureCognitive", StringComparison.OrdinalIgnoreCase))
        {
            var sttCfg = audioConfig.Stt.AzureCognitive;

            if (string.IsNullOrWhiteSpace(sttCfg.ApiKey))
            {
                errors.Add(
                    $"Stt.AzureCognitive.ApiKey is empty in '{configFileName}'. " +
                    "Please add your Azure Cognitive Services Speech API key.");
            }

            if (string.IsNullOrWhiteSpace(sttCfg.Endpoint) && string.IsNullOrWhiteSpace(sttCfg.Region))
            {
                errors.Add(
                    $"Stt.AzureCognitive requires either Endpoint or Region in '{configFileName}'. " +
                    "Please add your Azure Speech endpoint URL or region code (e.g., 'swedencentral').");
            }
        }
    }
}
