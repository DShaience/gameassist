using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace GameScreenTranslator.Core.Configuration;

public static class AppSettingsLoader
{
    public static AppSettings Load(string baseDirectory, string appSettingsFileName = "appsettings.json")
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentException("Base directory cannot be empty.", nameof(baseDirectory));

        var resolvedBaseDir = ResolveConfigDirectory(baseDirectory, appSettingsFileName);
        var path = Path.Combine(resolvedBaseDir, appSettingsFileName);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(resolvedBaseDir)
            .AddJsonFile(appSettingsFileName, optional: false)
            .Build();

        var settings = new AppSettings();
        configuration.Bind(settings);

        // Minimal validation for P0
        if (string.IsNullOrWhiteSpace(settings.Providers.AzureFoundry.Endpoint))
            throw new InvalidOperationException($"Missing config: Providers:AzureFoundry:Endpoint ({path})");

        // Catch the common template placeholder case early (avoids DNS error later).
        if (settings.Providers.AzureFoundry.Endpoint.Contains("your-foundry-resource-name", StringComparison.OrdinalIgnoreCase) ||
            settings.Providers.AzureFoundry.Endpoint.Contains("YOUR-FOUNDRY-RESOURCE-NAME", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Providers:AzureFoundry:Endpoint is still set to a template placeholder: '{settings.Providers.AzureFoundry.Endpoint}'. " +
                $"Replace it with your real Foundry inference endpoint URL (starts with https:// and ends with inference.ai.azure.com). File: {path}");
        }

        if (!settings.Providers.AzureFoundry.Endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Providers:AzureFoundry:Endpoint must start with https:// ({path})");

        if (string.IsNullOrWhiteSpace(settings.Providers.AzureFoundry.ApiKey))
            throw new InvalidOperationException($"Missing config: Providers:AzureFoundry:ApiKey ({path})");
        if (string.IsNullOrWhiteSpace(settings.Providers.AzureFoundry.LLM.ChatDeployment))
            throw new InvalidOperationException($"Missing config: Providers:AzureFoundry:LLM:ChatDeployment ({path})");
        if (string.IsNullOrWhiteSpace(settings.Providers.AzureFoundry.LLM.VisionDeployment))
            throw new InvalidOperationException($"Missing config: Providers:AzureFoundry:LLM:VisionDeployment ({path})");

        return settings;
    }

    private static string ResolveConfigDirectory(string baseDirectory, string appSettingsFileName)
    {
        // Common cases during dev:
        // - User runs from repo root (Directory.GetCurrentDirectory() == repo root)
        // - User runs from GameScreenTranslator/ directory
        // Support both.
        var direct = Path.Combine(baseDirectory, appSettingsFileName);
        if (File.Exists(direct))
            return baseDirectory;

        var underProject = Path.Combine(baseDirectory, "GameScreenTranslator", appSettingsFileName);
        if (File.Exists(underProject))
            return Path.Combine(baseDirectory, "GameScreenTranslator");

        // Give a helpful actionable error message (without creating files automatically).
        throw new FileNotFoundException(
            $"Configuration file '{appSettingsFileName}' was not found. " +
            $"Create it next to the project. For example, copy 'GameScreenTranslator{Path.DirectorySeparatorChar}appsettings.template.json' " +
            $"to 'GameScreenTranslator{Path.DirectorySeparatorChar}{appSettingsFileName}'. " +
            $"Searched: '{direct}', '{underProject}'.",
            direct);
    }
}
