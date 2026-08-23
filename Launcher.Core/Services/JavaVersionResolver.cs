using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Launcher.Core.Services;

public static partial class JavaVersionResolver
{
    private static readonly int[] SupportedFeatureVersions = [8, 17, 21, 25];

    public static IReadOnlyList<int> SupportedVersions => SupportedFeatureVersions;

    /// <summary>
    /// Resolves the bundled Java feature version for a Minecraft release.
    /// Uses Mojang's version metadata when available, otherwise falls back to version-id rules.
    /// </summary>
    public static int GetRequiredJavaFeatureVersion(string? minecraftVersionId, string? minecraftDirectory = null)
    {
        var fromMetadata = TryReadJavaMajorVersionFromMetadata(minecraftVersionId, minecraftDirectory);
        if (fromMetadata is int metadataVersion)
        {
            return MapToBundledVersion(metadataVersion);
        }

        return MapVersionIdToJava(minecraftVersionId);
    }

    public static int? TryReadJavaMajorVersionFromMetadata(string? minecraftVersionId, string? minecraftDirectory)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersionId) || string.IsNullOrWhiteSpace(minecraftDirectory))
        {
            return null;
        }

        var versionJsonPath = Path.Combine(minecraftDirectory, "versions", minecraftVersionId, $"{minecraftVersionId}.json");
        if (!File.Exists(versionJsonPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(versionJsonPath);
            var metadata = JsonSerializer.Deserialize<VersionJavaMetadata>(json);
            return metadata?.JavaVersion?.MajorVersion;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Maps a Minecraft version id to Java when metadata is not installed yet.
    /// </summary>
    public static int MapVersionIdToJava(string? minecraftVersionId)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersionId))
        {
            return 21;
        }

        var snapshotMatch = SnapshotVersionRegex().Match(minecraftVersionId);
        if (snapshotMatch.Success
            && int.TryParse(snapshotMatch.Groups[1].Value, out var snapshotYear))
        {
            if (snapshotYear >= 26)
            {
                return 25;
            }

            if (snapshotYear >= 24)
            {
                return 21;
            }

            return 17;
        }

        var parts = minecraftVersionId.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2
            && int.TryParse(parts[0], out var major)
            && int.TryParse(parts[1], out var minor))
        {
            // Year-based releases: 26.x needs Java 25, 25.x uses Java 21.
            if (major >= 26)
            {
                return 25;
            }

            if (major == 25)
            {
                return 21;
            }

            // Classic 1.x releases.
            if (major == 1)
            {
                if (minor >= 21)
                {
                    return 21;
                }

                if (minor == 20
                    && parts.Length >= 3
                    && int.TryParse(parts[2], out var patch)
                    && patch >= 5)
                {
                    return 21;
                }

                if (minor >= 17)
                {
                    return 17;
                }

                return 8;
            }
        }

        return 21;
    }

    /// <summary>
    /// Picks the bundled Temurin runtime DanClient can install for a required Java major version.
    /// </summary>
    public static int MapToBundledVersion(int requiredMajorVersion)
    {
        if (SupportedFeatureVersions.Contains(requiredMajorVersion))
        {
            return requiredMajorVersion;
        }

        if (requiredMajorVersion <= 8)
        {
            return 8;
        }

        if (requiredMajorVersion <= 17)
        {
            return 17;
        }

        if (requiredMajorVersion <= 21)
        {
            return 21;
        }

        return 25;
    }

    public static string GetRuntimeLabel(int featureVersion) => $"Java {featureVersion} (Temurin)";

    public static string GetSupportedVersionsText() =>
        string.Join(", ", SupportedFeatureVersions.Select(version => $"Java {version}"));

    [GeneratedRegex(@"^(\d{2})w", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SnapshotVersionRegex();

    private sealed record VersionJavaMetadata(
        [property: JsonPropertyName("javaVersion")] JavaVersionReference? JavaVersion);

    private sealed record JavaVersionReference(
        [property: JsonPropertyName("majorVersion")] int MajorVersion);
}
