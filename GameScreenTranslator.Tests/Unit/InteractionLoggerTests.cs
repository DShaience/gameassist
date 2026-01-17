using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace GameScreenTranslator.Tests.Unit;

public sealed class InteractionLoggerTests
{
    [Fact]
    public void Append_WritesValidJsonl()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gst_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Log.Configure(new ConsoleLogger(LogLevel.Info));

            var settings = new AppSettings
            {
                InteractionLogging = new AppSettings.InteractionLoggingSettings
                {
                    LogFilePath = "interactions.jsonl",
                    IncludeScreenshotsInDebug = true
                }
            };

            var logger = new InteractionLogger(settings, tempDir);

            logger.Append(new InteractionLogEntry
            {
                InteractionType = "translate_cycle",
                WindowTitle = "Test Window",
                ProcessName = "test.exe",
                ProcessId = 123,
                ScreenSignature = "DEADBEEF",
                CycleIndex = 0,
                Priority = "Highlighted",
                Translation = new InteractionLogEntry.TranslationData
                {
                    TargetLanguage = "Hebrew",
                    TranslatedText = "שלום",
                    SpokenText = "שלום"
                },
                LatencyMs = new InteractionLogEntry.LatencyData
                {
                    Capture = 10,
                    ModelCall = 20,
                    Tts = 30,
                    Total = 60
                }
            });

            var logPath = Path.Combine(tempDir, "interactions.jsonl");
            Assert.True(File.Exists(logPath));

            var lines = File.ReadAllLines(logPath);
            Assert.Single(lines);

            using var doc = JsonDocument.Parse(lines[0]);
            var root = doc.RootElement;

            Assert.Equal("translate_cycle", root.GetProperty("interaction_type").GetString());
            Assert.Equal("Test Window", root.GetProperty("window_title").GetString());
            Assert.Equal("DEADBEEF", root.GetProperty("screen_signature").GetString());

            // Ensure nested objects are present
            Assert.True(root.TryGetProperty("translation", out var translation));
            Assert.Equal("שלום", translation.GetProperty("translated_text").GetString());
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void EncodeScreenshotPngBase64_RespectsDebugGating()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gst_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var settings = new AppSettings
            {
                InteractionLogging = new AppSettings.InteractionLoggingSettings
                {
                    LogFilePath = "interactions.jsonl",
                    IncludeScreenshotsInDebug = true
                }
            };

            var logger = new InteractionLogger(settings, tempDir);

            using var img = new Image<Rgba32>(1, 1);
            img[0, 0] = new Rgba32(255, 0, 0, 255);

            Log.Configure(new ConsoleLogger(LogLevel.Info));
            Assert.Null(logger.EncodeScreenshotPngBase64(img));

            Log.Configure(new ConsoleLogger(LogLevel.Debug));
            var b64 = logger.EncodeScreenshotPngBase64(img);
            Assert.False(string.IsNullOrWhiteSpace(b64));

            var bytes = Convert.FromBase64String(b64!);

            // PNG signature: 89 50 4E 47 0D 0A 1A 0A
            var pngSig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Assert.True(bytes.Length > pngSig.Length);
            Assert.True(bytes.Take(pngSig.Length).SequenceEqual(pngSig));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }
}
