using System;
using System.IO;
using System.Text.Json;

namespace GameScreenTranslator.Core.Configuration;

/// <summary>
/// Loads AudioConfig from JSON file.
/// </summary>
public static class AudioConfigLoader
{
    public const string DefaultFileName = "audio_config.json";

    public static AudioConfig Load(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, DefaultFileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Audio configuration file not found: {path}. " +
                $"Copy audio_config.template.json to {DefaultFileName} and fill in your API keys.");
        }

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        var config = JsonSerializer.Deserialize<AudioConfig>(json, options);

        if (config is null)
        {
            throw new InvalidOperationException($"Failed to deserialize {DefaultFileName}");
        }

        return config;
    }

    /// <summary>
    /// Loads AudioConfig, or returns null if file doesn't exist (for migration scenarios).
    /// </summary>
    public static AudioConfig? LoadOrNull(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, DefaultFileName);

        if (!File.Exists(path))
            return null;

        return Load(baseDirectory);
    }
}
