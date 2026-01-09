using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace GameScreenTranslator.Core.Configuration;

public static class SpeechLanguageConfigLoader
{
    public static SpeechLanguageConfig Load(string baseDirectory, string configFileName)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentException("Base directory cannot be empty.", nameof(baseDirectory));
        if (string.IsNullOrWhiteSpace(configFileName))
            throw new ArgumentException("Config file name cannot be empty.", nameof(configFileName));

        var resolvedBaseDir = ResolveConfigDirectory(baseDirectory, configFileName);
        var path = Path.Combine(resolvedBaseDir, configFileName);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(resolvedBaseDir)
            .AddJsonFile(configFileName, optional: false)
            .Build();

        var config = new SpeechLanguageConfig();
        configuration.Bind(config);

        if (string.IsNullOrWhiteSpace(config.AzureSpeech.ApiKey))
            throw new InvalidOperationException($"Missing config: AzureSpeech:ApiKey ({path})");

        // Allow either a full endpoint OR a region (we can use FromSubscription when region is provided).
        if (string.IsNullOrWhiteSpace(config.AzureSpeech.Endpoint) && string.IsNullOrWhiteSpace(config.AzureSpeech.Region))
            throw new InvalidOperationException($"Missing config: AzureSpeech:Endpoint or AzureSpeech:Region ({path})");

        if (string.IsNullOrWhiteSpace(config.TextToSpeech.DefaultVoiceName))
            throw new InvalidOperationException($"Missing config: TextToSpeech:DefaultVoiceName ({path})");

        return config;
    }

    private static string ResolveConfigDirectory(string baseDirectory, string configFileName)
    {
        // Support running from repo root or project directory.
        var direct = Path.Combine(baseDirectory, configFileName);
        if (File.Exists(direct))
            return baseDirectory;

        var underProject = Path.Combine(baseDirectory, "GameScreenTranslator", configFileName);
        if (File.Exists(underProject))
            return Path.Combine(baseDirectory, "GameScreenTranslator");

        throw new FileNotFoundException(
            $"Configuration file '{configFileName}' was not found. " +
            $"Create it next to the project. For example, copy 'GameScreenTranslator{Path.DirectorySeparatorChar}{configFileName.Replace(".json", ".template.json")}' " +
            $"to 'GameScreenTranslator{Path.DirectorySeparatorChar}{configFileName}'. " +
            $"Searched: '{direct}', '{underProject}'.",
            direct);
    }
}