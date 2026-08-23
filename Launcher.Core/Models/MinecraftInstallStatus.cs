namespace Launcher.Core.Models;

public enum MinecraftInstallState
{
    Missing,
    Incomplete,
    Installed
}

public sealed record MinecraftInstallStatus(
    MinecraftInstallState State,
    string Message,
    int CheckedFiles,
    int MissingFiles)
{
    public bool IsInstalled => State == MinecraftInstallState.Installed;
}
