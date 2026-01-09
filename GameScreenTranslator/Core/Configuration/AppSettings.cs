namespace GameScreenTranslator.Core.Configuration;

public sealed class AppSettings
{
    public ProvidersSettings Providers { get; set; } = new();
    public TranslationSettings Translation { get; set; } = new();
    public ScreenCaptureSettings ScreenCapture { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();

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
}
