using System;
using System.Threading.Tasks;
using GameScreenTranslator.Core.Capture;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Logging;
using GameScreenTranslator.Core.State;

namespace GameScreenTranslator.Core.App.Commands;

public sealed class CaptureOnceCommand : ICliCommand
{
    public string Name => "--capture-once";
    public bool RequiresFullBootstrap => false;

    public async Task RunAsync(RuntimeContext? ctx, string[] args)
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

        result.Screenshot.Dispose();
    }
}
