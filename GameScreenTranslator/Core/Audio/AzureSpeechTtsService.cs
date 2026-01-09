using System;
using System.Threading;
using System.Threading.Tasks;
using GameScreenTranslator.Core.Configuration;
using Microsoft.CognitiveServices.Speech;

namespace GameScreenTranslator.Core.Audio;

public sealed class AzureSpeechTtsService : IAsyncDisposable
{
    private readonly SpeechSynthesizer _synthesizer;
    private readonly SpeechLanguageConfig _config;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AzureSpeechTtsService(SpeechLanguageConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        SpeechConfig speechConfig;

        if (!string.IsNullOrWhiteSpace(_config.AzureSpeech.Endpoint))
        {
            speechConfig = SpeechConfig.FromEndpoint(new Uri(_config.AzureSpeech.Endpoint), _config.AzureSpeech.ApiKey);

            if (!string.IsNullOrWhiteSpace(_config.AzureSpeech.Region))
            {
                // Region isn't strictly required when using endpoint+key, but it can help certain auth flows.
                speechConfig.SetProperty("SpeechServiceConnection_Region", _config.AzureSpeech.Region);
            }
        }
        else
        {
            // Region-based setup (common in Azure Speech docs).
            speechConfig = SpeechConfig.FromSubscription(_config.AzureSpeech.ApiKey, _config.AzureSpeech.Region!);
        }

        // We'll set voice per-call (SpeakAsync).
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

    public async Task SpeakAsync(string text, string? languageOrLocale, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var voice = ResolveVoice(languageOrLocale);
            if (!string.IsNullOrWhiteSpace(voice))
            {
                _synthesizer.Properties.SetProperty(PropertyId.SpeechServiceConnection_SynthVoice, voice);
            }

            // Note: Speech SDK cancellation is not strongly integrated with CancellationToken.
            // We still accept it for future extension (and for consistent API shape).
            using var result = await _synthesizer.SpeakTextAsync(text).ConfigureAwait(false);

            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                return;

            if (result.Reason == ResultReason.Canceled)
            {
                var details = SpeechSynthesisCancellationDetails.FromResult(result);
                throw new InvalidOperationException(
                    $"Azure Speech TTS canceled. Reason={details.Reason}, ErrorCode={details.ErrorCode}, Details={details.ErrorDetails}");
            }

            throw new InvalidOperationException($"Azure Speech TTS failed. Reason={result.Reason}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private string ResolveVoice(string? languageOrLocale)
    {
        // Prefer an explicit locale if provided and mapped.
        if (!string.IsNullOrWhiteSpace(languageOrLocale))
        {
            // If caller passed "Hebrew"/"Russian", try mapping by common cases.
            var normalized = languageOrLocale.Trim();

            if (_config.TextToSpeech.VoiceByLanguage.TryGetValue(normalized, out var exact))
                return exact;

            if (normalized.Equals("Hebrew", StringComparison.OrdinalIgnoreCase))
            {
                if (_config.TextToSpeech.VoiceByLanguage.TryGetValue("he-IL", out var he))
                    return he;
            }

            if (normalized.Equals("Russian", StringComparison.OrdinalIgnoreCase))
            {
                if (_config.TextToSpeech.VoiceByLanguage.TryGetValue("ru-RU", out var ru))
                    return ru;
            }

            if (normalized.Equals("English", StringComparison.OrdinalIgnoreCase))
            {
                if (_config.TextToSpeech.VoiceByLanguage.TryGetValue("en-US", out var en))
                    return en;
            }
        }

        return _config.TextToSpeech.DefaultVoiceName;
    }

    public ValueTask DisposeAsync()
    {
        _synthesizer.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
