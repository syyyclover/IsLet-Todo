using TaskIsland.Application;
using Xunit;

public class ShellRulesTests
{
    [Theory]
    [InlineData(IslandAnchor.Left, -1904)]
    [InlineData(IslandAnchor.Center, -1140)]
    [InlineData(IslandAnchor.Right, -376)]
    public void PlacementHandlesNegativeMonitorOrigin(IslandAnchor anchor, double expectedX)
    {
        var result = IslandPlacement.Calculate(new(-1920, 0, 1920, 1080), 360, 200, anchor);
        Assert.Equal(expectedX, result.X);
        Assert.Equal(8, result.Y);
    }

    [Fact]
    public void OversizedWindowFitsSmallWorkArea()
    {
        var result = IslandPlacement.Calculate(new(0, 50, 300, 180), 400, 600, IslandAnchor.Right);
        Assert.True(result.X >= 0 && result.X + result.Width <= 300);
        Assert.True(result.Y >= 50 && result.Y + result.Height <= 230);
    }

    [Theory]
    [InlineData(IslandPage.Tasks)]
    [InlineData(IslandPage.Settings)]
    [InlineData(IslandPage.Archive)]
    [InlineData(IslandPage.Editor)]
    [InlineData(IslandPage.Attachments)]
    public void EveryExpandedPageCollapsesOnPointerLeave(IslandPage page)
    {
        var state = new IslandState();
        state.Navigate(page);
        state.CollapseAfterPointerLeave();
        Assert.Equal(IslandPage.Collapsed, state.Page);
    }
    [Theory]
    [InlineData("", false)]
    [InlineData("task=garbage", false)]
    [InlineData("task=00000000-0000-0000-0000-000000000000", false)]
    [InlineData("task=bdab384c-0821-4b36-b1ed-543c19e38b77&command=exit", false)]
    [InlineData("task=bdab384c-0821-4b36-b1ed-543c19e38b77", true)]
    public void NotificationArgumentsAreStrictlyValidated(string input, bool valid)
    {
        Assert.Equal(valid, ActivationRequest.Parse(input).TaskId.HasValue);
    }

    [Theory]
    [InlineData(IslandPage.Tasks)]
    [InlineData(IslandPage.Settings)]
    [InlineData(IslandPage.Archive)]
    [InlineData(IslandPage.Editor)]
    [InlineData(IslandPage.Attachments)]
    public void PointerReentryAlwaysOpensTasksAfterLeavingAnyPage(IslandPage page)
    {
        var state = new IslandState();
        state.Navigate(page);
        state.CollapseAfterPointerLeave();
        state.CollapseAfterPointerLeave();
        state.ExpandAfterPointerEnter();
        Assert.Equal(IslandPage.Tasks, state.Page);
        state.ExpandAfterPointerEnter();
        Assert.Equal(IslandPage.Tasks, state.Page);
    }

    [Fact]
    public void InitialHoverAndExplicitCollapseAlwaysOpenTasks()
    {
        var state = new IslandState();
        Assert.Equal(IslandPage.Collapsed, state.Page);
        state.CollapseAfterPointerLeave();
        state.ExpandAfterPointerEnter();
        Assert.Equal(IslandPage.Tasks, state.Page);
        state.Navigate(IslandPage.Editor);
        state.Navigate(IslandPage.Tasks);
        state.Navigate(IslandPage.Collapsed);
        state.ExpandAfterPointerEnter();
        Assert.Equal(IslandPage.Tasks, state.Page);
    }
}
