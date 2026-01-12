using System;
using System.IO;

namespace GameScreenTranslator.Core.Configuration;

/// <summary>
/// Centralizes the "bootstrap" logic that was repeated across multiple Program.cs branches:
///   1) Resolve baseDir
///   2) Ensure first-run config templates exist
///   3) Load settings, prompts, speech config
///   4) Configure logging
///
/// "Bootstrapping" means: the one-time setup work an application does at startup before
/// it can run its main logic (config loading, logging setup, creating directories, etc.).
/// When the same steps are copy-pasted into multiple if/else branches, that's "repeated
/// bootstrapping" — easy to let drift and hard to maintain. This class does it once.
/// </summary>
public static class RuntimeBootstrapper
{
    /// <summary>
    /// Loads all runtime configuration and returns a <see cref="RuntimeContext"/>.
    /// Throws on validation errors (missing config, parse failures).
    /// </summary>
    public static RuntimeContext Load()
    {
        var baseDir = ResolveRuntimeBaseDir();

        // Create first-run configs next to the executable when missing.
        ConfigBootstrapper.EnsureConfigFilesExist(baseDir);

        var settings = AppSettingsLoader.Load(baseDir);
        ConfigureLogging(settings);

        var prompts = PromptPackLoader.Load(baseDir);
        var speechConfig = SpeechLanguageConfigLoader.Load(baseDir, settings.Providers.AzureSpeech.ConfigPath);

        var logsDir = Path.Combine(baseDir, "logs");

        return new RuntimeContext(baseDir, logsDir, settings, prompts, speechConfig);
    }

    /// <summary>
    /// Dev-friendly: prefer current directory (repo root / project dir),
    /// but for a portable published EXE prefer config next to the EXE.
    /// </summary>
    public static string ResolveRuntimeBaseDir(string appSettingsFileName = "appsettings.json")
    {
        var cwd = Directory.GetCurrentDirectory();

        var direct = Path.Combine(cwd, appSettingsFileName);
        if (File.Exists(direct))
            return cwd;

        var underProject = Path.Combine(cwd, "GameScreenTranslator", appSettingsFileName);
        if (File.Exists(underProject))
            return Path.Combine(cwd, "GameScreenTranslator");

        var exeDir = AppContext.BaseDirectory;

        var exeDirect = Path.Combine(exeDir, appSettingsFileName);
        if (File.Exists(exeDirect))
            return exeDir;

        var exeUnderProject = Path.Combine(exeDir, "GameScreenTranslator", appSettingsFileName);
        if (File.Exists(exeUnderProject))
            return Path.Combine(exeDir, "GameScreenTranslator");

        // No config found anywhere: default to exe dir (portable).
        return exeDir;
    }

    private static void ConfigureLogging(AppSettings settings)
    {
        var configured = settings.Logging?.LogLevel?.GameScreenTranslator;
        if (string.IsNullOrWhiteSpace(configured))
            configured = settings.Logging?.LogLevel?.Default;

        var level = Logging.LogLevelParser.Parse(configured, fallback: Logging.LogLevel.Info);
        Logging.Log.Configure(new Logging.ConsoleLogger(level));

        Logging.Log.Debug($"Logging configured: {level} (from appsettings.json)");
    }
}

/// <summary>
/// Holds everything created during bootstrapping so commands don't need to repeat the work.
/// </summary>
public sealed class RuntimeContext
{
    public string BaseDirectory { get; }
    public string LogsDirectory { get; }
    public AppSettings Settings { get; }
    public PromptPack Prompts { get; }
    public SpeechLanguageConfig SpeechConfig { get; }

    public RuntimeContext(
        string baseDirectory,
        string logsDirectory,
        AppSettings settings,
        PromptPack prompts,
        SpeechLanguageConfig speechConfig)
    {
        BaseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
        LogsDirectory = logsDirectory ?? throw new ArgumentNullException(nameof(logsDirectory));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));
        SpeechConfig = speechConfig ?? throw new ArgumentNullException(nameof(speechConfig));
    }
}
