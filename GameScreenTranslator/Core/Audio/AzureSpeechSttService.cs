using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameScreenTranslator.Core.Configuration;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace GameScreenTranslator.Core.Audio;

public sealed class AzureSpeechSttService : IAsyncDisposable
{
    private readonly SpeechLanguageConfig _config;
    private readonly SpeechRecognizer _recognizer;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private StringBuilder? _activeBuffer;

    public AzureSpeechSttService(SpeechLanguageConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        // Minimal P0: use default mic input.
        var audio = AudioConfig.FromDefaultMicrophoneInput();

        var lang = _config.SpeechToText.DefaultRecognitionLanguage;
        if (string.IsNullOrWhiteSpace(lang))
            lang = "en-US";

        var speechConfig = CreateSpeechConfig(_config);

        _recognizer = new SpeechRecognizer(speechConfig, lang, audio);

        _recognizer.Recognized += (_, e) =>
        {
            if (e.Result.Reason != ResultReason.RecognizedSpeech)
                return;

            var text = e.Result.Text;
            if (string.IsNullOrWhiteSpace(text))
                return;

            // Collect recognized chunks while recording.
            _activeBuffer?.Append(text).Append(' ');
        };
    }

    public async Task<string> RecognizeOnceAsync(CancellationToken cancellationToken = default)
    {
        // Note: Speech SDK cancellation is not strongly integrated with CancellationToken.
        var result = await _recognizer.RecognizeOnceAsync().ConfigureAwait(false);

        return result.Reason switch
        {
            ResultReason.RecognizedSpeech => result.Text ?? "",
            ResultReason.NoMatch => "",
            ResultReason.Canceled => ThrowCanceled(result),
            _ => ""
        };
    }

    /// <summary>
    /// Recognize from the microphone and auto-stop after the user stops speaking.
    /// Uses Azure Speech end-of-speech silence timeout.
    /// </summary>
    public async Task<string> RecognizeOnceWithEndSilenceTimeoutAsync(
        int endSilenceTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        if (endSilenceTimeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(endSilenceTimeoutMs), "Must be > 0.");

        // Prevent overlap with StartAsync/StopAsync and other STT calls.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var audio = AudioConfig.FromDefaultMicrophoneInput();
            var lang = _config.SpeechToText.DefaultRecognitionLanguage;
            if (string.IsNullOrWhiteSpace(lang))
                lang = "en-US";

            var speechConfig = CreateSpeechConfig(_config, endSilenceTimeoutMs);

            using var recognizer = new SpeechRecognizer(speechConfig, lang, audio);

            var result = await recognizer.RecognizeOnceAsync().ConfigureAwait(false);

            return result.Reason switch
            {
                ResultReason.RecognizedSpeech => (result.Text ?? "").Trim(),
                ResultReason.NoMatch => "",
                ResultReason.Canceled => ThrowCanceled(result),
                _ => ""
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Start continuous recognition. Call <see cref="StopAsync"/> to end and get the collected text.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _activeBuffer = new StringBuilder();
            await _recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Stop continuous recognition and return the collected text.
    /// </summary>
    public async Task<string> StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
            var text = _activeBuffer?.ToString() ?? "";
            _activeBuffer = null;
            return text.Trim();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static SpeechConfig CreateSpeechConfig(SpeechLanguageConfig config, int? endSilenceTimeoutMs = null)
    {
        SpeechConfig speechConfig;

        if (!string.IsNullOrWhiteSpace(config.AzureSpeech.Endpoint))
        {
            speechConfig = SpeechConfig.FromEndpoint(new Uri(config.AzureSpeech.Endpoint), config.AzureSpeech.ApiKey);

            if (!string.IsNullOrWhiteSpace(config.AzureSpeech.Region))
            {
                speechConfig.SetProperty("SpeechServiceConnection_Region", config.AzureSpeech.Region);
            }
        }
        else
        {
            // Region-based setup (common in Azure Speech docs).
            speechConfig = SpeechConfig.FromSubscription(config.AzureSpeech.ApiKey, config.AzureSpeech.Region!);
        }

        if (endSilenceTimeoutMs is not null)
        {
            // How long to wait after user stops speaking before finalizing.
            // (PropertyId is supported by the Speech SDK; string key is also accepted.)
            speechConfig.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, endSilenceTimeoutMs.Value.ToString());
        }

        return speechConfig;
    }

    private static string ThrowCanceled(SpeechRecognitionResult result)
    {
        var details = CancellationDetails.FromResult(result);
        throw new InvalidOperationException(
            $"Azure Speech STT canceled. Reason={details.Reason}, ErrorCode={details.ErrorCode}, Details={details.ErrorDetails}");
    }

    public ValueTask DisposeAsync()
    {
        _recognizer.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}