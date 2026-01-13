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
        public string? Mode { get; set; }

        public string? TranslatedText { get; set; }
        public string? Explanation { get; set; }
        public string? SpokenText { get; set; }
    }

    public sealed class LatencyData
    {
        public int? Capture { get; set; }
        public int? ModelCall { get; set; }
        public int? Tts { get; set; }
        public int? Total { get; set; }
    }
}
