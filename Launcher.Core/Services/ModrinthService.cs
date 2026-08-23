using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class ModrinthService : IModrinthService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public ModrinthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DanClient/0.1 modrinth");
        }
    }

    public async Task<IReadOnlyList<ModrinthProject>> SearchProjectsAsync(
        string query,
        string minecraftVersion,
        string loader,
        CancellationToken cancellationToken = default)
    {
        var result = await BrowseProjectsAsync(query, minecraftVersion, loader, "relevance", 0, 20, cancellationToken)
            .ConfigureAwait(false);
        return result.Projects;
    }

    public async Task<ModrinthBrowseResult> BrowseProjectsAsync(
        string? query,
        string minecraftVersion,
        string loader,
        string sortIndex = "downloads",
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
        {
            return new ModrinthBrowseResult([], 0, offset, limit);
        }

        var normalizedSort = string.IsNullOrWhiteSpace(sortIndex) ? "downloads" : sortIndex;
        var facets = WebUtility.UrlEncode(
            $"[[\"project_type:mod\"],[\"versions:{minecraftVersion}\"],[\"categories:{loader}\"]]");
        var uri = "https://api.modrinth.com/v2/search"
                  + $"?query={WebUtility.UrlEncode(query ?? string.Empty)}"
                  + $"&facets={facets}"
                  + $"&index={WebUtility.UrlEncode(normalizedSort)}"
                  + $"&offset={Math.Max(0, offset)}"
                  + $"&limit={Math.Clamp(limit, 1, 100)}";

        using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var search = await JsonSerializer.DeserializeAsync<SearchResponse>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        var projects = search?.Hits.Select(hit => new ModrinthProject(
            hit.ProjectId,
            hit.Slug,
            hit.Title,
            hit.Description,
            hit.Author,
            hit.ProjectType,
            hit.Downloads,
            hit.IconUrl)).ToArray() ?? [];

        return new ModrinthBrowseResult(
            projects,
            search?.TotalHits ?? projects.Length,
            search?.Offset ?? offset,
            search?.Limit ?? limit);
    }

    public async Task<IReadOnlyList<ModrinthProjectVersion>> GetProjectVersionsAsync(
        string projectIdOrSlug,
        string minecraftVersion,
        string loader,
        ModrinthVersionFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var versions = await FetchMatchingVersionsAsync(projectIdOrSlug, minecraftVersion, loader, cancellationToken)
            .ConfigureAwait(false);
        var activeFilter = filter ?? ModrinthVersionFilter.None;
        return versions
            .Where(version => activeFilter.Includes(version.VersionType))
            .Select(version => new ModrinthProjectVersion(
                version.Id,
                version.Name,
                version.VersionNumber,
                version.VersionType ?? "release"))
            .ToArray();
    }

    public async Task<ModrinthInstallResult> InstallProjectAsync(
        ModrinthProject project,
        string minecraftVersion,
        string loader,
        string modsDirectory,
        string? versionId = null,
        ModrinthVersionFilter? filter = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(modsDirectory);
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return await InstallProjectCoreAsync(
            project.Slug,
            minecraftVersion,
            loader,
            modsDirectory,
            installed,
            versionId,
            filter,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ModrinthInstallResult> InstallProjectCoreAsync(
        string projectIdOrSlug,
        string minecraftVersion,
        string loader,
        string modsDirectory,
        HashSet<string> installed,
        string? versionId,
        ModrinthVersionFilter? filter,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!installed.Add(projectIdOrSlug))
        {
            return new ModrinthInstallResult(projectIdOrSlug, "Already installed", string.Empty, modsDirectory, 0);
        }

        progress?.Report($"Resolving {projectIdOrSlug} on Modrinth.");
        var version = await ResolveVersionAsync(projectIdOrSlug, minecraftVersion, loader, versionId, filter, cancellationToken)
            .ConfigureAwait(false);

        foreach (var dependency in version.Dependencies.Where(d => d.DependencyType == "required"))
        {
            if (!string.IsNullOrWhiteSpace(dependency.ProjectId))
            {
                await InstallProjectCoreAsync(
                    dependency.ProjectId,
                    minecraftVersion,
                    loader,
                    modsDirectory,
                    installed,
                    dependency.VersionId,
                    filter,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault()
                   ?? throw new InvalidOperationException($"{projectIdOrSlug} returned no downloadable files.");
        var targetPath = Path.Combine(modsDirectory, file.Filename);

        await DownloadUtility.DownloadFileAsync(
            _httpClient,
            new Uri(file.Url),
            targetPath,
            progress,
            file.Size,
            file.Hashes.TryGetValue("sha1", out var sha1) ? sha1 : null,
            cancellationToken).ConfigureAwait(false);

        return new ModrinthInstallResult(projectIdOrSlug, version.Name, file.Filename, targetPath, file.Size);
    }

    private async Task<ModrinthVersion> ResolveVersionAsync(
        string projectIdOrSlug,
        string minecraftVersion,
        string loader,
        string? versionId,
        ModrinthVersionFilter? filter,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(versionId))
        {
            var uri = $"https://api.modrinth.com/v2/version/{WebUtility.UrlEncode(versionId)}";
            using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<ModrinthVersion>(stream, JsonOptions, cancellationToken)
                       .ConfigureAwait(false)
                   ?? throw new InvalidOperationException($"Modrinth version {versionId} could not be loaded.");
        }

        var versions = await FetchMatchingVersionsAsync(projectIdOrSlug, minecraftVersion, loader, cancellationToken)
            .ConfigureAwait(false);
        var activeFilter = filter ?? ModrinthVersionFilter.None;
        return versions.FirstOrDefault(version => activeFilter.Includes(version.VersionType))
               ?? throw new InvalidOperationException($"No Modrinth version for {projectIdOrSlug} matched Minecraft {minecraftVersion}.");
    }

    private async Task<ModrinthVersion[]> FetchMatchingVersionsAsync(
        string projectIdOrSlug,
        string minecraftVersion,
        string loader,
        CancellationToken cancellationToken)
    {
        var loaders = WebUtility.UrlEncode($"[\"{loader}\"]");
        var versions = WebUtility.UrlEncode($"[\"{minecraftVersion}\"]");
        var uri = $"https://api.modrinth.com/v2/project/{projectIdOrSlug}/version?loaders={loaders}&game_versions={versions}";
        using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<ModrinthVersion[]>(stream, JsonOptions, cancellationToken)
                   .ConfigureAwait(false)
               ?? [];
    }

    private sealed record SearchResponse(
        [property: JsonPropertyName("hits")] ModrinthSearchHit[] Hits,
        [property: JsonPropertyName("total_hits")] int TotalHits,
        [property: JsonPropertyName("offset")] int Offset,
        [property: JsonPropertyName("limit")] int Limit);

    private sealed record ModrinthSearchHit(
        [property: JsonPropertyName("project_id")] string ProjectId,
        [property: JsonPropertyName("slug")] string Slug,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("author")] string Author,
        [property: JsonPropertyName("project_type")] string ProjectType,
        [property: JsonPropertyName("downloads")] int Downloads,
        [property: JsonPropertyName("icon_url")] string? IconUrl);

    private sealed record ModrinthVersion(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("version_number")] string VersionNumber,
        [property: JsonPropertyName("version_type")] string? VersionType,
        [property: JsonPropertyName("files")] ModrinthFile[] Files,
        [property: JsonPropertyName("dependencies")] ModrinthDependency[] Dependencies);

    private sealed record ModrinthFile(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("filename")] string Filename,
        [property: JsonPropertyName("primary")] bool Primary,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("hashes")] Dictionary<string, string> Hashes);

    private sealed record ModrinthDependency(
        [property: JsonPropertyName("project_id")] string? ProjectId,
        [property: JsonPropertyName("version_id")] string? VersionId,
        [property: JsonPropertyName("dependency_type")] string DependencyType);
}
