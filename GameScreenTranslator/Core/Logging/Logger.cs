using System;
using System.Linq;

namespace GameScreenTranslator.Core.Logging;

public enum LogLevel
{
    Error = 0,
    Warning = 1,
    Info = 2,
    Debug = 3
}

public static class LogLevelParser
{
    public static LogLevel Parse(string? value, LogLevel fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        // Accept both short and Microsoft-style names.
        // Error, Warning, Information, Debug, Trace, Critical.
        var s = value.Trim();

        if (s.Equals("Critical", StringComparison.OrdinalIgnoreCase))
            return LogLevel.Error;

        if (s.Equals("Error", StringComparison.OrdinalIgnoreCase))
            return LogLevel.Error;

        if (s.Equals("Warning", StringComparison.OrdinalIgnoreCase) || s.Equals("Warn", StringComparison.OrdinalIgnoreCase))
            return LogLevel.Warning;

        if (s.Equals("Information", StringComparison.OrdinalIgnoreCase) || s.Equals("Info", StringComparison.OrdinalIgnoreCase))
            return LogLevel.Info;

        if (s.Equals("Debug", StringComparison.OrdinalIgnoreCase) || s.Equals("Trace", StringComparison.OrdinalIgnoreCase))
            return LogLevel.Debug;

        return fallback;
    }
}

public interface ILogger
{
    LogLevel Level { get; }
    void Log(LogLevel level, string message);
}

public sealed class ConsoleLogger : ILogger
{
    public ConsoleLogger(LogLevel level)
    {
        Level = level;
    }

    public LogLevel Level { get; }

    public void Log(LogLevel level, string message)
    {
        if (level > Level)
            return;

        var prefix = level switch
        {
            LogLevel.Error => "[ERROR] ",
            LogLevel.Warning => "[WARN] ",
            LogLevel.Info => "[INFO] ",
            LogLevel.Debug => "[DEBUG] ",
            _ => ""
        };

        // Apply RTL mark if message contains Hebrew characters
        var adjustedMessage = AdjustForRtl(message);
        Console.WriteLine(prefix + adjustedMessage);
    }

    /// <summary>
    /// Adjusts text with mixed LTR (English) and RTL (Hebrew) content for better console display.
    /// Uses Unicode Right-to-Left Isolate (RLI) to wrap Hebrew segments.
    /// </summary>
    private static string AdjustForRtl(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Hebrew Unicode range: U+0590 to U+05FF
        // Check if any character is in the Hebrew block
        bool hasHebrew = text.Any(c => c >= '\u0590' && c <= '\u05FF');
        
        if (!hasHebrew)
            return text;

        // For mixed content, use Unicode Bidi formatting characters:
        // RLI (Right-to-Left Isolate) = U+2067 - starts RTL isolation
        // PDI (Pop Directional Isolate) = U+2069 - ends isolation
        // This keeps English labels LTR while Hebrew content renders RTL

        const char RLI = '\u2067';  // Right-to-Left Isolate
        const char PDI = '\u2069';  // Pop Directional Isolate
        const char RLM = '\u200F';  // Right-to-Left Mark

        var result = new System.Text.StringBuilder();
        bool inHebrew = false;

        foreach (var ch in text)
        {
            bool isHebrewChar = ch >= '\u0590' && ch <= '\u05FF';

            if (isHebrewChar && !inHebrew)
            {
                // Start Hebrew segment
                result.Append(RLI);
                inHebrew = true;
            }
            else if (!isHebrewChar && inHebrew && !char.IsWhiteSpace(ch) && ch != '.' && ch != ',' && ch != '!' && ch != '?')
            {
                // End Hebrew segment (but keep punctuation and spaces inside)
                result.Append(PDI);
                inHebrew = false;
            }

            result.Append(ch);
        }

        // Close any open Hebrew segment
        if (inHebrew)
        {
            result.Append(PDI);
        }

        // Also prepend RLM for overall RTL hint
        return RLM + result.ToString();
    }
}

/// <summary>
/// Minimal global logger (no DI) for this toy project.
/// Configure once in Program.cs after loading AppSettings.
/// </summary>
public static class Log
{
    private static readonly object Sync = new();
    private static ILogger _logger = new ConsoleLogger(LogLevel.Info);

    public static void Configure(ILogger logger)
    {
        if (logger is null) throw new ArgumentNullException(nameof(logger));
        lock (Sync)
        {
            _logger = logger;
        }
    }

    public static LogLevel Level
    {
        get
        {
            lock (Sync)
            {
                return _logger.Level;
            }
        }
    }

    public static bool IsEnabled(LogLevel level) => level <= Level;

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Error(Exception ex, string message)
    {
        Write(LogLevel.Error, message);
        Write(LogLevel.Error, ex.ToString());
    }

    private static void Write(LogLevel level, string message)
    {
        lock (Sync)
        {
            _logger.Log(level, message);
        }
    }
}
