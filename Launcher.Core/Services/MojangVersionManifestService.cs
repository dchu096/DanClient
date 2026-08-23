using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class MojangVersionManifestService : IVersionManifestService
{
    private static readonly Uri ManifestUri = new("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly string _cachePath;

    public MojangVersionManifestService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _cachePath = Path.Combine(AppPaths.Cache, "version_manifest_v2.json");
    }

    public async Task<IReadOnlyList<MinecraftVersion>> GetVersionsAsync(
        bool includeSnapshots,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        AppPaths.Ensure();
        var json = await ReadManifestJsonAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<VersionManifestDto>(json, JsonOptions)
                       ?? throw new InvalidDataException("Mojang version manifest could not be parsed.");

        return manifest.Versions
            .Where(v => v.Type == "release" || (includeSnapshots && v.Type == "snapshot"))
            .Select(v => new MinecraftVersion(v.Id, v.Type, v.Url, v.Time, v.ReleaseTime))
            .OrderByDescending(v => v.ReleaseTime)
            .ToArray();
    }

    private async Task<string> ReadManifestJsonAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && File.Exists(_cachePath))
        {
            var age = DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(_cachePath);
            if (age < TimeSpan.FromHours(6))
            {
                return await File.ReadAllTextAsync(_cachePath, cancellationToken).ConfigureAwait(false);
            }
        }

        try
        {
            using var response = await _httpClient.GetAsync(ManifestUri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(_cachePath, json, cancellationToken).ConfigureAwait(false);
            return json;
        }
        catch when (File.Exists(_cachePath))
        {
            return await File.ReadAllTextAsync(_cachePath, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record VersionManifestDto(
        [property: JsonPropertyName("versions")] VersionEntryDto[] Versions);

    private sealed record VersionEntryDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("time")] DateTimeOffset Time,
        [property: JsonPropertyName("releaseTime")] DateTimeOffset ReleaseTime);
}
