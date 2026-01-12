using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameScreenTranslator.Core.App.Flows;
using GameScreenTranslator.Core.Models;
using Xunit;

namespace GameScreenTranslator.Tests.Unit;

public sealed class FlowsTests
{
    [Fact]
    public void SpeakTextBuilder_WhenTranslatedTextPresent_ReturnsTranslatedOrTranslatedPlusExplanation()
    {
        var noExplanation = new TranslateCycleResponse
        {
            TranslatedText = "שלום",
            Explanation = null,
            SourceText = "hello",
            SelectedLabel = "x"
        };

        Assert.Equal("שלום", SpeakTextBuilder.Build(noExplanation));

        var withExplanation = new TranslateCycleResponse
        {
            TranslatedText = "שלום",
            Explanation = "This is a greeting",
            SourceText = "hello",
            SelectedLabel = "x"
        };

        Assert.Equal("שלום. This is a greeting", SpeakTextBuilder.Build(withExplanation));
    }

    [Fact]
    public void SpeakTextBuilder_WhenTranslatedTextEmpty_FallsBackToExplanationThenSourceThenDefault()
    {
        var explanationOnly = new TranslateCycleResponse
        {
            TranslatedText = "   ",
            Explanation = "Expl",
            SourceText = "",
            SelectedLabel = "x"
        };
        Assert.Equal("Expl", SpeakTextBuilder.Build(explanationOnly));

        var sourceOnly = new TranslateCycleResponse
        {
            TranslatedText = "",
            Explanation = "   ",
            SourceText = "SOURCE",
            SelectedLabel = "x"
        };
        Assert.Equal("SOURCE", SpeakTextBuilder.Build(sourceOnly));

        var neither = new TranslateCycleResponse
        {
            TranslatedText = "",
            Explanation = null,
            SourceText = "",
            SelectedLabel = "x"
        };
        Assert.False(string.IsNullOrWhiteSpace(SpeakTextBuilder.Build(neither)));
    }

    [Fact]
    public async Task OperationPipeline_RunsStepsInOrder()
    {
        var calls = new List<string>();

        var p = new OperationPipeline<TestContext>(
            new RecordStep("a", calls),
            new RecordStep("b", calls),
            new RecordStep("c", calls));

        await p.RunAsync(new TestContext(), CancellationToken.None);

        Assert.Equal(new[] { "a", "b", "c" }, calls);
    }

    private sealed class TestContext;

    private sealed class RecordStep : IOperationStep<TestContext>
    {
        private readonly string _name;
        private readonly List<string> _calls;

        public RecordStep(string name, List<string> calls)
        {
            _name = name;
            _calls = calls;
        }

        public Task ExecuteAsync(TestContext ctx, CancellationToken cancellationToken)
        {
            _calls.Add(_name);
            return Task.CompletedTask;
        }
    }
}
