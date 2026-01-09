using System;

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

        Console.WriteLine(prefix + message);
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