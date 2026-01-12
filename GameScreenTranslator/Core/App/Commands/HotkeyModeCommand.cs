using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Logging;

namespace GameScreenTranslator.Core.App.Commands;

public sealed class HotkeyModeCommand : ICliCommand
{
    public string Name => "--hotkey-mode";
    public bool RequiresFullBootstrap => true;

    public async Task RunAsync(RuntimeContext? ctx, string[] args)
    {
        if (ctx is null)
            throw new InvalidOperationException("HotkeyModeCommand requires RuntimeContext.");

        using var cts = new CancellationTokenSource();

        // Start a system-tray icon on an STA thread.
        var trayThread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using var trayCtx = new TrayAppContext(cts, ctx.LogsDirectory);
                Application.Run(trayCtx);
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
        Log.Info("  Ctrl+Alt+W = Tap-to-talk test (auto-stops after ~1.5s silence)");
        Log.Info("  Ctrl+Alt+X = Stop Speaking");
        Log.Info("  Ctrl+Alt+R = Reset Conversation");
        Log.Info("");

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await using var runner = new HotkeyAppRunner(ctx.Settings, ctx.SpeechConfig, ctx.Prompts, ctx.BaseDirectory);
        runner.Start();
        runner.Run(cts.Token);

        // Best-effort: give tray loop a moment to exit.
        try { trayThread.Join(1500); } catch { /* ignore */ }
    }
}
