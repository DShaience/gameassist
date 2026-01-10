// See https://aka.ms/new-console-template for more information
using GameScreenTranslator.Core.App;
using GameScreenTranslator.Core.Audio;
using GameScreenTranslator.Core.Capture;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Logging;
using GameScreenTranslator.Core.State;
using System.Windows.Forms;

Log.Info("GameScreenTranslator starting...");

try
{
    // Default mode: hotkey mode when no args are supplied.
    if (args.Length == 0)
    {
        args = ["--hotkey-mode"];
    }
    else if (args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("-h", StringComparison.OrdinalIgnoreCase))
    {
        PrintUsage();
        return;
    }

    if (args.Length > 0 && args[0].Equals("--capture-once", StringComparison.OrdinalIgnoreCase))
    {
        var capturer = new ActiveWindowCapture();

        Log.Info("Switch to the target window now (you have ~2 seconds)...");
        await Task.Delay(2000);

        var result = capturer.CaptureActiveWindow();

        var sig = ScreenSignature.Compute(result.WindowTitle, result.Screenshot);

        Log.Info("Captured:");
        Log.Info($"  Title: {result.WindowTitle}");
        Log.Info($"  Process: {result.ProcessName} ({result.ProcessId})");
        Log.Info($"  Size: {result.Screenshot.Width}x{result.Screenshot.Height}");
        Log.Info($"  Signature: {sig}");
    }
    else if (args.Length > 0 && args[0].Equals("--cycle-demo", StringComparison.OrdinalIgnoreCase))
    {
        var capturer = new ActiveWindowCapture();
        var state = new ScreenStateManager();

        Log.Info("Cycle demo:");
        Log.Info("  Press ENTER to capture + compute signature + advance cycle for that screen");
        Log.Info("  Press Q then ENTER to quit");
        Log.Info("");

        while (true)
        {
            var line = Console.ReadLine();
            if (string.Equals(line, "q", StringComparison.OrdinalIgnoreCase))
                break;

            var result = capturer.CaptureActiveWindow();
            var sig = ScreenSignature.Compute(result.WindowTitle, result.Screenshot);

            state.SetCurrentScreen(sig);

            var cycleIndex = state.GetCycleIndex();
            var priority = PriorityCycler.GetPriorityForCycleIndex(cycleIndex);

            Log.Info($"Title: {result.WindowTitle}");
            Log.Info($"Sig:   {sig}");
            Log.Info($"Cycle: {cycleIndex}");
            Log.Info($"Prio:  {priority}");
            Log.Info("");

            state.AdvanceCycle();
        }
    }
    else if (args.Length > 0 && args[0].Equals("--foundry-translate-once", StringComparison.OrdinalIgnoreCase))
    {
        var printRaw = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i].Equals("--raw", StringComparison.OrdinalIgnoreCase))
                printRaw = true;
        }

        // Prefer existing config from current working directory (dev),
        // otherwise fall back to the executable directory (portable published EXE).
        var baseDir = ResolveRuntimeBaseDir();

        // Create first-run configs next to the executable when missing (published EXE scenario).
        ConfigBootstrapper.EnsureConfigFilesExist(baseDir);

        var settings = AppSettingsLoader.Load(baseDir);

        ConfigureLogging(settings);

        var speechConfig = SpeechLanguageConfigLoader.Load(baseDir, settings.Providers.AzureSpeech.ConfigPath);

        var capturer = new ActiveWindowCapture();

        Log.Info("Switch to the target window now (you have ~2 seconds)...");
        await Task.Delay(2000);

        var result = capturer.CaptureActiveWindow();

        try
        {
            var priority = PriorityCycler.UiPriority.Highlighted;

            var client = new GameScreenTranslator.Core.Foundry.AzureFoundryClient(settings.Providers.AzureFoundry);

            var response = await client.TranslateCycleAsync(
                screenshot: result.Screenshot,
                windowTitle: result.WindowTitle,
                priority: priority,
                targetLanguage: settings.Translation.TargetLanguage,
                mode: settings.Translation.Mode,
                downscaleWidthOverride: settings.ScreenCapture.DownscaleWidth,
                onRawModelContent: printRaw
                    ? raw =>
                    {
                        Log.Debug("");
                        Log.Debug("[Foundry Raw]");
                        Log.Debug(raw);
                        Log.Debug("");
                    }
                    : null);

            Log.Info("");
            Log.Info($"SelectedLabel: {response.SelectedLabel}");
            Log.Info($"SourceText: {response.SourceText}");
            Log.Info($"TranslatedText: {response.TranslatedText}");
            if (!string.IsNullOrWhiteSpace(response.Explanation))
                Log.Info($"Explanation: {response.Explanation}");

            // Minimal TTS behavior for the dev demo:
            // speak translated text, plus explanation if present.
            var toSpeak = response.TranslatedText;
            if (!string.IsNullOrWhiteSpace(response.Explanation))
                toSpeak = $"{toSpeak}. {response.Explanation}";

            await using var tts = new AzureSpeechTtsService(speechConfig);
            await tts.SpeakAsync(toSpeak, settings.Translation.TargetLanguage);
        }
        finally
        {
            result.Screenshot.Dispose();
        }
    }
    else if (args.Length > 0 && args[0].Equals("--hotkey-mode", StringComparison.OrdinalIgnoreCase))
    {
        // Prefer existing config from current working directory (dev),
        // otherwise fall back to the executable directory (portable published EXE).
        var baseDir = ResolveRuntimeBaseDir();

        // Create first-run configs next to the executable when missing (published EXE scenario).
        ConfigBootstrapper.EnsureConfigFilesExist(baseDir);

        AppSettings settings;
        SpeechLanguageConfig speechConfig;
        try
        {
            settings = AppSettingsLoader.Load(baseDir);
            ConfigureLogging(settings);
            speechConfig = SpeechLanguageConfigLoader.Load(baseDir, settings.Providers.AzureSpeech.ConfigPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "Configuration error. Edit 'appsettings.json' and 'speech_language_config.json' next to the executable, then run again.");
            return;
        }

        using var cts = new CancellationTokenSource();

        // Start a system-tray icon on an STA thread.
        // Right-click tray icon: open logs folder / exit.
        var logsDir = Path.Combine(baseDir, "logs");

        var trayThread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using var ctx = new TrayAppContext(cts, logsDir);
                Application.Run(ctx);
            }
            catch
            {
                // If tray fails, continue without it.
            }
        })
        {
            IsBackground = true
        };
        trayThread.SetApartmentState(ApartmentState.STA);
        trayThread.Start();

        // If shutdown is triggered elsewhere (Ctrl+C), try to close the tray loop.
        cts.Token.Register(() =>
        {
            try { Application.Exit(); } catch { /* ignore */ }
        });

        Log.Info("Hotkey mode running. Press Ctrl+C to exit.");
        Log.Info("  Ctrl+Alt+T = Translate/Cycle");
        Log.Info("  Ctrl+Alt+Q (hold) = Ask Question (record while held)");
        Log.Info("  Ctrl+Alt+W = Tap-to-talk test (auto-stops after ~1.5s silence)");
        Log.Info("  Ctrl+Alt+X = Stop Speaking");
        Log.Info("");

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await using var runner = new GameScreenTranslator.Core.App.HotkeyAppRunner(settings, speechConfig, baseDir);
        runner.Start();
        runner.Run(cts.Token);

        // Best-effort: give tray loop a moment to exit.
        try { trayThread.Join(1500); } catch { /* ignore */ }
    }
    else
    {
        PrintUsage();
    }
}
catch (Exception ex)
{
    Log.Error(ex,
        "Fatal error. If running a published EXE, ensure config files exist next to the EXE (or let it auto-create templates on first run).");
}

static void PrintUsage()
{
    Log.Info("Default: (no args) runs --hotkey-mode");
    Log.Info("Run with: --hotkey-mode");
    Log.Info("      or: --capture-once");
    Log.Info("      or: --cycle-demo");
    Log.Info("      or: --foundry-translate-once [--raw]");
    Log.Info("      or: --help / -h");
}

static string ResolveRuntimeBaseDir(string appSettingsFileName = "appsettings.json")
{
    // Dev-friendly: prefer current directory (repo root / project dir),
    // but for a portable published EXE prefer config next to the EXE.
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

static void ConfigureLogging(AppSettings settings)
{
    // Prefer app-specific override, fall back to default.
    var configured = settings.Logging?.LogLevel?.GameScreenTranslator;
    if (string.IsNullOrWhiteSpace(configured))
        configured = settings.Logging?.LogLevel?.Default;

    var level = LogLevelParser.Parse(configured, fallback: LogLevel.Info);
    Log.Configure(new ConsoleLogger(level));

    Log.Debug($"Logging configured: {level} (from appsettings.json)");
}