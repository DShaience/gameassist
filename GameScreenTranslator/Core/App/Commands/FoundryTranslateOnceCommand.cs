using System;
using System.Linq;
using System.Threading.Tasks;
using GameScreenTranslator.Core.Audio;
using GameScreenTranslator.Core.Capture;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Foundry;
using GameScreenTranslator.Core.Logging;
using GameScreenTranslator.Core.State;

namespace GameScreenTranslator.Core.App.Commands;

public sealed class FoundryTranslateOnceCommand : ICliCommand
{
    public string Name => "--foundry-translate-once";
    public bool RequiresFullBootstrap => true;

    public async Task RunAsync(RuntimeContext? ctx, string[] args)
    {
        if (ctx is null)
            throw new InvalidOperationException("FoundryTranslateOnceCommand requires RuntimeContext.");

        var printRaw = args.Any(a => a.Equals("--raw", StringComparison.OrdinalIgnoreCase));

        var capturer = new ActiveWindowCapture();

        Log.Info("Switch to the target window now (you have ~2 seconds)...");
        await Task.Delay(2000);

        var result = capturer.CaptureActiveWindow();

        try
        {
            var priority = PriorityCycler.UiPriority.Highlighted;

            var client = new AzureFoundryClient(ctx.Settings.Providers.AzureFoundry, ctx.Prompts);

            var response = await client.TranslateCycleAsync(
                screenshot: result.Screenshot,
                windowTitle: result.WindowTitle,
                priority: priority,
                targetLanguage: ctx.Settings.Translation.TargetLanguage,
                mode: ctx.Settings.Translation.Mode,
                downscaleWidthOverride: ctx.Settings.ScreenCapture.DownscaleWidth,
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

            // Minimal TTS behavior for the dev demo.
            var toSpeak = response.TranslatedText;
            if (!string.IsNullOrWhiteSpace(response.Explanation))
                toSpeak = $"{toSpeak}. {response.Explanation}";

            await using var tts = new AzureSpeechTtsService(ctx.SpeechConfig);
            await tts.SpeakAsync(toSpeak, ctx.Settings.Translation.TargetLanguage);
        }
        finally
        {
            result.Screenshot.Dispose();
        }
    }
}
