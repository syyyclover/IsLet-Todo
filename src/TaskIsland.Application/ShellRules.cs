namespace TaskIsland.Application;

public enum IslandAnchor { Left, Center, Right }
public enum IslandPage { Collapsed, Tasks, Settings, Archive }
public readonly record struct WorkArea(double X, double Y, double Width, double Height);
public readonly record struct IslandBounds(double X, double Y, double Width, double Height);
public static class IslandPlacement
{
    public static IslandBounds Calculate(WorkArea area, double width, double height, IslandAnchor anchor)
    {
        width = Math.Clamp(width, 1, Math.Max(1, area.Width));
        height = Math.Clamp(height, 1, Math.Max(1, area.Height - 8));
        var x = anchor switch
        {
            IslandAnchor.Left => area.X + 16,
            IslandAnchor.Right => area.X + area.Width - width - 16,
            _ => area.X + (area.Width - width) / 2
        };
        return new(Math.Clamp(x, area.X, area.X + area.Width - width),
            area.Y + Math.Min(8, Math.Max(0, area.Height - height)), width, height);
    }
}
public sealed class IslandState
{
    public IslandPage Page { get; private set; }
    public void Navigate(IslandPage page) => Page = page;
    public void CollapseAfterPointerLeave()
    {
        if (Page == IslandPage.Tasks) Page = IslandPage.Collapsed;
    }
}
public sealed record ActivationRequest(Guid? TaskId)
{
    public static ActivationRequest Parse(string argument)
    {
        const string prefix = "task=";
        return argument.StartsWith(prefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(argument[prefix.Length..], "D", out var id) && id != Guid.Empty
            ? new(id) : new((Guid?)null);
    }
}
