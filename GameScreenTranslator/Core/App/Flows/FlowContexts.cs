using System.Diagnostics;
using GameScreenTranslator.Core.Capture;
using GameScreenTranslator.Core.Logging;
using GameScreenTranslator.Core.Models;
using GameScreenTranslator.Core.State;

namespace GameScreenTranslator.Core.App.Flows;

public sealed class TranslateCycleContext
{
    public Stopwatch TotalSw { get; } = InteractionLogger.StartTimer();
    public Stopwatch? CaptureSw { get; set; }
    public Stopwatch? ModelSw { get; set; }
    public Stopwatch? TtsSw { get; set; }

    public WindowCaptureResult? Capture { get; set; }
    public ulong Signature { get; set; }
    public int CycleIndex { get; set; }
    public PriorityCycler.UiPriority? Priority { get; set; }

    public TranslateCycleResponse? Response { get; set; }
    public string SpokenText { get; set; } = "";
    public string SpokenKey { get; set; } = "";
    public bool SkippedBecauseAlreadySpoken { get; set; }
    public string? Error { get; set; }
}

public sealed class TapToTalkQuestionContext
{
    public Stopwatch TotalSw { get; } = InteractionLogger.StartTimer();
    public Stopwatch? CaptureSw { get; set; }
    public Stopwatch? ModelSw { get; set; }
    public Stopwatch? TtsSw { get; set; }

    public WindowCaptureResult? Capture { get; set; }
    public ulong Signature { get; set; }

    public string Question { get; set; } = "";
    public int ConversationTurnCount { get; set; }
    public string? ConversationContextTextForLog { get; set; }

    public TranslateCycleResponse? Response { get; set; }
    public string SpokenText { get; set; } = "";
    public string? Error { get; set; }
}
