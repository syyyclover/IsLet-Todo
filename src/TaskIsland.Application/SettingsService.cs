namespace TaskIsland.Application;

public sealed record IslandSettings(IslandAnchor Anchor, bool AnimationsEnabled);
public sealed class SettingsService(ITaskStore store)
{
    public Task<IslandSettings> LoadAsync() => store.ReadAsync(tx => new IslandSettings(
        Enum.TryParse<IslandAnchor>(tx.ReadSetting("IslandAnchor"), out var anchor) && Enum.IsDefined(anchor) ? anchor : IslandAnchor.Center,
        !bool.TryParse(tx.ReadSetting("AnimationsEnabled"), out var enabled) || enabled));
    public Task SaveAsync(IslandSettings settings) => store.WriteAsync(tx =>
    {
        tx.WriteSetting("IslandAnchor", settings.Anchor.ToString());
        tx.WriteSetting("AnimationsEnabled", settings.AnimationsEnabled.ToString()); return 0;
    });
}
