namespace Launcher.Core.Models;

public sealed record ModInfo(
    string Id,
    string Name,
    string Version,
    string FilePath,
    string? IconPath,
    bool IsEnabled,
    string Source)
{
    public string FileName => Path.GetFileName(FilePath);
}
