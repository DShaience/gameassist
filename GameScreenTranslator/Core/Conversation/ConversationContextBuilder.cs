using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameScreenTranslator.Core.Configuration;

namespace GameScreenTranslator.Core.Conversation;

/// <summary>
/// Represents a chat message to be sent to the model.
/// </summary>
public sealed class ChatMessageDto
{
    public string Role { get; set; } = "";
    public string TextContent { get; set; } = "";
    public string? ImageDataUrl { get; set; }

    /// <summary>
    /// True if this message should include the image (only for the current user message).
    /// </summary>
    public bool HasImage => !string.IsNullOrEmpty(ImageDataUrl);
}

/// <summary>
/// Configuration for conversation context building.
/// </summary>
public sealed class ConversationContextConfig
{
    /// <summary>
    /// Whether conversation history is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Number of recent turns to keep verbatim (user + assistant pairs).
    /// </summary>
    public int MaxVerbatimTurns { get; set; } = 2;

    /// <summary>
    /// Maximum character length for the rolling summary of older turns.
    /// </summary>
    public int SummaryMaxChars { get; set; } = 1200;
}

/// <summary>
/// Builds conversation context for model calls, applying the rolling summary strategy:
/// - Keep last N turns verbatim
/// - Summarize older turns into a rolling summary
/// </summary>
public sealed class ConversationContextBuilder
{
    private readonly ConversationContextConfig _config;
    private readonly Func<string, CancellationToken, Task<string>>? _summarizer;

    private string? _rollingSummary;

    /// <summary>
    /// Creates a new ConversationContextBuilder.
    /// </summary>
    /// <param name="config">Configuration for context building.</param>
    /// <param name="summarizer">
    /// Optional async function to generate a summary from text.
    /// If null, older turns are truncated instead of summarized.
    /// The function receives the text to summarize and returns the summary.
    /// </param>
    public ConversationContextBuilder(ConversationContextConfig config, Func<string, CancellationToken, Task<string>>? summarizer = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _summarizer = summarizer;
    }

    /// <summary>
    /// Gets the current rolling summary (for logging/debugging).
    /// </summary>
    public string? RollingSummary => _rollingSummary;

    /// <summary>
    /// Builds the message list for a model call, incorporating conversation history.
    /// </summary>
    /// <param name="systemPrompt">The system prompt text.</param>
    /// <param name="currentUserPrompt">The current user prompt (rendered template).</param>
    /// <param name="imageDataUrl">The base64 data URL for the current screenshot.</param>
    /// <param name="conversationStore">The conversation store with history.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of messages to send to the model.</returns>
    public async Task<List<ChatMessageDto>> BuildMessagesAsync(
        string systemPrompt,
        string currentUserPrompt,
        string? imageDataUrl,
        ConversationStore conversationStore,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessageDto>();

        // 1. System message
        messages.Add(new ChatMessageDto
        {
            Role = "system",
            TextContent = systemPrompt
        });

        if (!_config.Enabled || conversationStore.TurnCount == 0)
        {
            // No history - just add current user message with image
            messages.Add(new ChatMessageDto
            {
                Role = "user",
                TextContent = currentUserPrompt,
                ImageDataUrl = imageDataUrl
            });
            return messages;
        }

        // 2. Build conversation context
        var turns = conversationStore.GetTurns();
        var verbatimCount = _config.MaxVerbatimTurns * 2; // Each "turn" is user+assistant pair
        var recentTurns = turns.TakeLast(verbatimCount).ToList();
        var olderTurns = turns.Take(Math.Max(0, turns.Count - verbatimCount)).ToList();

        // 3. Handle older turns (summarize or truncate)
        if (olderTurns.Count > 0)
        {
            await UpdateRollingSummaryAsync(olderTurns, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(_rollingSummary))
            {
                // Add summary as a system-level context note
                messages.Add(new ChatMessageDto
                {
                    Role = "system",
                    TextContent = $"[Previous conversation summary: {_rollingSummary}]"
                });
            }
        }

        // 4. Add recent turns verbatim (text-only, no images from history)
        foreach (var turn in recentTurns)
        {
            messages.Add(new ChatMessageDto
            {
                Role = turn.Role,
                TextContent = turn.Text
            });
        }

        // 5. Add current user message with image
        messages.Add(new ChatMessageDto
        {
            Role = "user",
            TextContent = currentUserPrompt,
            ImageDataUrl = imageDataUrl
        });

        return messages;
    }

