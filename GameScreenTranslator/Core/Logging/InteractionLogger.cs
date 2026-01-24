using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameScreenTranslator.Core.Abstractions;
using GameScreenTranslator.Core.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace GameScreenTranslator.Core.Logging;

public sealed class InteractionLogger : IInteractionLogger
{
    private readonly object _sync = new();
    private readonly string _logPath;
    private readonly bool _includeScreenshotsInDebug;

    public InteractionLogger(AppSettings settings, string baseDirectory)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        if (string.IsNullOrWhiteSpace(baseDirectory)) throw new ArgumentException("Base directory cannot be empty.", nameof(baseDirectory));

        var configuredPath = settings.InteractionLogging.LogFilePath;
        if (string.IsNullOrWhiteSpace(configuredPath))
            configuredPath = "logs/interactions.jsonl";

        // Generate timestamp-based filename: yyyy_mm_dd__HHMMSS_interactions.jsonl
        var timestamp = DateTime.Now.ToString("yyyy_MM_dd__HHmmss");
        var dir = Path.GetDirectoryName(configuredPath) ?? "logs";
        var ext = Path.GetExtension(configuredPath);
        var baseName = Path.GetFileNameWithoutExtension(configuredPath);
        var timestampedFilename = $"{timestamp}_{baseName}{ext}";
        configuredPath = Path.Combine(dir, timestampedFilename);

        _logPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(baseDirectory, configuredPath);

        _includeScreenshotsInDebug = settings.InteractionLogging.IncludeScreenshotsInDebug;
    }

    public void Append(InteractionLogEntry entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));

        // Ensure timestamp always set.
        if (entry.TimestampUtc == default)
            entry.TimestampUtc = DateTimeOffset.UtcNow;

        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Preserve UTF-8 (Hebrew, Russian, etc.)
        });

        lock (_sync)
        {
            var dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            File.AppendAllText(_logPath, json + Environment.NewLine, Encoding.UTF8);
        }
    }

    public bool ShouldIncludeScreenshots() =>
        _includeScreenshotsInDebug && Log.IsEnabled(LogLevel.Debug);

    public string? EncodeScreenshotPngBase64(Image<Rgba32> screenshot)
    {
        if (!ShouldIncludeScreenshots())
            return null;

        using var ms = new MemoryStream();
        screenshot.Save(ms, new PngEncoder());
        return Convert.ToBase64String(ms.ToArray());
    }

    public static Stopwatch StartTimer() => Stopwatch.StartNew();

    public static int? ElapsedMs(Stopwatch? sw) => sw is null ? null : (int)sw.ElapsedMilliseconds;
}

/// <summary>
/// .NET 10 includes JsonNamingPolicy.SnakeCaseLower, but keep a compatibility shim in case it changes.
/// </summary>
internal static class JsonNamingPolicy
{
    public static System.Text.Json.JsonNamingPolicy SnakeCaseLower { get; } =
        System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
}
