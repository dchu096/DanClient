namespace Launcher.Core.Models;

public sealed record MinecraftInstallResult(
    string VersionId,
    string AssetIndexId,
    string MainClass,
    string VersionDirectory,
    string ClientJarPath);
