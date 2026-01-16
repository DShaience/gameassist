using System;
using System.Collections.Generic;

namespace GameScreenTranslator.Core.Configuration;

public sealed class SpeechLanguageConfig
{
    public AzureSpeechSection AzureSpeech { get; set; } = new();
    public TextToSpeechSection TextToSpeech { get; set; } = new();
    public SpeechToTextSection SpeechToText { get; set; } = new();
    public OpenAiTtsConfig OpenAiTts { get; set; } = new();

    public sealed class AzureSpeechSection
    {
        public string Endpoint { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string? Region { get; set; }
    }

    public sealed class TextToSpeechSection
    {
        public string DefaultVoiceName { get; set; } = "he-IL-HilaNeural";

        /// <summary>
        /// Map from locale to voice name, e.g. "he-IL" => "he-IL-HilaNeural".
        /// </summary>
        public Dictionary<string, string> VoiceByLanguage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class SpeechToTextSection
    {
        public string DefaultRecognitionLanguage { get; set; } = "he-IL";
        public List<string> CandidateLanguages { get; set; } = new();
        public bool EnableAutoLanguageDetection { get; set; } = true;
    }
}

/// <summary>
/// Configuration for Azure OpenAI TTS-HD deployment.
/// </summary>
public sealed class OpenAiTtsConfig
{
    /// <summary>
    /// If true, use OpenAI TTS instead of Azure Cognitive Services TTS.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Azure OpenAI endpoint (e.g., "https://your-resource.openai.azure.com").
    /// </summary>
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// API key for Azure OpenAI.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Deployment name (e.g., "tts-hd").
    /// </summary>
    public string Deployment { get; set; } = "tts-hd";

    /// <summary>
    /// API version to use.
    /// </summary>
    public string ApiVersion { get; set; } = "2025-03-01-preview";

    /// <summary>
    /// Default voice to use. Options: alloy, echo, fable, onyx, nova, shimmer.
    /// </summary>
    public string DefaultVoice { get; set; } = "alloy";

    /// <summary>
    /// Map from language/locale to voice name.
    /// </summary>
    public Dictionary<string, string> VoiceByLanguage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
