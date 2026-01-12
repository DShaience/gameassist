// See https://aka.ms/new-console-template for more information
using System;
using GameScreenTranslator.Core.App.Commands;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Logging;

Log.Info("GameScreenTranslator starting...");

// Register all available commands.
var commands = new ICliCommand[]
{
    new HotkeyModeCommand(),
    new CaptureOnceCommand(),
    new CycleDemoCommand(),
    new FoundryTranslateOnceCommand()
};

try
{
    // Default mode: hotkey mode when no args are supplied.
    var effectiveArgs = args.Length == 0 ? ["--hotkey-mode"] : args;

    // --help / -h
    if (effectiveArgs[0].Equals("--help", StringComparison.OrdinalIgnoreCase) ||
        effectiveArgs[0].Equals("-h", StringComparison.OrdinalIgnoreCase))
    {
        PrintUsage(commands);
        return;
    }

    // Find matching command.
    ICliCommand? matched = null;
    foreach (var cmd in commands)
    {
        if (effectiveArgs[0].Equals(cmd.Name, StringComparison.OrdinalIgnoreCase))
        {
            matched = cmd;
            break;
        }
    }

    if (matched is null)
    {
        PrintUsage(commands);
        return;
    }

    // Bootstrap only if needed (some commands don't require config).
    RuntimeContext? ctx = null;
    if (matched.RequiresFullBootstrap)
    {
        try
        {
            ctx = RuntimeBootstrapper.Load();
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "Configuration error. Edit 'appsettings.json' and 'speech_language_config.json' next to the executable, then run again.");
            return;
        }
    }

    await matched.RunAsync(ctx, effectiveArgs);
}
catch (Exception ex)
{
    Log.Error(ex,
        "Fatal error. If running a published EXE, ensure config files exist next to the EXE (or let it auto-create templates on first run).");
}

static void PrintUsage(ICliCommand[] commands)
{
    Log.Info("Default: (no args) runs --hotkey-mode");
    foreach (var cmd in commands)
    {
        Log.Info($"      or: {cmd.Name}");
    }
    Log.Info("      or: --help / -h");
}
