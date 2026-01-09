using System;
using System.IO;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using System.Threading;
using System.Threading.Tasks;
using GameScreenTranslator.Core.Audio;
using GameScreenTranslator.Core.Capture;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Foundry;
using GameScreenTranslator.Core.Hotkeys;
using GameScreenTranslator.Core.State;

namespace GameScreenTranslator.Core.App;

public sealed class HotkeyAppRunner : IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly SpeechLanguageConfig _speechConfig;

    private readonly ActiveWindowCapture _capturer = new();
    private readonly ScreenStateManager _state = new();

    private readonly AzureFoundryClient _foundryClient;
    private readonly AzureSpeechTtsService _tts;
    private readonly AzureSpeechSttService _stt;

    private readonly Win32HotkeyManager _hotkeys = new();
    private readonly GlobalKeyboardHook _keyboardHook = new();

    private readonly object _sync = new();

    private CancellationTokenSource? _operationCts;
    private bool _isRecordingQuestion;
    private WindowCaptureResult? _questionSnapshot;
    private bool _isDisposed;

    private int _translateHotkeyId;
    private int _stopHotkeyId;
    private int _tapQuestionHotkeyId;

    public HotkeyAppRunner(AppSettings settings, SpeechLanguageConfig speechConfig)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _speechConfig = speechConfig ?? throw new ArgumentNullException(nameof(speechConfig));

        _foundryClient = new AzureFoundryClient(_settings.Providers.AzureFoundry);
        _tts = new AzureSpeechTtsService(_speechConfig);
        _stt = new AzureSpeechSttService(_speechConfig);
    }

    public void Start()
    {
        // Register non-hold hotkeys:
        // Ctrl+Alt+T => translate/cycle
        // Ctrl+Alt+X => stop speaking / cancel
        _translateHotkeyId = _hotkeys.Register(HotkeyModifiers.Control | HotkeyModifiers.Alt, (uint)VirtualKey.T);
        _stopHotkeyId = _hotkeys.Register(HotkeyModifiers.Control | HotkeyModifiers.Alt, (uint)VirtualKey.X);

        // Tap-to-talk (auto end-of-speech) test mode:
        // Ctrl+Alt+W => start STT recognize-once with end silence timeout, then ask Foundry and speak
        _tapQuestionHotkeyId = _hotkeys.Register(HotkeyModifiers.Control | HotkeyModifiers.Alt, (uint)VirtualKey.W);

        _hotkeys.HotkeyPressed += (_, id) =>
        {
            lock (_sync)
            {
                if (_isDisposed)
                    return;
            }

            if (id == _translateHotkeyId)
            {
                _ = Task.Run(TranslateCycleAsync);
                return;
            }

            if (id == _stopHotkeyId)
            {
                _ = Task.Run(StopAsync);
                return;
            }

            if (id == _tapQuestionHotkeyId)
            {
                _ = Task.Run(TapQuestionAutoStopAsync);
                return;
            }
        };

        // Hold-to-talk:
        // Ctrl+Alt+Q DOWN => start STT continuous
        // Ctrl+Alt+Q UP   => stop STT, then capture -> LLM answer -> TTS
        _keyboardHook.KeyDown += (_, e) =>
        {
            if (e.VkCode == (uint)VirtualKey.Q && IsCtrlAltDown())
            {
                lock (_sync)
                {
                    if (_isDisposed)
                        return;

                    if (_isRecordingQuestion)
                        return;

                    _isRecordingQuestion = true;
                }

                // Capture synchronously on the hook thread (avoid focus changing before Task.Run executes).
                try
                {
                    var snap = _capturer.CaptureActiveWindow();
                    lock (_sync)
                    {
                        _questionSnapshot?.Screenshot.Dispose();
                        _questionSnapshot = snap;
                    }

                    DebugPrintCapture("[Capture] Question key-down", snap);
                    DebugSaveCapture(snap, "question_keydown");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Capture] Question key-down capture failed: {ex.Message}");
                }

                _ = Task.Run(StartQuestionRecordingAsync);
            }
        };

        _keyboardHook.KeyUp += (_, e) =>
        {
            if (e.VkCode == (uint)VirtualKey.Q)
            {
                var shouldStop = false;

                lock (_sync)
                {
                    if (_isDisposed)
                        return;

                    if (_isRecordingQuestion)
                    {
                        _isRecordingQuestion = false;
                        shouldStop = true;
                    }
                }

                if (shouldStop)
                    _ = Task.Run(StopQuestionAndAnswerAsync);
            }
        };
    }

    public void Run(CancellationToken cancellationToken)
    {
        // Blocks current thread until cancellation (message loop).
        _hotkeys.RunMessageLoop(cancellationToken);
    }

    private async Task TranslateCycleAsync()
    {
        CancelCurrentOperation();

        using var cts = NewOperationCts();
        try
        {
            await _tts.StopAsync().ConfigureAwait(false);

            Console.WriteLine("[Hotkey] Translate/Cycle triggered...");

            var result = _capturer.CaptureActiveWindow();
            try
            {
                DebugPrintCapture("[Capture] Translate/Cycle", result);
                DebugSaveCapture(result, "translate_cycle");
                var sig = ScreenSignature.Compute(result.WindowTitle, result.Screenshot);
                _state.SetCurrentScreen(sig);

                var cycleIndex = _state.GetCycleIndex();
                var priority = PriorityCycler.GetPriorityForCycleIndex(cycleIndex);

                var response = await _foundryClient.TranslateCycleAsync(
                        screenshot: result.Screenshot,
                        windowTitle: result.WindowTitle,
                        priority: priority,
                        targetLanguage: _settings.Translation.TargetLanguage,
                        mode: _settings.Translation.Mode,
                        userQuestion: null,
                        downscaleWidthOverride: _settings.ScreenCapture.DownscaleWidth,
                        onRawModelContent: raw =>
                        {
                            Console.WriteLine();
                            Console.WriteLine("[Foundry Raw]");
                            Console.WriteLine(raw);
                            Console.WriteLine();
                        },
                        cancellationToken: cts.Token)
                    .ConfigureAwait(false);

                DebugPrintFoundryResponse(response);

                // Mark spoken to avoid repeats on same screen.
                // For P0, a simple key is enough.
                var spokenKey = $"{response.SelectedLabel}|{response.SourceText}|{response.TranslatedText}";
                if (!_state.HasSpoken(spokenKey))
                {
                    _state.MarkSpoken(spokenKey);

                    var toSpeak = BuildSpeakText(response);

                    Console.WriteLine($"[TTS] {toSpeak}");
                    await _tts.SpeakAsync(toSpeak, _settings.Translation.TargetLanguage, cts.Token).ConfigureAwait(false);
                }

                _state.AdvanceCycle();
            }
            finally
            {
                result.Screenshot.Dispose();
            }
        }
        catch (Exception ex)
        {
            // Use ToString() so we also see the inner exception (HTTP status + body from AzureFoundryClient).
            Console.WriteLine($"[Hotkey] Translate/Cycle failed:\n{ex}");
        }
    }

    private async Task StartQuestionRecordingAsync()
    {
        CancelCurrentOperation();

        using var cts = NewOperationCts();
        try
        {
            await _tts.StopAsync().ConfigureAwait(false);

            WindowCaptureResult? snap = null;
            lock (_sync)
            {
                snap = _questionSnapshot;
            }

            Console.WriteLine($"[Hotkey] Hold-to-talk: recording started (captured: {snap?.WindowTitle ?? "(none)"})...");
            await _stt.StartAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hotkey] STT start failed: {ex.Message}");
        }
    }

    private async Task StopQuestionAndAnswerAsync()
    {
        using var cts = NewOperationCts();

        try
        {
            Console.WriteLine("[Hotkey] Hold-to-talk: recording stopped...");
            var question = await _stt.StopAsync(cts.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(question))
            {
                Console.WriteLine("[Hotkey] No question recognized.");
                return;
            }

            Console.WriteLine($"[Hotkey] Question: {question}");

            WindowCaptureResult? snap = null;
            lock (_sync)
            {
                snap = _questionSnapshot;
                _questionSnapshot = null;
            }

            // Fallback in case key-down capture failed for any reason.
            snap ??= _capturer.CaptureActiveWindow();

            try
            {
                Console.WriteLine($"[Hotkey] Asking about captured window: {snap.WindowTitle}");

                var response = await _foundryClient.TranslateCycleAsync(
                        screenshot: snap.Screenshot,
                        windowTitle: snap.WindowTitle,
                        priority: PriorityCycler.UiPriority.Highlighted,
                        targetLanguage: _settings.Translation.TargetLanguage,
                        mode: _settings.Translation.Mode,
                        userQuestion: question,
                        downscaleWidthOverride: _settings.ScreenCapture.DownscaleWidth,
                        onRawModelContent: raw =>
                        {
                            Console.WriteLine();
                            Console.WriteLine("[Foundry Raw]");
                            Console.WriteLine(raw);
                            Console.WriteLine();
                        },
                        cancellationToken: cts.Token)
                    .ConfigureAwait(false);

                DebugPrintFoundryResponse(response);

                // For question mode, we expect the model to place the answer in TranslatedText.
                var toSpeak = BuildSpeakText(response);

                Console.WriteLine($"[TTS] {toSpeak}");
                await _tts.SpeakAsync(toSpeak, _settings.Translation.TargetLanguage, cts.Token).ConfigureAwait(false);
            }
            finally
            {
                snap.Screenshot.Dispose();
            }
        }
        catch (Exception ex)
        {
            // Use ToString() so we also see the inner exception (HTTP status + body from AzureFoundryClient).
            Console.WriteLine($"[Hotkey] Question flow failed:\n{ex}");
        }
    }

    private async Task TapQuestionAutoStopAsync()
    {
        CancelCurrentOperation();

        using var cts = NewOperationCts();
        try
        {
            await _tts.StopAsync().ConfigureAwait(false);

            // Capture immediately so the screenshot matches what the user is asking about.
            var snap = _capturer.CaptureActiveWindow();
            try
            {
                DebugPrintCapture("[Capture] Tap-to-talk", snap);
                DebugSaveCapture(snap, "tap_to_talk");

                Console.WriteLine("[Hotkey] Tap-to-talk: speak now (auto-stops after 1.5s silence)...");
                var question = await _stt.RecognizeOnceWithEndSilenceTimeoutAsync(1500, cts.Token).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(question))
                {
                    Console.WriteLine("[Hotkey] No question recognized.");
                    return;
                }

                Console.WriteLine($"[Hotkey] Question: {question}");
                Console.WriteLine($"[Hotkey] Asking about captured window: {snap.WindowTitle}");

                var response = await _foundryClient.TranslateCycleAsync(
                        screenshot: snap.Screenshot,
                        windowTitle: snap.WindowTitle,
                        priority: PriorityCycler.UiPriority.Highlighted,
                        targetLanguage: _settings.Translation.TargetLanguage,
                        mode: _settings.Translation.Mode,
                        userQuestion: question,
                        downscaleWidthOverride: _settings.ScreenCapture.DownscaleWidth,
                        onRawModelContent: raw =>
                        {
                            Console.WriteLine();
                            Console.WriteLine("[Foundry Raw]");
                            Console.WriteLine(raw);
                            Console.WriteLine();
                        },
                        cancellationToken: cts.Token)
                    .ConfigureAwait(false);

                DebugPrintFoundryResponse(response);

                var toSpeak = BuildSpeakText(response);

                Console.WriteLine($"[TTS] {toSpeak}");
                await _tts.SpeakAsync(toSpeak, _settings.Translation.TargetLanguage, cts.Token).ConfigureAwait(false);
            }
            finally
            {
                snap.Screenshot.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hotkey] Tap-to-talk flow failed:\n{ex}");
        }
    }

    private async Task StopAsync()
    {
        try
        {
            Console.WriteLine("[Hotkey] Stop triggered...");
            CancelCurrentOperation();

            try
            {
                await _tts.StopAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // ignore (shutdown race)
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hotkey] Stop failed: {ex.Message}");
        }
    }

    private void CancelCurrentOperation()
    {
        lock (_sync)
        {
            try
            {
                _operationCts?.Cancel();
            }
            catch
            {
                // ignore
            }
            finally
            {
                _operationCts?.Dispose();
                _operationCts = null;
            }
        }
    }

    private CancellationTokenSource NewOperationCts()
    {
        lock (_sync)
        {
            _operationCts?.Dispose();
            _operationCts = new CancellationTokenSource();
            return _operationCts;
        }
    }

    private static bool IsCtrlAltDown()
    {
        // VK_CONTROL = 0x11, VK_MENU (ALT) = 0x12
        return IsKeyDown(0x11) && IsKeyDown(0x12);
    }

    private static bool IsKeyDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static string BuildSpeakText(Core.Models.TranslateCycleResponse response)
    {
        // Avoid speaking just "." when translatedText is empty.
        if (response is null)
            return "";

        var translated = (response.TranslatedText ?? "").Trim();
        var explanation = (response.Explanation ?? "").Trim();
        var source = (response.SourceText ?? "").Trim();

        if (translated.Length == 0)
        {
            if (explanation.Length > 0)
                return explanation;

            if (source.Length > 0)
                return source;

            return "לא הצלחתי למצוא טקסט קריא במסך הזה.";
        }

        if (explanation.Length > 0)
            return $"{translated}. {explanation}";

        return translated;
    }

    private static void DebugPrintFoundryResponse(Core.Models.TranslateCycleResponse response)
    {
        Console.WriteLine("[Foundry Parsed]");
        Console.WriteLine($"  SelectedLabel: {response.SelectedLabel}");
        Console.WriteLine($"  SourceText:    {response.SourceText}");
        Console.WriteLine($"  TranslatedText:{response.TranslatedText}");
        if (!string.IsNullOrWhiteSpace(response.Explanation))
            Console.WriteLine($"  Explanation:   {response.Explanation}");
        Console.WriteLine();
    }

    private static void DebugPrintCapture(string prefix, WindowCaptureResult capture)
    {
        Console.WriteLine($"{prefix}:");
        Console.WriteLine($"  Title:   {capture.WindowTitle}");
        Console.WriteLine($"  Process: {capture.ProcessName ?? "(unknown)"} ({capture.ProcessId})");
        Console.WriteLine($"  Hwnd:    0x{capture.Hwnd.ToInt64():X}");
        Console.WriteLine($"  Size:    {capture.Screenshot.Width}x{capture.Screenshot.Height}");
        Console.WriteLine();
    }

    private static void DebugSaveCapture(WindowCaptureResult capture, string tag)
    {
        try
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "logs", "captures");
            Directory.CreateDirectory(dir);

            var safeTitle = MakeSafeFilePart(capture.WindowTitle);
            var ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var path = Path.Combine(dir, $"{ts}_{tag}_{safeTitle}.png");

            capture.Screenshot.Save(path, new PngEncoder());
            Console.WriteLine($"[Capture] Saved screenshot: {path}");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Capture] Failed to save screenshot: {ex.Message}");
        }
    }

    private static string MakeSafeFilePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "window";

        var s = value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');

        // keep filenames reasonable
        return s.Length <= 60 ? s : s[..60];
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _isDisposed = true;
        }

        _hotkeys.Unregister(_translateHotkeyId);
        _hotkeys.Unregister(_stopHotkeyId);
        _hotkeys.Unregister(_tapQuestionHotkeyId);

        _hotkeys.Dispose();
        _keyboardHook.Dispose();

        lock (_sync)
        {
            _questionSnapshot?.Screenshot.Dispose();
            _questionSnapshot = null;
        }

        CancelCurrentOperation();

        await _tts.DisposeAsync().ConfigureAwait(false);
        await _stt.DisposeAsync().ConfigureAwait(false);
    }
}