    /// <summary>
    /// Builds the message list without async summarization (uses existing summary or truncates).
    /// </summary>
    public List<ChatMessageDto> BuildMessagesSync(
        string systemPrompt,
        string currentUserPrompt,
        string? imageDataUrl,
        ConversationStore conversationStore)
    {
        var messages = new List<ChatMessageDto>();

        // 1. System message
        messages.Add(new ChatMessageDto
        {
            Role = "system",
            TextContent = systemPrompt
        });

        if (!_config.Enabled || conversationStore.TurnCount == 0)
        {
            // No history - just add current user message with image
            messages.Add(new ChatMessageDto
            {
                Role = "user",
                TextContent = currentUserPrompt,
                ImageDataUrl = imageDataUrl
            });
            return messages;
        }

        // 2. Build conversation context
        var turns = conversationStore.GetTurns();
        var verbatimCount = _config.MaxVerbatimTurns * 2;
        var recentTurns = turns.TakeLast(verbatimCount).ToList();
        var olderTurns = turns.Take(Math.Max(0, turns.Count - verbatimCount)).ToList();

        // 3. Handle older turns (use existing summary or create truncated version)
        if (olderTurns.Count > 0)
        {
            var summaryText = _rollingSummary ?? TruncateOlderTurns(olderTurns);

            if (!string.IsNullOrWhiteSpace(summaryText))
            {
                messages.Add(new ChatMessageDto
                {
                    Role = "system",
                    TextContent = $"[Previous conversation summary: {summaryText}]"
                });
            }
        }

        // 4. Add recent turns verbatim
        foreach (var turn in recentTurns)
        {
            messages.Add(new ChatMessageDto
            {
                Role = turn.Role,
                TextContent = turn.Text
            });
        }

        // 5. Add current user message with image
        messages.Add(new ChatMessageDto
        {
            Role = "user",
            TextContent = currentUserPrompt,
            ImageDataUrl = imageDataUrl
        });

        return messages;
    }

    /// <summary>
    /// Resets the rolling summary (call when conversation is reset).
    /// </summary>
    public void ResetSummary()
    {
        _rollingSummary = null;
    }

    private async Task UpdateRollingSummaryAsync(IReadOnlyList<ConversationTurn> olderTurns, CancellationToken cancellationToken)
    {
        if (olderTurns.Count == 0)
            return;

        // Build text from older turns
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(_rollingSummary))
        {
            sb.AppendLine("Previous summary:");
            sb.AppendLine(_rollingSummary);
            sb.AppendLine();
            sb.AppendLine("Additional conversation:");
        }

        foreach (var turn in olderTurns)
        {
            var roleLabel = turn.Role == ConversationTurn.UserRole ? "User" : "Assistant";
            sb.AppendLine($"{roleLabel}: {turn.Text}");
        }

        var textToSummarize = sb.ToString();

        if (_summarizer != null)
        {
            try
            {
                var summary = await _summarizer(textToSummarize, cancellationToken).ConfigureAwait(false);
                _rollingSummary = TruncateToMaxChars(summary, _config.SummaryMaxChars);
            }
            catch (Exception ex)
            {
                // If summarization fails, fall back to truncation
                Console.WriteLine($"[Conversation] Summarization failed, using truncation: {ex.Message}");
                _rollingSummary = TruncateToMaxChars(textToSummarize, _config.SummaryMaxChars);
            }
        }
        else
        {
            // No summarizer - use truncation
            _rollingSummary = TruncateToMaxChars(textToSummarize, _config.SummaryMaxChars);
        }
    }

    private string TruncateOlderTurns(IReadOnlyList<ConversationTurn> olderTurns)
    {
        var sb = new StringBuilder();
        foreach (var turn in olderTurns)
        {
            var roleLabel = turn.Role == ConversationTurn.UserRole ? "User" : "Assistant";
            sb.AppendLine($"{roleLabel}: {turn.Text}");
        }
        return TruncateToMaxChars(sb.ToString(), _config.SummaryMaxChars);
    }

    private static string TruncateToMaxChars(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var trimmed = text.Trim();
        if (trimmed.Length <= maxChars)
            return trimmed;

        // Truncate and add ellipsis
        return trimmed.Substring(0, maxChars - 3) + "...";
    }
}
