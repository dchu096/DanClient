using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class FabricInstallerService : IFabricInstallerService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public FabricInstallerService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DanClient/0.1 native-launcher");
        }
    }

    public async Task<FabricLoaderResolution> ResolveLoaderAsync(
        string minecraftVersion,
        CancellationToken cancellationToken = default)
    {
        var uri = $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(minecraftVersion)}";
        using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var entries = await JsonSerializer.DeserializeAsync<FabricLoaderEntry[]>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        var latest = entries?.FirstOrDefault()
                     ?? throw new InvalidOperationException($"No Fabric loader was found for Minecraft {minecraftVersion}.");

        return new FabricLoaderResolution(
            latest.Loader.Version,
            latest.Intermediary.Version,
            new Uri($"https://meta.fabricmc.net/v2/versions/loader/{minecraftVersion}/{latest.Loader.Version}/profile/json"));
    }

    public async Task<IReadOnlyList<ModrinthDownload>> InstallPerformanceModsAsync(
        string minecraftVersion,
        string modsDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(modsDirectory);
        var projects = new[] { "sodium", "fabric-api" };
        var installed = new List<ModrinthDownload>();

        foreach (var project in projects)
        {
            progress?.Report($"Resolving {project} for {minecraftVersion}.");
            var download = await ResolveModrinthDownloadAsync(project, minecraftVersion, cancellationToken).ConfigureAwait(false);
            var targetPath = Path.Combine(modsDirectory, download.FileName);

            await DownloadUtility.DownloadFileAsync(
                _httpClient,
                download.DownloadUri,
                targetPath,
                progress,
                download.Size,
                null,
                cancellationToken).ConfigureAwait(false);

            installed.Add(download);
        }

        return installed;
    }

    public bool IsLoaderInstalled(string minecraftDirectory)
    {
        if (string.IsNullOrWhiteSpace(minecraftDirectory))
        {
            return false;
        }

        var loaderRoot = Path.Combine(minecraftDirectory, "libraries", "net", "fabricmc", "fabric-loader");
        return Directory.Exists(loaderRoot)
               && Directory.EnumerateFiles(loaderRoot, "*.jar", SearchOption.AllDirectories).Any();
    }

    public async Task InstallLoaderLibrariesAsync(
        string minecraftVersion,
        string minecraftDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var resolution = await ResolveLoaderAsync(minecraftVersion, cancellationToken).ConfigureAwait(false);
        progress?.Report($"Installing Fabric Loader {resolution.LoaderVersion}.");
        using var response = await _httpClient.GetAsync(resolution.ProfileJsonUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var profile = await JsonSerializer.DeserializeAsync<FabricProfile>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidDataException("Fabric profile JSON could not be parsed.");

        var librariesDirectory = Path.Combine(minecraftDirectory, "libraries");
        Directory.CreateDirectory(librariesDirectory);

        foreach (var library in profile.Libraries)
        {
            var relativePath = MavenToPath(library.Name);
            var targetPath = Path.Combine(librariesDirectory, relativePath);
            if (File.Exists(targetPath))
            {
                continue;
            }

            var baseUrl = string.IsNullOrWhiteSpace(library.Url)
                ? "https://maven.fabricmc.net/"
                : library.Url;
            var downloadUri = new Uri(new Uri(baseUrl), relativePath.Replace('\\', '/'));
            await DownloadUtility.DownloadFileAsync(
                _httpClient,
                downloadUri,
                targetPath,
                progress,
                null,
                null,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ModrinthDownload> ResolveModrinthDownloadAsync(
        string projectSlug,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        var loaders = WebUtility.UrlEncode("[\"fabric\"]");
        var versions = WebUtility.UrlEncode($"[\"{minecraftVersion}\"]");
        var uri = $"https://api.modrinth.com/v2/project/{projectSlug}/version?loaders={loaders}&game_versions={versions}";
        using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var versionsResponse = await JsonSerializer.DeserializeAsync<ModrinthVersion[]>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        var version = versionsResponse?.FirstOrDefault()
                      ?? throw new InvalidOperationException($"No Modrinth version for {projectSlug} matched Minecraft {minecraftVersion}.");
        var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault()
                   ?? throw new InvalidOperationException($"{projectSlug} returned no downloadable files.");

        return new ModrinthDownload(projectSlug, version.Name, file.Filename, new Uri(file.Url), file.Size);
    }

    private static string MavenToPath(string name)
    {
        var parts = name.Split(':');
        if (parts.Length < 3)
        {
            throw new FormatException($"Invalid Maven coordinate: {name}");
        }

        var group = parts[0].Replace('.', Path.DirectorySeparatorChar);
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : string.Empty;
        var fileName = $"{artifact}-{version}{classifier}.jar";
        return Path.Combine(group, artifact, version, fileName);
    }

    private sealed record FabricLoaderEntry(
        [property: JsonPropertyName("loader")] FabricVersion Loader,
        [property: JsonPropertyName("intermediary")] FabricVersion Intermediary);

    private sealed record FabricVersion(
        [property: JsonPropertyName("version")] string Version);

    private sealed record ModrinthVersion(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("files")] ModrinthFile[] Files);

    private sealed record ModrinthFile(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("filename")] string Filename,
        [property: JsonPropertyName("primary")] bool Primary,
        [property: JsonPropertyName("size")] long Size);

    private sealed record FabricProfile(
        [property: JsonPropertyName("libraries")] FabricProfileLibrary[] Libraries);

    private sealed record FabricProfileLibrary(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("url")] string? Url);
}
