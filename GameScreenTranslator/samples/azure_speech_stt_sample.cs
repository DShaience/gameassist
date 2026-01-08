// Standalone Azure AI Speech Speech-to-Text (STT) sample (batch / from file).
//
// Config mapping (reads the same JSON file used by the TTS sample):
// - SpeechToText.Endpoint -> AZURE_SPEECH_ENDPOINT (optional override)
// - SpeechToText.ApiKey   -> AZURE_SPEECH_KEY (optional override)
// - SpeechToText.(SpeechRecognitionLanguage|RecognitionLanguage|SpeechSynthesisVoiceName) -> AZURE_SPEECH_RECOGNITION_LANGUAGE (optional override)
// - (Optional) SpeechToText.Region -> AZURE_SPEECH_REGION (preferred if available)
// - (Optional) SpeechToText.LanguageList -> enables auto language detection when no single language is set
//
// Prereq:
//   dotnet add package Microsoft.CognitiveServices.Speech
//
// Run (PowerShell):
//   dotnet run -- "path\to\audio.wav"
//
// Notes:
// - This sample uses RecognizeOnceAsync: best for short audio clips.
// - WAV input should typically be PCM (e.g., 16 kHz, 16-bit, mono).
// - MP3 input is supported via NAudio decode+resample to 16 kHz mono PCM.
//
// Extra prereq for MP3:
//   dotnet add package NAudio

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: dotnet run -- <path-to-audio.wav>");
            return 64;
        }

        var audioPath = args[0];
        if (!File.Exists(audioPath))
        {
            Console.Error.WriteLine($"Audio file not found: {audioPath}");
            return 66;
        }

        // Prefer env vars, but fall back to reading the local config file.
        // IMPORTANT: Never print/log the API key.
        var endpoint = Environment.GetEnvironmentVariable("AZURE_SPEECH_ENDPOINT");
        var apiKey = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
        var recognitionLanguage = Environment.GetEnvironmentVariable("AZURE_SPEECH_RECOGNITION_LANGUAGE");
        var region = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");
        string[]? candidateLanguages = null;

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(recognitionLanguage) || string.IsNullOrWhiteSpace(region))
        {
            var cfg = TryLoadSttConfig();
            endpoint ??= cfg?.Endpoint;
            apiKey ??= cfg?.ApiKey;
            recognitionLanguage ??= cfg?.SpeechRecognitionLanguage
                ?? cfg?.RecognitionLanguage
                ?? cfg?.SpeechSynthesisVoiceName; // fallback for your current sample config
            region ??= cfg?.Region;
            candidateLanguages ??= cfg?.LanguageList;
        }

        endpoint ??= "https://gaming-assistant-ai.cognitiveservices.azure.com/";

        // Normalize optional inputs.
        if (string.IsNullOrWhiteSpace(recognitionLanguage))
        {
            recognitionLanguage = null;
        }

        if (candidateLanguages is { Length: > 0 })
        {
            candidateLanguages = Array.FindAll(candidateLanguages, lang => !string.IsNullOrWhiteSpace(lang));
            if (candidateLanguages.Length == 0)
            {
                candidateLanguages = null;
            }
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine("Missing Azure Speech API key. Set AZURE_SPEECH_KEY or provide it in the SpeechToText config JSON.");
            return 1;
        }

        var config = !string.IsNullOrWhiteSpace(region)
            ? SpeechConfig.FromSubscription(apiKey, region)
            : SpeechConfig.FromEndpoint(new Uri(endpoint), apiKey);

        // If we have a candidate list from config and no explicit single-language override, use auto-detect.
        var useAutoDetect = (recognitionLanguage is null)
            && (candidateLanguages is { Length: > 0 });

        if (!useAutoDetect)
        {
            // Default only when auto-detect isn't used.
            recognitionLanguage ??= "he-IL";
            config.SpeechRecognitionLanguage = recognitionLanguage;
        }

        using var audioConfig = CreateAudioConfigFromFile(audioPath);
        using var recognizer = useAutoDetect
            ? new SpeechRecognizer(config, AutoDetectSourceLanguageConfig.FromLanguages(candidateLanguages!), audioConfig)
            : new SpeechRecognizer(config, audioConfig);

        var result = await recognizer.RecognizeOnceAsync();

        switch (result.Reason)
        {
            case ResultReason.RecognizedSpeech:
                if (useAutoDetect)
                {
                    var detected = AutoDetectSourceLanguageResult.FromResult(result);
                    // Print detected language to stderr to avoid breaking simple stdout piping.
                    Console.Error.WriteLine($"Detected language: {detected.Language}");
                }
                Console.WriteLine(result.Text);
                return 0;

            case ResultReason.NoMatch:
                Console.WriteLine("NOMATCH: Speech could not be recognized.");
                return 2;

            case ResultReason.Canceled:
                var cancellation = CancellationDetails.FromResult(result);
                Console.WriteLine($"CANCELED: Reason={cancellation.Reason}");

                if (cancellation.Reason == CancellationReason.Error)
                {
                    Console.WriteLine($"CANCELED: ErrorCode={cancellation.ErrorCode}");
                    Console.WriteLine($"CANCELED: ErrorDetails=[{cancellation.ErrorDetails}]");
                    Console.WriteLine("CANCELED: Check endpoint/key/language and audio format.");
                }

                return 3;

            default:
                Console.WriteLine($"Unexpected result reason: {result.Reason}");
                return 4;
        }
    }

    private static SpeechToTextConfig? TryLoadSttConfig()
    {
        var explicitPath = Environment.GetEnvironmentVariable("STT_CONFIG_PATH")
            ?? Environment.GetEnvironmentVariable("TTS_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return TryReadConfigFile(explicitPath);
        }

        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "..", "configs", "samples", "tts_model_config.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "configs", "samples", "tts_model_config.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "configs", "samples", "tts_model_config.json"),
        };

        foreach (var path in candidates)
        {
            var cfg = TryReadConfigFile(path);
            if (cfg is not null)
            {
                return cfg;
            }
        }

        return null;
    }

    private static SpeechToTextConfig? TryReadConfigFile(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                return null;
            }

            var json = File.ReadAllText(fullPath);
            var root = JsonSerializer.Deserialize<SttModelConfigRoot>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return root?.SpeechToText;
        }
        catch
        {
            // Intentionally swallow errors here to keep this sample small and avoid logging secrets.
            return null;
        }
    }

    private sealed class SttModelConfigRoot
    {
        public SpeechToTextConfig? SpeechToText { get; set; }
    }

    private sealed class SpeechToTextConfig
    {
        public string? Endpoint { get; set; }
        public string? ApiKey { get; set; }
        public string? Region { get; set; }

        public string[]? LanguageList { get; set; }

        // Recommended field name (if you add it later):
        public string? SpeechRecognitionLanguage { get; set; }

        // Alternate field name (if you prefer it):
        public string? RecognitionLanguage { get; set; }

        // Back-compat with your current sample config key:
        public string? SpeechSynthesisVoiceName { get; set; }
    }

    private static AudioConfig CreateAudioConfigFromFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        // Fast path for WAV (Speech SDK can read wav directly)
        if (extension == ".wav")
        {
            return AudioConfig.FromWavFileInput(path);
        }

        // For MP3 (and many other formats supported by MediaFoundation on Windows), decode and
        // resample to 16 kHz mono 16-bit PCM and push to Speech SDK.
        var format = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
        var pushStream = AudioInputStream.CreatePushStream(format);

        try
        {
            using var reader = new AudioFileReader(path);
            ISampleProvider samples = reader;

            if (reader.WaveFormat.Channels == 2)
            {
                samples = new StereoToMonoSampleProvider(samples);
            }
            else if (reader.WaveFormat.Channels != 1)
            {
                throw new NotSupportedException($"Unsupported channel count: {reader.WaveFormat.Channels}");
            }

            samples = new WdlResamplingSampleProvider(samples, 16000);
            var pcm16 = new SampleToWaveProvider16(samples);

            var buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = pcm16.Read(buffer, 0, buffer.Length)) > 0)
            {
                // Copy the exact bytes read; Speech SDK reads bytes synchronously here.
                if (bytesRead == buffer.Length)
                {
                    pushStream.Write(buffer);
                }
                else
                {
                    var chunk = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRead);
                    pushStream.Write(chunk);
                }
            }
        }
        finally
        {
            pushStream.Close();
        }

        return AudioConfig.FromStreamInput(pushStream);
    }
}
