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
    /// Validates AppSettings, PromptPack, and SpeechLanguageConfig.
    /// Throws <see cref="ConfigValidationException"/> if any required fields are missing.
    /// </summary>
    public static void Validate(
        string baseDir,
        AppSettings settings,
        PromptPack prompts,
        SpeechLanguageConfig speechConfig)
    {
        var errors = new List<string>();

        ValidateAppSettings(settings, errors);
        ValidatePrompts(prompts, errors);
        ValidateSpeechConfig(baseDir, settings, speechConfig, errors);

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

    private static void ValidateSpeechConfig(
        string baseDir,
        AppSettings settings,
        SpeechLanguageConfig speechConfig,
        List<string> errors)
    {
        var configPath = settings.Providers.AzureSpeech.ConfigPath;

        // Check if speech config file actually exists
        if (string.IsNullOrWhiteSpace(configPath))
        {
            errors.Add(
                "Providers.AzureSpeech.ConfigPath is empty in appsettings.json. " +
                "Please set the path to your speech_language_config.json file.");
            return;
        }

        var fullPath = Path.IsPathRooted(configPath)
            ? configPath
            : Path.Combine(baseDir, configPath);

        if (!File.Exists(fullPath))
        {
            errors.Add(
                $"Speech config file not found at '{fullPath}'. " +
                "Run the app once to create a template, then fill in your Azure Speech credentials.");
            return;
        }

        // Azure Speech credentials
        if (string.IsNullOrWhiteSpace(speechConfig.AzureSpeech.ApiKey))
        {
            errors.Add(
                $"AzureSpeech.ApiKey is empty in '{configPath}'. " +
                "Please add your Azure Speech API key.");
        }

        // Must have either Endpoint or Region
        if (string.IsNullOrWhiteSpace(speechConfig.AzureSpeech.Endpoint) &&
            string.IsNullOrWhiteSpace(speechConfig.AzureSpeech.Region))
        {
            errors.Add(
                $"AzureSpeech requires either Endpoint or Region in '{configPath}'. " +
                "Please add your Azure Speech endpoint URL or region code (e.g., 'westeurope').");
        }
    }
}
