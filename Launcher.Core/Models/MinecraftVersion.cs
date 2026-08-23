namespace Launcher.Core.Models;

public sealed record MinecraftVersion(
    string Id,
    string Type,
    string Url,
    DateTimeOffset Time,
    DateTimeOffset ReleaseTime)
{
    public bool IsRelease => string.Equals(Type, "release", StringComparison.OrdinalIgnoreCase);
    public bool IsSnapshot => string.Equals(Type, "snapshot", StringComparison.OrdinalIgnoreCase);
    public string DisplayName => IsRelease ? Id : $"{Id} ({Type})";
}
