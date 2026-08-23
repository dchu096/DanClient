using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class MinecraftInstallationService : IMinecraftInstallationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public MinecraftInstallationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MinecraftInstallStatus> GetInstallStatusAsync(
        MinecraftVersion version,
        string minecraftDirectory,
        CancellationToken cancellationToken = default)
    {
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", version.Id);
        var versionJsonPath = Path.Combine(versionDirectory, $"{version.Id}.json");
        if (!File.Exists(versionJsonPath))
        {
            return new MinecraftInstallStatus(
                MinecraftInstallState.Missing,
                "Minecraft files are not installed for this profile.",
                0,
                1);
        }

        try
        {
            var versionJson = await File.ReadAllTextAsync(versionJsonPath, cancellationToken).ConfigureAwait(false);
            var metadata = JsonSerializer.Deserialize<VersionMetadata>(versionJson, JsonOptions)
                           ?? throw new InvalidDataException($"Version metadata for {version.Id} could not be parsed.");

            var checkedFiles = 0;
            var missingFiles = 0;

            if (metadata.Downloads.Client is not null)
            {
                checkedFiles++;
                var clientJarPath = Path.Combine(versionDirectory, $"{version.Id}.jar");
                if (!IsFileComplete(clientJarPath, metadata.Downloads.Client.Size, metadata.Downloads.Client.Sha1))
                {
                    missingFiles++;
                }
            }

            foreach (var library in metadata.Libraries.Where(IsLibraryAllowed))
            {
                if (library.Downloads.Artifact is not null)
                {
                    checkedFiles++;
                    var path = Path.Combine(
                        minecraftDirectory,
                        "libraries",
                        library.Downloads.Artifact.Path.Replace('/', Path.DirectorySeparatorChar));
                    if (!IsFileComplete(path, library.Downloads.Artifact.Size, library.Downloads.Artifact.Sha1))
                    {
                        missingFiles++;
                    }
                }

                var nativeClassifier = ResolveNativeClassifier(library);
                if (nativeClassifier is not null
                    && library.Downloads.Classifiers is { } classifiers
                    && classifiers.TryGetProperty(nativeClassifier, out var nativeElement)
                    && nativeElement.Deserialize<LibraryArtifact>(JsonOptions) is { } nativeArtifact)
                {
                    checkedFiles++;
                    var path = Path.Combine(
                        minecraftDirectory,
                        "libraries",
                        nativeArtifact.Path.Replace('/', Path.DirectorySeparatorChar));
                    if (!IsFileComplete(path, nativeArtifact.Size, nativeArtifact.Sha1))
                    {
                        missingFiles++;
                    }
                }
            }

            if (metadata.AssetIndex is not null)
            {
                checkedFiles++;
                var indexPath = Path.Combine(minecraftDirectory, "assets", "indexes", $"{metadata.AssetIndex.Id}.json");
                if (!File.Exists(indexPath))
                {
                    missingFiles++;
                }
                else
                {
                    var indexJson = await File.ReadAllTextAsync(indexPath, cancellationToken).ConfigureAwait(false);
                    var index = JsonSerializer.Deserialize<AssetIndex>(indexJson, JsonOptions);
                    if (index?.Objects is null)
                    {
                        missingFiles++;
                    }
                    else
                    {
                        foreach (var asset in index.Objects.Values)
                        {
                            checkedFiles++;
                            var prefix = asset.Hash[..2];
                            var target = Path.Combine(minecraftDirectory, "assets", "objects", prefix, asset.Hash);
                            if (!IsFileComplete(target, asset.Size, asset.Hash, verifyHash: false))
                            {
                                missingFiles++;
                            }
                        }
                    }
                }
            }

            return missingFiles == 0
                ? new MinecraftInstallStatus(MinecraftInstallState.Installed, "Minecraft files are installed.", checkedFiles, 0)
                : new MinecraftInstallStatus(MinecraftInstallState.Incomplete, $"{missingFiles} game files need install or repair.", checkedFiles, missingFiles);
        }
        catch
        {
            return new MinecraftInstallStatus(
                MinecraftInstallState.Incomplete,
                "Minecraft files need repair.",
                1,
                1);
        }
    }

    public async Task<MinecraftInstallResult> InstallVersionAsync(
        MinecraftVersion version,
        string minecraftDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(minecraftDirectory);
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", version.Id);
        var librariesDirectory = Path.Combine(minecraftDirectory, "libraries");
        var nativesDirectory = Path.Combine(minecraftDirectory, "natives");
        Directory.CreateDirectory(versionDirectory);
        Directory.CreateDirectory(librariesDirectory);
        Directory.CreateDirectory(nativesDirectory);

        progress?.Report($"Preparing Minecraft {version.Id}.");
        var versionJsonPath = Path.Combine(versionDirectory, $"{version.Id}.json");
        await DownloadFileAsync(new Uri(version.Url), versionJsonPath, progress, cancellationToken).ConfigureAwait(false);
        var versionJson = await File.ReadAllTextAsync(versionJsonPath, cancellationToken).ConfigureAwait(false);
        var metadata = JsonSerializer.Deserialize<VersionMetadata>(versionJson, JsonOptions)
                       ?? throw new InvalidDataException($"Version metadata for {version.Id} could not be parsed.");

        if (metadata.Downloads.Client is not null)
        {
            var clientJarPath = Path.Combine(versionDirectory, $"{version.Id}.jar");
            await DownloadFileAsync(
                new Uri(metadata.Downloads.Client.Url),
                clientJarPath,
                progress,
                cancellationToken,
                metadata.Downloads.Client.Size,
                metadata.Downloads.Client.Sha1).ConfigureAwait(false);
        }

        foreach (var library in metadata.Libraries.Where(IsLibraryAllowed))
        {
            if (library.Downloads.Artifact is not null)
            {
                await DownloadLibraryAsync(librariesDirectory, library.Downloads.Artifact, progress, cancellationToken).ConfigureAwait(false);
            }

            var nativeClassifier = ResolveNativeClassifier(library);
            if (nativeClassifier is not null
                && library.Downloads.Classifiers is { } classifiers
                && classifiers.TryGetProperty(nativeClassifier, out var nativeElement)
                && nativeElement.Deserialize<LibraryArtifact>(JsonOptions) is { } nativeArtifact)
            {
                var nativeJarPath = await DownloadLibraryAsync(librariesDirectory, nativeArtifact, progress, cancellationToken).ConfigureAwait(false);
                ExtractNativeJar(nativeJarPath, nativesDirectory);
            }
        }

        if (metadata.AssetIndex is not null)
        {
            await InstallAssetsAsync(minecraftDirectory, metadata.AssetIndex, progress, cancellationToken).ConfigureAwait(false);
        }

        var clientPath = Path.Combine(versionDirectory, $"{version.Id}.jar");
        return new MinecraftInstallResult(
            version.Id,
            metadata.AssetIndex?.Id ?? version.Id,
            metadata.MainClass ?? "net.minecraft.client.main.Main",
            versionDirectory,
            clientPath);
    }

    private async Task InstallAssetsAsync(
        string minecraftDirectory,
        AssetIndexReference assetIndex,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var indexesDirectory = Path.Combine(minecraftDirectory, "assets", "indexes");
        var objectsDirectory = Path.Combine(minecraftDirectory, "assets", "objects");
        Directory.CreateDirectory(indexesDirectory);
        Directory.CreateDirectory(objectsDirectory);

        var indexPath = Path.Combine(indexesDirectory, $"{assetIndex.Id}.json");
        await DownloadFileAsync(new Uri(assetIndex.Url), indexPath, progress, cancellationToken).ConfigureAwait(false);

        var indexJson = await File.ReadAllTextAsync(indexPath, cancellationToken).ConfigureAwait(false);
        var index = JsonSerializer.Deserialize<AssetIndex>(indexJson, JsonOptions)
                    ?? throw new InvalidDataException($"Asset index {assetIndex.Id} could not be parsed.");

        var count = 0;
        foreach (var asset in index.Objects.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prefix = asset.Hash[..2];
            var target = Path.Combine(objectsDirectory, prefix, asset.Hash);
            if (File.Exists(target) && new FileInfo(target).Length == asset.Size)
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var uri = new Uri($"https://resources.download.minecraft.net/{prefix}/{asset.Hash}");
            await DownloadFileAsync(uri, target, null, cancellationToken, asset.Size, asset.Hash).ConfigureAwait(false);
            count++;
            if (count % 50 == 0)
            {
                progress?.Report($"Downloaded {count} assets.");
            }
        }
    }

    private async Task<string> DownloadLibraryAsync(
        string librariesDirectory,
        LibraryArtifact artifact,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var targetPath = Path.Combine(librariesDirectory, artifact.Path.Replace('/', Path.DirectorySeparatorChar));
        await DownloadFileAsync(
            new Uri(artifact.Url),
            targetPath,
            progress,
            cancellationToken,
            artifact.Size,
            artifact.Sha1).ConfigureAwait(false);
        return targetPath;
    }

    private async Task DownloadFileAsync(
        Uri uri,
        string targetPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        long? expectedSize = null,
        string? expectedSha1 = null)
    {
        await DownloadUtility.DownloadFileAsync(
            _httpClient,
            uri,
            targetPath,
            progress,
            expectedSize,
            expectedSha1,
            cancellationToken).ConfigureAwait(false);
    }

    private static bool IsLibraryAllowed(LibraryDefinition library)
    {
        if (library.Rules is null || library.Rules.Length == 0)
        {
            return true;
        }

        var allowed = false;
        foreach (var rule in library.Rules)
        {
            var matchesOs = rule.Os is null || string.Equals(rule.Os.Name, CurrentMojangOsName(), StringComparison.OrdinalIgnoreCase);
            if (matchesOs)
            {
                allowed = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
            }
        }

        return allowed;
    }

    private static string? ResolveNativeClassifier(LibraryDefinition library)
    {
        if (library.Natives is null)
        {
            return null;
        }

        var osName = CurrentMojangOsName();
        var natives = library.Natives.Value;
        if (!natives.TryGetProperty(osName, out var classifierElement))
        {
            return null;
        }

        var classifier = classifierElement.GetString();
        return classifier?.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32", StringComparison.OrdinalIgnoreCase);
    }

    private static string CurrentMojangOsName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "osx";
        }

        return "linux";
    }

    private static void ExtractNativeJar(string jarPath, string nativesDirectory)
    {
        using var archive = ZipFile.OpenRead(jarPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name)
                || entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var targetPath = Path.Combine(nativesDirectory, entry.Name);
            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private static bool IsFileComplete(
        string path,
        long? expectedSize,
        string? expectedSha1,
        bool verifyHash = true)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (expectedSize is not null && new FileInfo(path).Length != expectedSize.Value)
        {
            return false;
        }

        return !verifyHash
               || string.IsNullOrWhiteSpace(expectedSha1)
               || string.Equals(ComputeSha1(path), expectedSha1, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha1(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
    }

    private sealed record VersionMetadata(
        [property: JsonPropertyName("mainClass")] string? MainClass,
        [property: JsonPropertyName("downloads")] VersionDownloads Downloads,
        [property: JsonPropertyName("libraries")] LibraryDefinition[] Libraries,
        [property: JsonPropertyName("assetIndex")] AssetIndexReference? AssetIndex);

    private sealed record VersionDownloads(
        [property: JsonPropertyName("client")] FileDownload? Client);

    private sealed record FileDownload(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("sha1")] string? Sha1,
        [property: JsonPropertyName("size")] long? Size);

    private sealed record LibraryDefinition(
        [property: JsonPropertyName("downloads")] LibraryDownloads Downloads,
        [property: JsonPropertyName("rules")] LibraryRule[]? Rules,
        [property: JsonPropertyName("natives")] JsonElement? Natives);

    private sealed record LibraryDownloads(
        [property: JsonPropertyName("artifact")] LibraryArtifact? Artifact,
        [property: JsonPropertyName("classifiers")] JsonElement? Classifiers);

    private sealed record LibraryArtifact(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("sha1")] string? Sha1);

    private sealed record LibraryRule(
        [property: JsonPropertyName("action")] string Action,
        [property: JsonPropertyName("os")] LibraryRuleOs? Os);

    private sealed record LibraryRuleOs(
        [property: JsonPropertyName("name")] string? Name);

    private sealed record AssetIndexReference(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("url")] string Url);

    private sealed record AssetIndex(
        [property: JsonPropertyName("objects")] Dictionary<string, AssetObject> Objects);

    private sealed record AssetObject(
        [property: JsonPropertyName("hash")] string Hash,
        [property: JsonPropertyName("size")] long Size);
}
