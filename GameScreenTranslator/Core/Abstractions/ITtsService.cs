using System.Threading;
using System.Threading.Tasks;

namespace GameScreenTranslator.Core.Abstractions;

/// <summary>
/// Timing information from a TTS operation for latency analysis.
/// </summary>
public sealed class TtsTimingInfo
{
    /// <summary>
    /// Milliseconds from request start until first audio bytes received.
    /// For streaming TTS, this is when the first chunk arrives.
    /// For batch TTS, this is when the audio is fully synthesized.
    /// </summary>
    public int? NetworkMs { get; set; }

    /// <summary>
    /// Milliseconds from first audio bytes to playback start.
    /// This measures buffering/audio device setup time.
    /// </summary>
    public int? PlaybackStartDelayMs { get; set; }

    /// <summary>
    /// The voice name/ID used for synthesis.
    /// </summary>
    public string? VoiceName { get; set; }

    /// <summary>
    /// The provider identifier (e.g., "AzureCognitive", "AzureOpenAi").
    /// </summary>
    public string? Provider { get; set; }
}

public interface ITtsService
{
    /// <summary>
    /// Speak text and return timing information for latency analysis.
    /// </summary>
    Task<TtsTimingInfo> SpeakAsync(string text, string targetLanguage, CancellationToken cancellationToken = default);

    Task StopAsync();
}
