namespace Launcher.Core.Models;

public sealed record GameLaunchOptions(
    string MinecraftDirectory,
    string JavaExecutable,
    string VersionId,
    string AssetIndexId,
    string MainClass,
    string PlayerName,
    string AccessToken,
    string Uuid,
    int MaxMemoryMegabytes,
    IReadOnlyList<string> ExtraJvmArgs);

public sealed record GameProcessHandle(
    int ProcessId,
    DateTimeOffset StartedAt,
    string MinecraftDirectory,
    string? LauncherLogPath);
