using System;
using System.Threading.Tasks;
using GameScreenTranslator.Core.Capture;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Logging;
using GameScreenTranslator.Core.State;

namespace GameScreenTranslator.Core.App.Commands;

public sealed class CycleDemoCommand : ICliCommand
{
    public string Name => "--cycle-demo";
    public bool RequiresFullBootstrap => false;

    public Task RunAsync(RuntimeContext? ctx, string[] args)
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
            result.Screenshot.Dispose();
        }

        return Task.CompletedTask;
    }
}
