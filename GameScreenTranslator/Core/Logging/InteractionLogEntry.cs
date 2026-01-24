using System;

namespace GameScreenTranslator.Core.Logging;

public sealed class InteractionLogEntry
{
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// E.g. translate_cycle, question_tap, stop
    /// </summary>
    public string InteractionType { get; set; } = "";

    public string WindowTitle { get; set; } = "";
    public string? ProcessName { get; set; }
    public int? ProcessId { get; set; }
    public string? HwndHex { get; set; }

    public string ScreenSignature { get; set; } = "";
    public int? CycleIndex { get; set; }
    public string? Priority { get; set; }

    public string? UserQuestion { get; set; }

    /// <summary>
    /// Conversation turn index at the time of this interaction (0-based).
    /// Used for conversation history tracking.
    /// </summary>
    public int? ConversationTurnIndex { get; set; }

    /// <summary>
    /// Privacy-sensitive. Optional conversation context text (rolling summary + recent turns),
    /// only intended to be written when Debug + explicit opt-in is enabled.
    /// </summary>
    public string? ConversationContextText { get; set; }

    public SelectedRegionData? SelectedRegion { get; set; }
    public TranslationData? Translation { get; set; }
    public LatencyData? LatencyMs { get; set; }

    public bool? SkippedBecauseAlreadySpoken { get; set; }
    public string? Error { get; set; }

    /// <summary>
    /// Optional screenshot PNG base64; only written when enabled + debug logging.
    /// </summary>
    public string? ScreenshotPngBase64 { get; set; }

    public sealed class SelectedRegionData
    {
        public string? SelectedLabel { get; set; }
        public string? SourceText { get; set; }
    }

    public sealed class TranslationData
    {
        public string? TargetLanguage { get; set; }
        public string? TranslatedText { get; set; }
        public string? Explanation { get; set; }
        public string? SpokenText { get; set; }
    }

    public sealed class LatencyData
    {
        // Core timings (always logged)
        public int? Capture { get; set; }
        public int? ModelCall { get; set; }
        public int? Tts { get; set; }
        public int? Total { get; set; }

        // Detailed breakdown for latency analysis
        public DetailedTimings? Detailed { get; set; }
    }

    /// <summary>
    /// Detailed timing breakdown for latency analysis (PRD 0001.5).
    /// Helps identify which stage dominates Time To First Audio (TTFA).
    /// </summary>
    public sealed class DetailedTimings
    {
        // Image processing
        public int? ImageDownscaleMs { get; set; }

        // STT breakdown (Ask flow only)
        public int? SttMs { get; set; }

        // LLM breakdown
        public int? LlmInputTokens { get; set; }
        public int? LlmOutputTokens { get; set; }

        // TTS breakdown
        public int? TtsNetworkMs { get; set; }          // Time until first audio bytes received
        public int? PlaybackStartDelayMs { get; set; } // Time from first bytes to first audio played
        public int? TtsCharCount { get; set; }         // Characters sent to TTS

        // Summary metrics
        public int? TtfaUserMs { get; set; }           // Time To First Audio (from hotkey press)

        // Outcome tracking
        public string? Outcome { get; set; }           // success, timeout, error, cancelled
        public string? CancelStage { get; set; }       // stt, llm, tts, playback, other (if cancelled)
        public string? ErrorStage { get; set; }        // Which stage failed (if error)

        // Provider info
        public string? SttProvider { get; set; }
        public string? LlmProvider { get; set; }
        public string? LlmModel { get; set; }
        public string? TtsProvider { get; set; }
        public string? TtsVoice { get; set; }
    }
}
