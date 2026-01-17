using System;
using System.Collections.Generic;
using GameScreenTranslator.Core.Configuration;
using GameScreenTranslator.Core.Conversation;
using GameScreenTranslator.Core.Imaging;
using GameScreenTranslator.Core.State;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GameScreenTranslator.Core.Foundry;

/// <summary>
/// Builds prompts and request messages for Foundry/OpenAI API calls.
/// Separates prompt construction from HTTP communication.
/// </summary>
public sealed class FoundryRequestBuilder
{
    private readonly PromptPack _prompts;

    public FoundryRequestBuilder(PromptPack prompts)
    {
        _prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));
    }

    /// <summary>
    /// Gets the system prompt text from the prompt pack.
    /// </summary>
    public string GetSystemPrompt() => _prompts.Foundry.System;

    /// <summary>
    /// Builds the user prompt text from the appropriate template.
    /// </summary>
    public string BuildUserPromptText(
        string windowTitle,
        PriorityCycler.UiPriority priority,
        string targetLanguage,
        string? userQuestion = null)
    {
        var template = string.IsNullOrWhiteSpace(userQuestion)
            ? _prompts.Foundry.TranslateUser
            : _prompts.Foundry.QuestionUser;

        var values = new Dictionary<string, string?>
        {
            ["windowTitle"] = windowTitle,
            ["priority"] = priority.ToString(),
            ["targetLanguage"] = targetLanguage,
            ["mode"] = "explain", // Legacy placeholder - mode is now always "explain"
            ["userQuestion"] = userQuestion
        };

        return PromptTemplate.Render(template, values);
    }

    /// <summary>
    /// Prepares the screenshot as a base64 data URL for use in API calls.
    /// </summary>
    public static string PrepareScreenshotDataUrl(Image<Rgba32> screenshot, int downscaleWidth = 640)
    {
        if (screenshot is null) throw new ArgumentNullException(nameof(screenshot));

        using var downscaled = ImageDownscaler.DownscaleToWidth(screenshot, downscaleWidth);
        var pngBytes = ImageDownscaler.EncodePng(downscaled);
        return "data:image/png;base64," + Convert.ToBase64String(pngBytes);
    }

    /// <summary>
    /// Converts ChatMessageDto list to the internal message format for API requests.
    /// </summary>
    public static List<FoundryMessage> BuildRequestMessages(IReadOnlyList<ChatMessageDto> messages)
    {
        if (messages is null || messages.Count == 0)
            throw new ArgumentException("Messages cannot be empty.", nameof(messages));

        var result = new List<FoundryMessage>();

        foreach (var msg in messages)
        {
            var parts = new List<FoundryMessagePart>();

            if (!string.IsNullOrEmpty(msg.TextContent))
            {
                parts.Add(new FoundryMessagePart { Type = "text", Text = msg.TextContent });
            }

            if (msg.HasImage)
            {
                parts.Add(new FoundryMessagePart { Type = "image_url", ImageUrl = msg.ImageDataUrl! });
            }

            result.Add(new FoundryMessage
            {
                Role = msg.Role,
                Parts = parts
            });
        }

        return result;
    }

    /// <summary>
    /// Builds messages for a simple translate/cycle request (no conversation history).
    /// </summary>
    public List<FoundryMessage> BuildTranslateMessages(
        string windowTitle,
        PriorityCycler.UiPriority priority,
        string targetLanguage,
        string? userQuestion,
        string imageDataUrl)
    {
        var systemText = GetSystemPrompt();
        var userText = BuildUserPromptText(windowTitle, priority, targetLanguage, userQuestion);

        return new List<FoundryMessage>
        {
            new()
            {
                Role = "system",
                Parts = new List<FoundryMessagePart>
                {
                    new() { Type = "text", Text = systemText }
                }
            },
            new()
            {
                Role = "user",
                Parts = new List<FoundryMessagePart>
                {
                    new() { Type = "text", Text = userText },
                    new() { Type = "image_url", ImageUrl = imageDataUrl }
                }
            }
        };
    }

    /// <summary>
    /// Builds messages for a summarization request.
    /// </summary>
    public static List<FoundryMessage> BuildSummaryMessages(string textToSummarize)
    {
        const string systemPrompt = "You are a helpful assistant that summarizes conversations. Create a brief, factual summary of the key points from the conversation below. Focus on: questions asked, answers given, and any important context about the game or situation. Keep the summary concise (under 300 words).";

        return new List<FoundryMessage>
        {
            new()
            {
                Role = "system",
                Parts = new List<FoundryMessagePart>
                {
                    new() { Type = "text", Text = systemPrompt }
                }
            },
            new()
            {
                Role = "user",
                Parts = new List<FoundryMessagePart>
                {
                    new() { Type = "text", Text = $"Please summarize this conversation:\n\n{textToSummarize}" }
                }
            }
        };
    }
}

/// <summary>
/// Represents a message to be sent to the Foundry/OpenAI API.
/// </summary>
public sealed class FoundryMessage
{
    public string Role { get; set; } = "";
    public List<FoundryMessagePart> Parts { get; set; } = new();
}

/// <summary>
/// Represents a part of a message (text or image).
/// </summary>
public sealed class FoundryMessagePart
{
    public string Type { get; set; } = "";
    public string? Text { get; set; }
    public string? ImageUrl { get; set; }
}
