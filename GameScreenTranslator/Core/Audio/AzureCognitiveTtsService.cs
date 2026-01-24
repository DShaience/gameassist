using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GameScreenTranslator.Core.Abstractions;
using GameScreenTranslator.Core.Configuration;
using Microsoft.CognitiveServices.Speech;

namespace GameScreenTranslator.Core.Audio;

/// <summary>
/// TTS service using Azure Cognitive Services Speech SDK.
/// </summary>
public sealed class AzureCognitiveTtsService : ITtsService, IAsyncDisposable
{
    private readonly SpeechSynthesizer _synthesizer;
    private readonly AzureCognitiveTtsConfig _config;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AzureCognitiveTtsService(AzureCognitiveTtsConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        SpeechConfig speechConfig;

        if (!string.IsNullOrWhiteSpace(_config.Endpoint))
        {
            speechConfig = SpeechConfig.FromEndpoint(new Uri(_config.Endpoint), _config.ApiKey);

            if (!string.IsNullOrWhiteSpace(_config.Region))
            {
                // Region isn't strictly required when using endpoint+key, but it can help certain auth flows.
                speechConfig.SetProperty("SpeechServiceConnection_Region", _config.Region);
            }
        }
        else
        {
            // Region-based setup (common in Azure Speech docs).
            speechConfig = SpeechConfig.FromSubscription(_config.ApiKey, _config.Region!);
        }

        _synthesizer = new SpeechSynthesizer(speechConfig);
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _synthesizer.StopSpeakingAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TtsTimingInfo> SpeakAsync(string text, string? languageOrLocale, CancellationToken cancellationToken = default)
    {
        var timing = new TtsTimingInfo
        {
            Provider = "AzureCognitive"
        };

        if (string.IsNullOrWhiteSpace(text))
            return timing;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var voice = ResolveVoice(languageOrLocale);
            timing.VoiceName = voice;

            if (!string.IsNullOrWhiteSpace(voice))
            {
                _synthesizer.Properties.SetProperty(PropertyId.SpeechServiceConnection_SynthVoice, voice);
            }

            // Azure Cognitive Services TTS is batch-based: the SDK synthesizes and plays in one call.
            // We measure the total time as "network" time since synthesis happens server-side.
            var networkSw = Stopwatch.StartNew();

            using var result = await _synthesizer.SpeakTextAsync(text).ConfigureAwait(false);

            networkSw.Stop();

            // For batch TTS, network time includes synthesis + download + playback.
            // We report it as network time; playback delay is ~0 since SDK handles it.
            timing.NetworkMs = (int)networkSw.ElapsedMilliseconds;
            timing.PlaybackStartDelayMs = 0; // SDK handles playback internally

            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                return timing;

            if (result.Reason == ResultReason.Canceled)
            {
                var details = SpeechSynthesisCancellationDetails.FromResult(result);
                throw new InvalidOperationException(
                    $"Azure Cognitive TTS canceled. Reason={details.Reason}, ErrorCode={details.ErrorCode}, Details={details.ErrorDetails}");
            }

            throw new InvalidOperationException($"Azure Cognitive TTS failed. Reason={result.Reason}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private string ResolveVoice(string? languageOrLocale)
    {
        if (!string.IsNullOrWhiteSpace(languageOrLocale))
        {
            var normalized = languageOrLocale.Trim();

            if (_config.VoiceByLanguage.TryGetValue(normalized, out var exact))
                return exact;

            // Common language name mappings
            if (normalized.Equals("Hebrew", StringComparison.OrdinalIgnoreCase) &&
                _config.VoiceByLanguage.TryGetValue("he-IL", out var he))
                return he;

            if (normalized.Equals("Russian", StringComparison.OrdinalIgnoreCase) &&
                _config.VoiceByLanguage.TryGetValue("ru-RU", out var ru))
                return ru;

            if (normalized.Equals("English", StringComparison.OrdinalIgnoreCase) &&
                _config.VoiceByLanguage.TryGetValue("en-US", out var en))
                return en;
        }

        return _config.DefaultVoiceName;
    }

    public ValueTask DisposeAsync()
    {
        _synthesizer.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
