using System;
using System.Collections.Generic;

namespace GameScreenTranslator.Core.Conversation;

/// <summary>
/// Represents a single turn in the conversation (user or assistant).
/// </summary>
public sealed record ConversationTurn(string Role, string Text, DateTime TimestampUtc)
{
    public const string UserRole = "user";
    public const string AssistantRole = "assistant";
}

/// <summary>
/// Thread-safe in-memory store for conversation history.
/// Maintains a global conversation context that persists across screens/windows.
/// </summary>
public sealed class ConversationStore
{
    private readonly object _lock = new();
    private readonly List<ConversationTurn> _turns = new();

    /// <summary>
    /// Appends a user message to the conversation.
    /// </summary>
    public void AppendUser(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        lock (_lock)
        {
            _turns.Add(new ConversationTurn(ConversationTurn.UserRole, text.Trim(), DateTime.UtcNow));
        }
    }

    /// <summary>
    /// Appends an assistant message to the conversation.
    /// </summary>
    public void AppendAssistant(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        lock (_lock)
        {
            _turns.Add(new ConversationTurn(ConversationTurn.AssistantRole, text.Trim(), DateTime.UtcNow));
        }
    }

    /// <summary>
    /// Returns a snapshot of all conversation turns.
    /// </summary>
    public IReadOnlyList<ConversationTurn> GetTurns()
    {
        lock (_lock)
        {
            return _turns.ToArray();
        }
    }

    /// <summary>
    /// Returns the number of turns in the conversation.
    /// </summary>
    public int TurnCount
    {
        get
        {
            lock (_lock)
            {
                return _turns.Count;
            }
        }
    }

    /// <summary>
    /// Clears all conversation history.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _turns.Clear();
        }
    }

    /// <summary>
    /// Returns the timestamp of the last turn, or null if no turns exist.
    /// </summary>
    public DateTime? LastTurnTimestampUtc
    {
        get
        {
            lock (_lock)
            {
                return _turns.Count > 0 ? _turns[^1].TimestampUtc : null;
            }
        }
    }
}
