using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameScreenTranslator.Core.Abstractions;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Logging;
using NAudio.Wave;

namespace GameScreenTranslator.Core.Audio;

/// <summary>
/// TTS service using Azure OpenAI TTS-HD deployment.
/// Calls the REST API and plays returned MP3 audio via NAudio.
/// </summary>
public sealed class AzureOpenAiTtsService : ITtsService, IAsyncDisposable
{
    private readonly OpenAiTtsConfig _config;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    
    private WaveOutEvent? _waveOut;
    private CancellationTokenSource? _playbackCts;

    public AzureOpenAiTtsService(OpenAiTtsConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));
    }

    public async Task SpeakAsync(string text, string? languageOrLocale, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Stop any current playback
            StopPlayback();

            var voice = ResolveVoice(languageOrLocale);
            var audioBytes = await SynthesizeAsync(text, voice, cancellationToken).ConfigureAwait(false);
            
            await PlayAudioAsync(audioBytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            StopPlayback();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<byte[]> SynthesizeAsync(string text, string voice, CancellationToken cancellationToken)
    {
        var endpoint = _config.Endpoint.TrimEnd('/');
        var deployment = _config.Deployment;
        var apiVersion = _config.ApiVersion;
        
        var url = $"{endpoint}/openai/deployments/{deployment}/audio/speech?api-version={apiVersion}";

        var requestBody = new
        {
            model = deployment,
            input = text,
            voice = voice,
            response_format = "mp3"
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        Log.Debug($"[OpenAI TTS] POST {url}, voice={voice}, text length={text.Length}");

        using var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"OpenAI TTS failed. Status={response.StatusCode}, Body={errorBody}");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task PlayAudioAsync(byte[] mp3Bytes, CancellationToken cancellationToken)
    {
        _playbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tcs = new TaskCompletionSource<bool>();

        using var ms = new MemoryStream(mp3Bytes);
        using var mp3Reader = new Mp3FileReader(ms);
        
        _waveOut = new WaveOutEvent();
        _waveOut.Init(mp3Reader);

        _waveOut.PlaybackStopped += (_, _) =>
        {
            tcs.TrySetResult(true);
        };

        _waveOut.Play();

        // Wait for playback to complete or cancellation
        using var registration = _playbackCts.Token.Register(() =>
        {
            _waveOut?.Stop();
            tcs.TrySetCanceled();
        });

        try
        {
            await tcs.Task.ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            // Expected when StopAsync is called
        }
        finally
        {
            _waveOut?.Dispose();
            _waveOut = null;
        }
    }

    private void StopPlayback()
    {
        try
        {
            _playbackCts?.Cancel();
        }
        catch
        {
            // Ignore
        }

        try
        {
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _waveOut = null;
        }
        catch
        {
            // Ignore
        }
    }

    private string ResolveVoice(string? languageOrLocale)
    {
        if (!string.IsNullOrWhiteSpace(languageOrLocale))
        {
            var normalized = languageOrLocale.Trim();

            if (_config.VoiceByLanguage.TryGetValue(normalized, out var mapped))
                return mapped;

            // Try common language name mappings
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

        return _config.DefaultVoice;
    }

    public ValueTask DisposeAsync()
    {
        StopPlayback();
        _httpClient.Dispose();
        _gate.Dispose();
        _playbackCts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
