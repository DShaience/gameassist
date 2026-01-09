using System;
using System.Collections.Generic;

namespace GameScreenTranslator.Core.Configuration;

public sealed class SpeechLanguageConfig
{
    public AzureSpeechSection AzureSpeech { get; set; } = new();
    public TextToSpeechSection TextToSpeech { get; set; } = new();
    public SpeechToTextSection SpeechToText { get; set; } = new();

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