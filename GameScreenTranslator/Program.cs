// See https://aka.ms/new-console-template for more information
using GameScreenTranslator.Core.Audio;
using GameScreenTranslator.Core.Capture;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Logging;
using GameScreenTranslator.Core.State;

Log.Info("GameScreenTranslator starting...");

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

    // Reads appsettings.json from the current working directory (repo root when launched there).
    var baseDir = Directory.GetCurrentDirectory();
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
    var baseDir = Directory.GetCurrentDirectory();
    var settings = AppSettingsLoader.Load(baseDir);

    ConfigureLogging(settings);

    var speechConfig = SpeechLanguageConfigLoader.Load(baseDir, settings.Providers.AzureSpeech.ConfigPath);

    using var cts = new CancellationTokenSource();

    Log.Info("Hotkey mode running. Press Ctrl+C to exit.");
    Log.Info("  Ctrl+Alt+T = Translate/Cycle");
    Log.Info("  Ctrl+Alt+Q (hold) = Ask Question (record while held)");
    Log.Info("  Ctrl+Alt+X = Stop Speaking");
    Log.Info("");

    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    await using var runner = new GameScreenTranslator.Core.App.HotkeyAppRunner(settings, speechConfig);
    runner.Start();
    runner.Run(cts.Token);
}
else
{
    Log.Info("Run with: --capture-once");
    Log.Info("      or: --cycle-demo");
    Log.Info("      or: --foundry-translate-once [--raw]");
    Log.Info("      or: --hotkey-mode");
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