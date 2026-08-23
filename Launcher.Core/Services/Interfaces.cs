using Launcher.Core.Models;

namespace Launcher.Core.Services;

public interface IAuthenticationService
{
    Task<MinecraftAccount> SignInWithDeviceCodeAsync(
        Func<DeviceCodeInfo, Task> showDeviceCode,
        IProgress<AuthProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IAccountSessionService
{
    Task<MinecraftAccount?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(MinecraftAccount account, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public interface ILauncherProfileService
{
    Task<IReadOnlyList<LauncherProfile>> LoadProfilesAsync(CancellationToken cancellationToken = default);
    Task<LauncherProfile> GetSelectedProfileAsync(CancellationToken cancellationToken = default);
    Task SaveProfileAsync(LauncherProfile profile, bool select, CancellationToken cancellationToken = default);
    Task SelectProfileAsync(string profileId, CancellationToken cancellationToken = default);
    Task<LauncherProfile> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default);
}

public interface IVersionManifestService
{
    Task<IReadOnlyList<MinecraftVersion>> GetVersionsAsync(
        bool includeSnapshots,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}

public interface IModFileService : IDisposable
{
    event EventHandler? ModsChanged;
    string ModsDirectory { get; }
    void SetModsDirectory(string modsDirectory);
    Task<IReadOnlyList<ModInfo>> LoadModsAsync(CancellationToken cancellationToken = default);
    Task<ModInfo> SetEnabledAsync(ModInfo mod, bool enabled, CancellationToken cancellationToken = default);
    Task DeleteModAsync(ModInfo mod, CancellationToken cancellationToken = default);
    void OpenModsFolder();
}

public interface IFabricInstallerService
{
    Task<FabricLoaderResolution> ResolveLoaderAsync(
        string minecraftVersion,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModrinthDownload>> InstallPerformanceModsAsync(
        string minecraftVersion,
        string modsDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    Task InstallLoaderLibrariesAsync(
        string minecraftVersion,
        string minecraftDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    bool IsLoaderInstalled(string minecraftDirectory);
}

public interface IModrinthService
{
    Task<ModrinthBrowseResult> BrowseProjectsAsync(
        string? query,
        string minecraftVersion,
        string loader,
        string sortIndex = "downloads",
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModrinthProject>> SearchProjectsAsync(
        string query,
        string minecraftVersion,
        string loader,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModrinthProjectVersion>> GetProjectVersionsAsync(
        string projectIdOrSlug,
        string minecraftVersion,
        string loader,
        ModrinthVersionFilter? filter = null,
        CancellationToken cancellationToken = default);

    Task<ModrinthInstallResult> InstallProjectAsync(
        ModrinthProject project,
        string minecraftVersion,
        string loader,
        string modsDirectory,
        string? versionId = null,
        ModrinthVersionFilter? filter = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IDiscordPresenceService : IDisposable
{
    bool IsEnabled { get; set; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SetLauncherIdleAsync(string accountName, string profileName, CancellationToken cancellationToken = default);
    Task SetPlayingAsync(
        string accountName,
        string profileName,
        string minecraftVersion,
        int processId,
        CancellationToken cancellationToken = default);
    void SetApplicationId(string? applicationId);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public interface IMinecraftInstallationService
{
    Task<MinecraftInstallStatus> GetInstallStatusAsync(
        MinecraftVersion version,
        string minecraftDirectory,
        CancellationToken cancellationToken = default);

    Task<MinecraftInstallResult> InstallVersionAsync(
        MinecraftVersion version,
        string minecraftDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IJavaLauncherService
{
    Task<GameProcessHandle> LaunchAsync(
        GameLaunchOptions options,
        CancellationToken cancellationToken = default);
}

public interface IJavaRuntimeService
{
    int GetRequiredJavaFeatureVersion(string? minecraftVersionId, string? minecraftDirectory = null);
    IReadOnlyList<int> GetInstalledJavaVersions();
    string GetBundledJavaExecutable(int featureVersion);
    bool IsJavaExecutableAvailable(string javaExecutable);
    Task<string> EnsureJavaRuntimeAsync(
        int featureVersion,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
    Task EnsureAllSupportedRuntimesAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
