namespace GameScreenTranslator.Core.State;

/// <summary>
/// Maps a cycle index (0,1,2,...) to a high-level UI priority.
/// The model prompt will use this to focus attention on different regions.
/// </summary>
public static class PriorityCycler
{
    public enum UiPriority
    {
        Highlighted = 0,
        CentralDialog = 1,
        TooltipOrSidePanel = 2,
        MenuHeaderAndDescription = 3,
        OtherLargeReadableText = 4
    }

    public static UiPriority GetPriorityForCycleIndex(int cycleIndex)
    {
        if (cycleIndex < 0) cycleIndex = 0;
        return (UiPriority)(cycleIndex % 5);
    }
}