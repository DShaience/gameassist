// Standalone Azure AI Speech Text-to-Speech (TTS) sample.
//
// Config mapping (matches your tts_model_config.json structure, but does NOT read it from disk):
// - SpeechPlayground.Endpoint -> AZURE_SPEECH_ENDPOINT (e.g. https://gaming-assistant-ai.cognitiveservices.azure.com/)
// - SpeechPlayground.ApiKey   -> AZURE_SPEECH_KEY
// - SpeechSynthesisVoiceName  -> AZURE_SPEECH_VOICE (default: he-IL-HilaNeural)
//
// Prereq:
//   dotnet add package Microsoft.CognitiveServices.Speech
//
// Run (PowerShell):
//   $env:AZURE_SPEECH_ENDPOINT="https://...cognitiveservices.azure.com/"
//   $env:AZURE_SPEECH_KEY="..."
//   $env:AZURE_SPEECH_VOICE="he-IL-HilaNeural"
//   dotnet run
//
// Notes:
// - Uses the default speaker as audio output.
// - If you prefer to save to a WAV file instead, see the commented AudioConfig section.

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Prefer env vars, but fall back to reading the local config file.
        // IMPORTANT: Never print/log the API key.

        var endpoint = Environment.GetEnvironmentVariable("AZURE_SPEECH_ENDPOINT");
        var apiKey = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
        var voiceName = Environment.GetEnvironmentVariable("AZURE_SPEECH_VOICE");

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(voiceName))
        {
            var configFromFile = TryLoadSpeechPlaygroundConfig();

            endpoint ??= configFromFile?.Endpoint;
            apiKey ??= configFromFile?.ApiKey;
            voiceName ??= configFromFile?.SpeechSynthesisVoiceName;
        }

        endpoint ??= "https://gaming-assistant-ai.cognitiveservices.azure.com/";
        voiceName ??= "he-IL-HilaNeural";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine("Missing Azure Speech API key. Set AZURE_SPEECH_KEY or provide it in the TTS config JSON.");
            return 1;
        }

        var text = args.Length > 0
            ? string.Join(' ', args)
            : "שלום! אני יכול לקרוא טקסט בעברית.";

        var config = SpeechConfig.FromEndpoint(new Uri(endpoint), apiKey);
        config.SpeechSynthesisVoiceName = voiceName;

        // Default speaker output:
        using var synthesizer = new SpeechSynthesizer(config);

        // If you prefer writing audio to a WAV file instead:
        // using var audioConfig = AudioConfig.FromWavFileOutput("tts_output.wav");
        // using var synthesizer = new SpeechSynthesizer(config, audioConfig);

        using var result = await synthesizer.SpeakTextAsync(text);

        if (result.Reason == ResultReason.SynthesizingAudioCompleted)
        {
            Console.WriteLine($"Speech synthesized for text: {text}");
            return 0;
        }

        if (result.Reason == ResultReason.Canceled)
        {
            var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
            Console.WriteLine($"CANCELED: Reason={cancellation.Reason}");

            if (cancellation.Reason == CancellationReason.Error)
            {
                Console.WriteLine($"CANCELED: ErrorCode={cancellation.ErrorCode}");
                Console.WriteLine($"CANCELED: ErrorDetails=[{cancellation.ErrorDetails}]");
                Console.WriteLine("CANCELED: Check endpoint/key/region availability.");
            }

            return 2;
        }

        Console.WriteLine($"Unexpected result reason: {result.Reason}");
        return 3;
    }

    private static SpeechPlaygroundConfig? TryLoadSpeechPlaygroundConfig()
    {
        // You can override the config path via env var.
        var explicitPath = Environment.GetEnvironmentVariable("TTS_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return TryReadConfigFile(explicitPath);
        }

        // Common relative path when running from GameScreenTranslator/samples/AzureSpeechTtsSample
        // (console app folder) back to GameScreenTranslator/configs/samples/tts_model_config.json
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "configs", "samples", "tts_model_config.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "configs", "samples", "tts_model_config.json"),
        };

        foreach (var path in candidates)
        {
            var config = TryReadConfigFile(path);
            if (config is not null)
            {
                return config;
            }
        }

        return null;
    }

    private static SpeechPlaygroundConfig? TryReadConfigFile(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                return null;
            }

            var json = File.ReadAllText(fullPath);
            var root = JsonSerializer.Deserialize<TtsModelConfigRoot>(json);
            return root?.SpeechPlayground;
        }
        catch
        {
            // Intentionally swallow errors here to keep this sample small and avoid logging secrets.
            return null;
        }
    }

    private sealed class TtsModelConfigRoot
    {
        public SpeechPlaygroundConfig? SpeechPlayground { get; set; }
    }

    private sealed class SpeechPlaygroundConfig
    {
        public string? Endpoint { get; set; }
        public string? ApiKey { get; set; }
        public string? SpeechSynthesisVoiceName { get; set; }
    }
}
