namespace Launcher.Core.Models;

public sealed record LauncherProfile(
    string Id,
    string Name,
    string? MinecraftVersionId,
    bool IncludeSnapshots,
    bool InstallPerformanceMods,
    int MaxMemoryMegabytes,
    string JavaExecutable,
    string ExtraJvmArgs,
    string GameDirectory)
{
    public string ModsDirectory => Path.Combine(GameDirectory, "mods");
}
