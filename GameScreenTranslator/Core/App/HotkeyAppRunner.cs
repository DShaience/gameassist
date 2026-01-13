using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using System.Threading;
using System.Threading.Tasks;
using GameScreenTranslator.Core.Audio;
using GameScreenTranslator.Core.Capture;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Conversation;
using GameScreenTranslator.Core.Foundry;
using GameScreenTranslator.Core.Hotkeys;
using GameScreenTranslator.Core.Logging;
using GameScreenTranslator.Core.State;

namespace GameScreenTranslator.Core.App;

public sealed class HotkeyAppRunner : IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly SpeechLanguageConfig _speechConfig;
    private readonly PromptPack _prompts;

    private readonly ActiveWindowCapture _capturer = new();
    private readonly ScreenStateManager _state = new();

    private readonly AzureFoundryClient _foundryClient;
    private readonly AzureSpeechTtsService _tts;
    private readonly AzureSpeechSttService _stt;
    private readonly EarconPlayer _earcons;

    private readonly Win32HotkeyManager _hotkeys = new();

    private readonly InteractionLogger _interactionLogger;

    // Conversation support
    private readonly ConversationStore _conversationStore = new();
    private readonly ConversationContextBuilder _conversationContextBuilder;

    private readonly object _sync = new();

    private CancellationTokenSource? _operationCts;
    private bool _isRecordingQuestion;
    private WindowCaptureResult? _questionSnapshot;
    private bool _isDisposed;

    private int _translateHotkeyId;
    private int _stopHotkeyId;
    private int _tapQuestionHotkeyId;
    private int _resetConversationHotkeyId;

    public HotkeyAppRunner(AppSettings settings, SpeechLanguageConfig speechConfig, PromptPack prompts, string baseDirectory)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _speechConfig = speechConfig ?? throw new ArgumentNullException(nameof(speechConfig));
        _prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));

        _interactionLogger = new InteractionLogger(_settings, baseDirectory);

        _foundryClient = new AzureFoundryClient(_settings.Providers.AzureFoundry, _prompts);
        _tts = new AzureSpeechTtsService(_speechConfig);
        _stt = new AzureSpeechSttService(_speechConfig);
        _earcons = new EarconPlayer(_settings.Audio.Earcons, baseDirectory);

        // Initialize conversation context builder with summarizer
        var conversationConfig = new ConversationContextConfig
        {
            Enabled = _settings.Conversation.Enabled,
            MaxVerbatimTurns = _settings.Conversation.MaxVerbatimTurns,
            SummaryMaxChars = _settings.Conversation.SummaryMaxChars
        };

        // Use Foundry to generate summaries
        Func<string, CancellationToken, Task<string>> summarizer = async (text, ct) =>
        {
            return await _foundryClient.GenerateSummaryAsync(text, ct).ConfigureAwait(false);
        };

        _conversationContextBuilder = new ConversationContextBuilder(conversationConfig, summarizer);
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

        // Reset conversation history:
        // Ctrl+Alt+R => clear conversation context
        _resetConversationHotkeyId = _hotkeys.Register(HotkeyModifiers.Control | HotkeyModifiers.Alt, (uint)VirtualKey.R);

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

            if (id == _resetConversationHotkeyId)
            {
                _ = Task.Run(ResetConversationAsync);
                return;
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

        var totalSw = InteractionLogger.StartTimer();
        Stopwatch? captureSw = null;
        Stopwatch? modelSw = null;
        Stopwatch? ttsSw = null;

        WindowCaptureResult? result = null;
        ulong sig = 0;
        int cycleIndex = 0;
        PriorityCycler.UiPriority? priority = null;

        Core.Models.TranslateCycleResponse? response = null;

        var spokenKey = "";
        var skipped = false;
        var spokenText = "";
        string? error = null;

        try
        {
            await _tts.StopAsync().ConfigureAwait(false);

            Log.Info("Translate/Cycle triggered");

            captureSw = InteractionLogger.StartTimer();
            result = _capturer.CaptureActiveWindow();
            captureSw.Stop();

            DebugPrintCapture("Translate/Cycle capture", result);
            DebugSaveCapture(result, "translate_cycle");

            sig = ScreenSignature.Compute(result.WindowTitle, result.Screenshot);
            _state.SetCurrentScreen(sig);

            cycleIndex = _state.GetCycleIndex();
            priority = PriorityCycler.GetPriorityForCycleIndex(cycleIndex);

            modelSw = InteractionLogger.StartTimer();
            response = await _foundryClient.TranslateCycleAsync(
                    screenshot: result.Screenshot,
                    windowTitle: result.WindowTitle,
                    priority: priority.Value,
                    targetLanguage: _settings.Translation.TargetLanguage,
                    mode: _settings.Translation.Mode,
                    userQuestion: null,
                    downscaleWidthOverride: _settings.ScreenCapture.DownscaleWidth,
                    onRawModelContent: raw =>
                    {
                        Log.Debug("[Foundry Raw]");
                        Log.Debug(raw);
                    },
                    cancellationToken: cts.Token)
                .ConfigureAwait(false);
            modelSw.Stop();

            DebugPrintFoundryResponse(response);

            spokenKey = $"{response.SelectedLabel}|{response.SourceText}|{response.TranslatedText}";
            if (_state.HasSpoken(spokenKey))
            {
                skipped = true;
            }
            else
            {
                _state.MarkSpoken(spokenKey);

                spokenText = BuildSpeakText(response);

                Log.Info($"TTS: {spokenText}");
                ttsSw = InteractionLogger.StartTimer();
                await _tts.SpeakAsync(spokenText, _settings.Translation.TargetLanguage, cts.Token).ConfigureAwait(false);
                ttsSw.Stop();
            }

            _state.AdvanceCycle();
        }
        catch (Exception ex)
        {
            error = ex.ToString();

            Log.Error(ex, "Translate/Cycle failed");

            try
            {
                var fallback = FallbackSpeech.ResolveGenericErrorMessage(_settings, _settings.Translation.TargetLanguage);
                Log.Info($"TTS (fallback): {fallback}");
                await _tts.SpeakAsync(fallback, _settings.Translation.TargetLanguage, cts.Token).ConfigureAwait(false);
            }
            catch
            {
                // ignore fallback failures
            }
        }
        finally
        {
            totalSw.Stop();

            try
            {
                var entry = new InteractionLogEntry
                {
                    InteractionType = "translate_cycle",
                    WindowTitle = result?.WindowTitle ?? "",
                    ProcessName = result?.ProcessName,
                    ProcessId = result?.ProcessId,
                    HwndHex = result is null ? null : $"0x{result.Hwnd.ToInt64():X}",
                    ScreenSignature = result is null ? "" : sig.ToString("X16"),
                    CycleIndex = result is null ? null : cycleIndex,
                    Priority = priority?.ToString(),
                    SelectedRegion = response is null
                        ? null
                        : new InteractionLogEntry.SelectedRegionData
                        {
                            SelectedLabel = response.SelectedLabel,
                            SourceText = response.SourceText
                        },
                    Translation = response is null
                        ? null
                        : new InteractionLogEntry.TranslationData
                        {
                            TargetLanguage = _settings.Translation.TargetLanguage,
                            Mode = _settings.Translation.Mode,
                            TranslatedText = response.TranslatedText,
                            Explanation = response.Explanation,
                            SpokenText = spokenText
                        },
                    LatencyMs = new InteractionLogEntry.LatencyData
                    {
                        Capture = InteractionLogger.ElapsedMs(captureSw),
                        ModelCall = InteractionLogger.ElapsedMs(modelSw),
                        Tts = InteractionLogger.ElapsedMs(ttsSw),
                        Total = InteractionLogger.ElapsedMs(totalSw)
                    },
                    SkippedBecauseAlreadySpoken = skipped ? true : null,
                    Error = error
                };

                if (result is not null)
                    entry.ScreenshotPngBase64 = _interactionLogger.EncodeScreenshotPngBase64(result.Screenshot);

                _interactionLogger.Append(entry);
            }
            catch (Exception logEx)
            {
                Log.Warn($"Failed to write interaction log: {logEx.Message}");
            }

            if (result is not null)
                result.Screenshot.Dispose();
        }
    }

    private async Task TapQuestionAutoStopAsync()
    {
        CancelCurrentOperation();

        using var cts = NewOperationCts();

        var totalSw = InteractionLogger.StartTimer();
        Stopwatch? captureSw = null;
        Stopwatch? modelSw = null;
        Stopwatch? ttsSw = null;

        WindowCaptureResult? snap = null;
        ulong sig = 0;
        var question = "";
        Core.Models.TranslateCycleResponse? response = null;
        var spokenText = "";
        string? error = null;
        var conversationTurnCount = 0;
        string? conversationContextTextForLog = null;

        CancellationTokenSource? thinkingChimeCts = null;
        try
        {
            await _tts.StopAsync().ConfigureAwait(false);

            // Play listening earcon immediately so user knows the hotkey was recognized.
            // Fire-and-forget; the earcon plays on a background thread.
            _earcons.PlayListeningChime();

            // Capture immediately so the screenshot matches what the user is asking about.
            captureSw = InteractionLogger.StartTimer();
            snap = _capturer.CaptureActiveWindow();
            captureSw.Stop();

            DebugPrintCapture("Tap-to-talk capture", snap);
            DebugSaveCapture(snap, "tap_to_talk");

            sig = ScreenSignature.Compute(snap.WindowTitle, snap.Screenshot);

            Log.Info("Tap-to-talk: speak now (auto-stops after 1.5s silence)...");
            question = await _stt.RecognizeOnceWithEndSilenceTimeoutAsync(1500, cts.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(question))
            {
                Log.Info("No question recognized");
                return;
            }

            Log.Info($"Question: {question}");
            Log.Debug($"Asking about captured window: {snap.WindowTitle}");

            // Start a delayed "thinking" earcon. If the model responds very quickly,
            // the cancellation below prevents the chime from playing.
            thinkingChimeCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(Math.Max(0, _settings.Audio.Earcons.ThinkingDelayMs), thinkingChimeCts.Token).ConfigureAwait(false);
                    _earcons.PlayThinkingChime();
                }
                catch
                {
                    // ignore
                }
            });

            // Build conversation context
            var imageDataUrl = _foundryClient.PrepareScreenshotDataUrl(snap.Screenshot, _settings.ScreenCapture.DownscaleWidth);
            var userPrompt = _foundryClient.BuildUserPromptText(
                snap.WindowTitle,
                PriorityCycler.UiPriority.Highlighted,
                _settings.Translation.TargetLanguage,
                _settings.Translation.Mode,
                question);

            conversationTurnCount = _conversationStore.TurnCount;

            // Build messages with conversation history
            var messages = await _conversationContextBuilder.BuildMessagesAsync(
                _foundryClient.GetSystemPrompt(),
                userPrompt,
                imageDataUrl,
                _conversationStore,
                cts.Token).ConfigureAwait(false);

            Log.Debug($"Conversation turn count: {conversationTurnCount}, Messages: {messages.Count}");

            conversationContextTextForLog = BuildConversationContextTextForLog(question);

            modelSw = InteractionLogger.StartTimer();
            response = await _foundryClient.SendWithMessagesAsync(
                    messages,
                    onRawModelContent: raw =>
                    {
                        Log.Debug("[Foundry Raw]");
                        Log.Debug(raw);
                    },
                    cancellationToken: cts.Token)
                .ConfigureAwait(false);
            modelSw.Stop();

            try { thinkingChimeCts.Cancel(); } catch { /* ignore */ }

            DebugPrintFoundryResponse(response);

            spokenText = BuildSpeakText(response);

            // Store conversation turns (only on success)
            _conversationStore.AppendUser(question);
            _conversationStore.AppendAssistant(spokenText);

            Log.Info($"TTS: {spokenText}");
            ttsSw = InteractionLogger.StartTimer();
            await _tts.SpeakAsync(spokenText, _settings.Translation.TargetLanguage, cts.Token).ConfigureAwait(false);
            ttsSw.Stop();
        }
        catch (Exception ex)
        {
            error = ex.ToString();
            Log.Error(ex, "Tap-to-talk flow failed");

            try
            {
                var fallback = FallbackSpeech.ResolveGenericErrorMessage(_settings, _settings.Translation.TargetLanguage);
                Log.Info($"TTS (fallback): {fallback}");
                await _tts.SpeakAsync(fallback, _settings.Translation.TargetLanguage, cts.Token).ConfigureAwait(false);
            }
            catch
            {
                // ignore fallback failures
            }
        }
        finally
        {
            totalSw.Stop();

            if (thinkingChimeCts is not null)
            {
                try { thinkingChimeCts.Cancel(); } catch { /* ignore */ }
                thinkingChimeCts.Dispose();
            }

            try
            {
                var entry = new InteractionLogEntry
                {
                    InteractionType = "question_tap",
                    WindowTitle = snap?.WindowTitle ?? "",
                    ProcessName = snap?.ProcessName,
                    ProcessId = snap?.ProcessId,
                    HwndHex = snap is null ? null : $"0x{snap.Hwnd.ToInt64():X}",
                    ScreenSignature = snap is null ? "" : sig.ToString("X16"),
                    Priority = PriorityCycler.UiPriority.Highlighted.ToString(),
                    UserQuestion = string.IsNullOrWhiteSpace(question) ? null : question,
                    ConversationTurnIndex = conversationTurnCount,
                    ConversationContextText = conversationContextTextForLog,
                    SelectedRegion = response is null
                        ? null
                        : new InteractionLogEntry.SelectedRegionData
                        {
                            SelectedLabel = response.SelectedLabel,
                            SourceText = response.SourceText
                        },
                    Translation = response is null
                        ? null
                        : new InteractionLogEntry.TranslationData
                        {
                            TargetLanguage = _settings.Translation.TargetLanguage,
                            Mode = _settings.Translation.Mode,
                            TranslatedText = response.TranslatedText,
                            Explanation = response.Explanation,
                            SpokenText = spokenText
                        },
                    LatencyMs = new InteractionLogEntry.LatencyData
                    {
                        Capture = InteractionLogger.ElapsedMs(captureSw),
                        ModelCall = InteractionLogger.ElapsedMs(modelSw),
                        Tts = InteractionLogger.ElapsedMs(ttsSw),
                        Total = InteractionLogger.ElapsedMs(totalSw)
                    },
                    Error = error
                };

                if (snap is not null)
                    entry.ScreenshotPngBase64 = _interactionLogger.EncodeScreenshotPngBase64(snap.Screenshot);

                _interactionLogger.Append(entry);
            }
            catch (Exception logEx)
            {
                Log.Warn($"Failed to write interaction log: {logEx.Message}");
            }

            snap?.Screenshot.Dispose();
        }
    }

    private async Task ResetConversationAsync()
    {
        try
        {
            Log.Info("Reset conversation triggered");

            var turnCount = _conversationStore.TurnCount;
            _conversationStore.Reset();
            _conversationContextBuilder.ResetSummary();

            Log.Info($"Conversation cleared ({turnCount} turns)");

            // Speak confirmation
            var confirmationMessage = _settings.Translation.TargetLanguage.ToLowerInvariant() switch
            {
                "hebrew" => "השיחה נמחקה.",
                "russian" => "Разговор сброшен.",
                _ => "Conversation cleared."
            };

            using var cts = NewOperationCts();
            await _tts.SpeakAsync(confirmationMessage, _settings.Translation.TargetLanguage, cts.Token).ConfigureAwait(false);

            // Log the reset
            try
            {
                _interactionLogger.Append(new InteractionLogEntry
                {
                    InteractionType = "conversation_reset",
                    ConversationTurnIndex = turnCount
                });
            }
            catch (Exception logEx)
            {
                Log.Warn($"Failed to write reset interaction log: {logEx.Message}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Reset conversation failed: {ex.Message}");
        }
    }

    private string? BuildConversationContextTextForLog(string currentQuestion)
    {
        if (!_settings.Conversation.Enabled)
            return null;

        if (!_settings.Conversation.Logging.IncludeContextInDebug)
            return null;

        if (!Log.IsEnabled(LogLevel.Debug))
            return null;

        var recentCount = Math.Max(0, _settings.Conversation.MaxVerbatimTurns) * 2;
        var recentTurns = _conversationStore.GetTurns();
        if (recentCount > 0 && recentTurns.Count > recentCount)
            recentTurns = recentTurns.TakeLast(recentCount).ToArray();

        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(_conversationContextBuilder.RollingSummary))
        {
            sb.AppendLine("RollingSummary:");
            sb.AppendLine(_conversationContextBuilder.RollingSummary);
            sb.AppendLine();
        }

        if (recentTurns.Count > 0)
        {
            sb.AppendLine("RecentTurns:");
            foreach (var t in recentTurns)
            {
                sb.AppendLine($"{t.Role}: {t.Text}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("CurrentQuestion:");
        sb.AppendLine(currentQuestion);

        return sb.ToString().Trim();
    }

    private async Task StopAsync()
    {
        try
        {
            Log.Info("Stop triggered");
            CancelCurrentOperation();

            try
            {
                await _tts.StopAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // ignore (shutdown race)
            }

            _earcons.PlayCancelChime();

            try
            {
                _interactionLogger.Append(new InteractionLogEntry
                {
                    InteractionType = "stop"
                });
            }
            catch (Exception logEx)
            {
                Log.Warn($"Failed to write stop interaction log: {logEx.Message}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Stop failed: {ex.Message}");
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
        if (!Log.IsEnabled(LogLevel.Debug))
            return;

        Log.Debug($"[Foundry Parsed] SelectedLabel={response.SelectedLabel}, SourceText={response.SourceText}, TranslatedText={response.TranslatedText}" +
            (string.IsNullOrWhiteSpace(response.Explanation) ? "" : $", Explanation={response.Explanation}"));
    }

    private static void DebugPrintCapture(string prefix, WindowCaptureResult capture)
    {
        if (!Log.IsEnabled(LogLevel.Debug))
            return;

        Log.Debug($"{prefix}: Title={capture.WindowTitle}, Process={capture.ProcessName ?? "(unknown)"} ({capture.ProcessId}), Hwnd=0x{capture.Hwnd.ToInt64():X}, Size={capture.Screenshot.Width}x{capture.Screenshot.Height}");
    }

    private static void DebugSaveCapture(WindowCaptureResult capture, string tag)
    {
        if (!Log.IsEnabled(LogLevel.Debug))
            return;

        try
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "logs", "captures");
            Directory.CreateDirectory(dir);

            var safeTitle = MakeSafeFilePart(capture.WindowTitle);
            var ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var path = Path.Combine(dir, $"{ts}_{tag}_{safeTitle}.png");

            capture.Screenshot.Save(path, new PngEncoder());
            Log.Debug($"Saved screenshot: {path}");
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to save screenshot: {ex.Message}");
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
        _hotkeys.Unregister(_resetConversationHotkeyId);

        _hotkeys.Dispose();

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
