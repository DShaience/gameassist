using System;
using GameScreenTranslator.Core.Abstractions;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Logging;

namespace GameScreenTranslator.Core.Audio;

/// <summary>
/// Factory for creating TTS service instances based on configuration.
/// </summary>
public static class TtsServiceFactory
{
    public const string ProviderAzureCognitive = "AzureCognitive";
    public const string ProviderAzureOpenAi = "AzureOpenAi";

    /// <summary>
    /// Creates a TTS service based on the configured provider.
    /// </summary>
    public static ITtsService Create(TtsConfig config)
    {
        if (config is null)
            throw new ArgumentNullException(nameof(config));

        var provider = config.Provider?.Trim() ?? ProviderAzureCognitive;

        Log.Info($"Creating TTS service: {provider}");

        return provider switch
        {
            ProviderAzureCognitive => new AzureCognitiveTtsService(config.AzureCognitive),
            ProviderAzureOpenAi => new AzureOpenAiTtsService(config.AzureOpenAi),
            _ => throw new InvalidOperationException(
                $"Unknown TTS provider: '{provider}'. Valid providers: {ProviderAzureCognitive}, {ProviderAzureOpenAi}")
        };
    }
}
