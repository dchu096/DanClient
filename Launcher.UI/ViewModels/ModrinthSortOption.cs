namespace Launcher.UI.ViewModels;

public sealed record ModrinthSortOption(string Label, string Index)
{
    public override string ToString() => Label;
}
