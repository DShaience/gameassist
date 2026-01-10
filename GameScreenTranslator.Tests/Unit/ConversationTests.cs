using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameScreenTranslator.Core.Conversation;
using Xunit;

namespace GameScreenTranslator.Tests.Unit;

public sealed class ConversationTests
{
    [Fact]
    public void ConversationStore_AppendsAndResets()
    {
        var store = new ConversationStore();

        Assert.Equal(0, store.TurnCount);
        Assert.Null(store.LastTurnTimestampUtc);

        store.AppendUser(" where is the key? ");
        store.AppendAssistant("The key is on the table.");

        Assert.Equal(2, store.TurnCount);

        var turns = store.GetTurns();
        Assert.Equal(2, turns.Count);

        Assert.Equal(ConversationTurn.UserRole, turns[0].Role);
        Assert.Equal("where is the key?", turns[0].Text);

        Assert.Equal(ConversationTurn.AssistantRole, turns[1].Role);
        Assert.Equal("The key is on the table.", turns[1].Text);

        Assert.NotNull(store.LastTurnTimestampUtc);

        store.Reset();
        Assert.Equal(0, store.TurnCount);
        Assert.Empty(store.GetTurns());
        Assert.Null(store.LastTurnTimestampUtc);
    }

    [Fact]
    public void ConversationContextBuilder_WhenDisabled_DoesNotIncludeHistory()
    {
        var store = new ConversationStore();
        store.AppendUser("Q1");
        store.AppendAssistant("A1");

        var builder = new ConversationContextBuilder(new ConversationContextConfig
        {
            Enabled = false,
            MaxVerbatimTurns = 2,
            SummaryMaxChars = 200
        });

        var msgs = builder.BuildMessagesSync(
            systemPrompt: "SYS",
            currentUserPrompt: "CURRENT",
            imageDataUrl: "data:image/png;base64,abc",
            conversationStore: store);

        Assert.Equal(2, msgs.Count);
        Assert.Equal("system", msgs[0].Role);
        Assert.Equal("SYS", msgs[0].TextContent);

        Assert.Equal("user", msgs[1].Role);
        Assert.Equal("CURRENT", msgs[1].TextContent);
        Assert.True(msgs[1].HasImage);
    }

    [Fact]
    public void ConversationContextBuilder_IncludesRecentTurnsAndCurrentImage()
    {
        var store = new ConversationStore();
        store.AppendUser("U1");
        store.AppendAssistant("A1");
        store.AppendUser("U2");
        store.AppendAssistant("A2");
        store.AppendUser("U3");
        store.AppendAssistant("A3");

        var builder = new ConversationContextBuilder(new ConversationContextConfig
        {
            Enabled = true,
            MaxVerbatimTurns = 2, // keep last 4 turns verbatim
            SummaryMaxChars = 200
        });

        var msgs = builder.BuildMessagesSync(
            systemPrompt: "SYS",
            currentUserPrompt: "CURRENT",
            imageDataUrl: "data:image/png;base64,abc",
            conversationStore: store);

        // Expected:
        // system
        // summary (since older turns exist)
        // recent 4 turns: U2,A2,U3,A3
        // current user + image
        Assert.Equal(1 + 1 + 4 + 1, msgs.Count);

        Assert.Equal("system", msgs[0].Role);
        Assert.Equal("SYS", msgs[0].TextContent);

        Assert.Equal("system", msgs[1].Role);
        Assert.Contains("Previous conversation summary:", msgs[1].TextContent);

        var recent = msgs.Skip(2).Take(4).ToList();
        Assert.Equal(new[] { "user", "assistant", "user", "assistant" }, recent.Select(m => m.Role).ToArray());
        Assert.Equal(new[] { "U2", "A2", "U3", "A3" }, recent.Select(m => m.TextContent).ToArray());

        var current = msgs.Last();
        Assert.Equal("user", current.Role);
        Assert.Equal("CURRENT", current.TextContent);
        Assert.True(current.HasImage);
    }

    [Fact]
    public async Task ConversationContextBuilder_AsyncSummarizer_UpdatesRollingSummary()
    {
        var store = new ConversationStore();
        store.AppendUser("U1");
        store.AppendAssistant("A1");
        store.AppendUser("U2");
        store.AppendAssistant("A2");
        store.AppendUser("U3");
        store.AppendAssistant("A3");

        var builder = new ConversationContextBuilder(
            new ConversationContextConfig { Enabled = true, MaxVerbatimTurns = 2, SummaryMaxChars = 200 },
            summarizer: (text, ct) => Task.FromResult("SUMMARY_OK"));

        var msgs = await builder.BuildMessagesAsync(
            systemPrompt: "SYS",
            currentUserPrompt: "CURRENT",
            imageDataUrl: "data:image/png;base64,abc",
            conversationStore: store,
            cancellationToken: CancellationToken.None);

        Assert.Equal("SUMMARY_OK", builder.RollingSummary);
        Assert.Contains("SUMMARY_OK", msgs[1].TextContent);
    }
}
