using System.Collections.Generic;

namespace GameScreenTranslator.Core.Configuration;

public sealed class AppSettings
{
    public ProvidersSettings Providers { get; set; } = new();
    public TranslationSettings Translation { get; set; } = new();
    public ScreenCaptureSettings ScreenCapture { get; set; } = new();
    public AudioSettings Audio { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public InteractionLoggingSettings InteractionLogging { get; set; } = new();
    public ConversationSettings Conversation { get; set; } = new();

    public sealed class ProvidersSettings
    {
        public AzureFoundrySettings AzureFoundry { get; set; } = new();
        public AzureSpeechSettings AzureSpeech { get; set; } = new();
    }

    public sealed class AzureFoundrySettings
    {
        public string Endpoint { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string ApiVersion { get; set; } = "2024-10-21";

        public LlmSettings LLM { get; set; } = new();

        public sealed class LlmSettings
        {
            public string ChatDeployment { get; set; } = "";
            public string VisionDeployment { get; set; } = "";
            public int RetryAttempts { get; set; } = 2;
            public int TimeoutSeconds { get; set; } = 30;
        }
    }

    public sealed class AzureSpeechSettings
    {
        public string ConfigPath { get; set; } = "speech_language_config.json";
    }

    public sealed class TranslationSettings
    {
        public string TargetLanguage { get; set; } = "Hebrew";
        public string Mode { get; set; } = "explain";
    }

    public sealed class ScreenCaptureSettings
    {
        public int DownscaleWidth { get; set; } = 640;
        public string SignatureHashAlgorithm { get; set; } = "xxHash64";
    }

    public sealed class AudioSettings
    {
        // Not yet used in P0, but kept to match appsettings.template.json (future phases).
        public bool DuckingEnabled { get; set; } = true;
        public int DuckVolumePercent { get; set; } = 25;
        public int DuckDurationMs { get; set; } = 300;
        public double TTSSpeed { get; set; } = 1.0;

        /// <summary>
        /// Fallback messages spoken when the app cannot complete an action.
        /// Keys are language names like "Hebrew", "Russian", or locales like "he-IL", "ru-RU".
        /// </summary>
        public Dictionary<string, string> FallbackMessagesByLanguage { get; set; } = new()
        {
            // Hebrew
            ["Hebrew"] = "סליחה, הייתה בעיה. נסו שוב.",
            // Russian
            ["Russian"] = "Извините, произошла ошибка. Попробуйте еще раз.",
            // English default
            ["en-US"] = "Sorry, something went wrong. Please try again."
        };

        /// <summary>
        /// If capture fails due to protected / black frames, use this message.
        /// </summary>
        public string CaptureFailedMessage { get; set; } = "לא הצלחתי לצלם את המסך של המשחק הזה.";

        /// <summary>
        /// When speech synthesis or model calls fail, use a generic error message from FallbackMessagesByLanguage.
        /// </summary>
        public string GenericErrorMessageKey { get; set; } = "Hebrew";
    }

    public sealed class LoggingSettings
    {
        // Compatible with the common Microsoft-style config block:
        // "Logging": { "LogLevel": { "Default": "Information", "GameScreenTranslator": "Debug" } }
        public LogLevelSettings LogLevel { get; set; } = new();

        public sealed class LogLevelSettings
        {
            public string Default { get; set; } = "Information";
            public string GameScreenTranslator { get; set; } = "Information";
        }
    }

    public sealed class InteractionLoggingSettings
    {
        /// <summary>
        /// JSONL output path (relative to the working directory if not rooted).
        /// </summary>
        public string LogFilePath { get; set; } = "logs/interactions.jsonl";

        /// <summary>
        /// If true and log level is Debug, include base64 screenshots in the JSONL output.
        /// </summary>
        public bool IncludeScreenshotsInDebug { get; set; } = true;
    }

    public sealed class ConversationSettings
    {
        /// <summary>
        /// Whether conversation history is enabled (allows follow-up questions with context).
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Number of recent turns (user+assistant pairs) to keep verbatim in context.
        /// Older turns are summarized.
        /// </summary>
        public int MaxVerbatimTurns { get; set; } = 2;

        /// <summary>
        /// Maximum character length for the rolling summary of older turns.
        /// </summary>
        public int SummaryMaxChars { get; set; } = 1200;

        /// <summary>
        /// Logging settings for conversation context.
        /// </summary>
        public ConversationLoggingSettings Logging { get; set; } = new();

        public sealed class ConversationLoggingSettings
        {
            /// <summary>
            /// If true and log level is Debug, include conversation context text in JSONL output.
            /// Privacy-sensitive: disabled by default.
            /// </summary>
            public bool IncludeContextInDebug { get; set; } = false;
        }
    }
}
