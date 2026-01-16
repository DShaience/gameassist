using System;
using GameScreenTranslator.Core.Abstractions;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Logging;

namespace GameScreenTranslator.Core.Audio;

/// <summary>
/// Factory for creating STT service instances based on configuration.
/// </summary>
public static class SttServiceFactory
{
    public const string ProviderAzureCognitive = "AzureCognitive";
    // Future: public const string ProviderAzureOpenAiWhisper = "AzureOpenAiWhisper";

    /// <summary>
    /// Creates an STT service based on the configured provider.
    /// </summary>
    public static ISttService Create(SttConfig config)
    {
        if (config is null)
            throw new ArgumentNullException(nameof(config));

        var provider = config.Provider?.Trim() ?? ProviderAzureCognitive;

        Log.Info($"Creating STT service: {provider}");

        return provider switch
        {
            ProviderAzureCognitive => new AzureCognitiveSttService(config.AzureCognitive),
            // Future: ProviderAzureOpenAiWhisper => new AzureOpenAiWhisperSttService(config.AzureOpenAiWhisper),
            _ => throw new InvalidOperationException(
                $"Unknown STT provider: '{provider}'. Valid providers: {ProviderAzureCognitive}")
        };
    }
}
