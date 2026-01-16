using System;
using System.Collections.Generic;

namespace GameScreenTranslator.Core.Configuration;

/// <summary>
/// Root configuration for all audio services (TTS and STT).
/// Replaces the old SpeechLanguageConfig.
/// </summary>
public sealed class AudioConfig
{
    public TtsConfig Tts { get; set; } = new();
    public SttConfig Stt { get; set; } = new();
}

/// <summary>
/// Text-to-Speech configuration with provider selection.
/// </summary>
public sealed class TtsConfig
{
    /// <summary>
    /// Which TTS provider to use: "AzureCognitive" or "AzureOpenAi"
    /// </summary>
    public string Provider { get; set; } = "AzureCognitive";

    /// <summary>
    /// Configuration for Azure Cognitive Services TTS.
    /// </summary>
    public AzureCognitiveTtsConfig AzureCognitive { get; set; } = new();

    /// <summary>
    /// Configuration for Azure OpenAI TTS (tts-hd deployment).
    /// </summary>
    public AzureOpenAiTtsConfig AzureOpenAi { get; set; } = new();
}

/// <summary>
/// Speech-to-Text configuration with provider selection.
/// </summary>
public sealed class SttConfig
{
    /// <summary>
    /// Which STT provider to use: "AzureCognitive" (future: "AzureOpenAiWhisper")
    /// </summary>
    public string Provider { get; set; } = "AzureCognitive";

    /// <summary>
    /// Configuration for Azure Cognitive Services STT.
    /// </summary>
    public AzureCognitiveSttConfig AzureCognitive { get; set; } = new();
}

/// <summary>
/// Azure Cognitive Services TTS configuration.
/// </summary>
public sealed class AzureCognitiveTtsConfig
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string? Region { get; set; }
    public string DefaultVoiceName { get; set; } = "he-IL-HilaNeural";
    public Dictionary<string, string> VoiceByLanguage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Azure OpenAI TTS configuration (tts-hd deployment).
/// </summary>
public sealed class AzureOpenAiTtsConfig
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Deployment { get; set; } = "tts-hd";
    public string ApiVersion { get; set; } = "2025-03-01-preview";
    public string DefaultVoice { get; set; } = "alloy";
    public Dictionary<string, string> VoiceByLanguage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Azure Cognitive Services STT configuration.
/// </summary>
public sealed class AzureCognitiveSttConfig
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string? Region { get; set; }
    public string DefaultRecognitionLanguage { get; set; } = "he-IL";
    public List<string> CandidateLanguages { get; set; } = new();
    public bool EnableAutoLanguageDetection { get; set; } = true;
}
