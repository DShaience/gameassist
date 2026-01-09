using System;
using Xunit;
using GameScreenTranslator.Core.State;

namespace GameScreenTranslator.Tests.Unit;

public class ScreenStateManagerTests
{
    [Fact]
    public void Current_Throws_WhenNotSet()
    {
        var mgr = new ScreenStateManager();
        Assert.Throws<InvalidOperationException>(() => mgr.GetCycleIndex());
    }

    [Fact]
    public void SetCurrentScreen_CreatesState_AndStartsAtZero()
    {
        var mgr = new ScreenStateManager();

        mgr.SetCurrentScreen(123UL);

        Assert.Equal(123UL, mgr.CurrentSignature);
        Assert.Equal(0, mgr.GetCycleIndex());
        Assert.False(mgr.HasSpoken("x"));
    }

    [Fact]
    public void AdvanceCycle_IncrementsForSameSignature()
    {
        var mgr = new ScreenStateManager();
        mgr.SetCurrentScreen(1UL);

        mgr.AdvanceCycle();
        mgr.AdvanceCycle();

        Assert.Equal(2, mgr.GetCycleIndex());
    }

    [Fact]
    public void ScreenState_IsPerSignature()
    {
        var mgr = new ScreenStateManager();

        mgr.SetCurrentScreen(1UL);
        mgr.AdvanceCycle();
        mgr.MarkSpoken("hello");

        mgr.SetCurrentScreen(2UL);
        Assert.Equal(0, mgr.GetCycleIndex());
        Assert.False(mgr.HasSpoken("hello"));

        mgr.SetCurrentScreen(1UL);
        Assert.Equal(1, mgr.GetCycleIndex());
        Assert.True(mgr.HasSpoken("hello"));
    }

    [Fact]
    public void MarkSpoken_AndHasSpoken_Work()
    {
        var mgr = new ScreenStateManager();
        mgr.SetCurrentScreen(1UL);

        Assert.False(mgr.HasSpoken("k"));
        mgr.MarkSpoken("k");
        Assert.True(mgr.HasSpoken("k"));
    }

    [Fact]
    public void ClearAll_ResetsEverything()
    {
        var mgr = new ScreenStateManager();
        mgr.SetCurrentScreen(1UL);
        mgr.AdvanceCycle();
        mgr.MarkSpoken("k");

        mgr.ClearAll();

        Assert.Null(mgr.CurrentSignature);
        Assert.Throws<InvalidOperationException>(() => mgr.GetCycleIndex());
    }
}