using CommunityToolkit.Mvvm.ComponentModel;
using TaskIsland.Application;

namespace TaskIsland.Presentation;

public partial class IslandViewModel : ObservableObject
{
    [ObservableProperty] public partial string Status { get; set; } = "系统骨架验证中 · 任务功能尚未接入";
    [ObservableProperty] public partial IslandAnchor Anchor { get; set; } = IslandAnchor.Center;
    [ObservableProperty] public partial bool AnimationsEnabled { get; set; } = true;
}
