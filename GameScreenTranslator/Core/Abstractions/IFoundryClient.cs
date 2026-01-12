using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameScreenTranslator.Core.Conversation;
using GameScreenTranslator.Core.Models;
using GameScreenTranslator.Core.State;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GameScreenTranslator.Core.Abstractions;

public interface IFoundryClient
{
    string GetSystemPrompt();

    string PrepareScreenshotDataUrl(Image<Rgba32> screenshot, int? downscaleWidthOverride = null);

    string BuildUserPromptText(
        string windowTitle,
        PriorityCycler.UiPriority priority,
        string targetLanguage,
        string mode,
        string? userQuestion = null);

    Task<TranslateCycleResponse> TranslateCycleAsync(
        Image<Rgba32> screenshot,
        string windowTitle,
        PriorityCycler.UiPriority priority,
        string targetLanguage,
        string mode,
        string? userQuestion = null,
        int? downscaleWidthOverride = null,
        Action<string>? onRawModelContent = null,
        CancellationToken cancellationToken = default);

    Task<TranslateCycleResponse> SendWithMessagesAsync(
        IReadOnlyList<ChatMessageDto> messages,
        Action<string>? onRawModelContent = null,
        CancellationToken cancellationToken = default);

    Task<string> GenerateSummaryAsync(string textToSummarize, CancellationToken cancellationToken = default);
}
