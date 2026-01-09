using Xunit;
using GameScreenTranslator.Core.State;

namespace GameScreenTranslator.Tests.Unit;

public class PriorityCyclerTests
{
    [Theory]
    [InlineData(0, PriorityCycler.UiPriority.Highlighted)]
    [InlineData(1, PriorityCycler.UiPriority.CentralDialog)]
    [InlineData(2, PriorityCycler.UiPriority.TooltipOrSidePanel)]
    [InlineData(3, PriorityCycler.UiPriority.MenuHeaderAndDescription)]
    [InlineData(4, PriorityCycler.UiPriority.OtherLargeReadableText)]
    [InlineData(5, PriorityCycler.UiPriority.Highlighted)]
    [InlineData(6, PriorityCycler.UiPriority.CentralDialog)]
    public void GetPriorityForCycleIndex_WrapsEvery5(int cycleIndex, PriorityCycler.UiPriority expected)
    {
        Assert.Equal(expected, PriorityCycler.GetPriorityForCycleIndex(cycleIndex));
    }

    [Fact]
    public void GetPriorityForCycleIndex_Negative_TreatedAsZero()
    {
        Assert.Equal(PriorityCycler.UiPriority.Highlighted, PriorityCycler.GetPriorityForCycleIndex(-1));
    }
}