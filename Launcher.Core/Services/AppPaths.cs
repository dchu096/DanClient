namespace Launcher.Core.Services;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DanClient");

    public static string Cache { get; } = Path.Combine(Root, "cache");
    public static string Instances { get; } = Path.Combine(Root, "instances");
    public static string Java { get; } = Path.Combine(Root, "java");

    public static string GetBundledJavaRoot(int featureVersion) =>
        Path.Combine(Java, $"temurin-{featureVersion}");
    public static string ProfilesFile { get; } = Path.Combine(Root, "profiles.json");
    public static string AccountSessionFile { get; } = Path.Combine(Root, "account-session.json");
    public static string DefaultInstance { get; } = Path.Combine(Instances, "Default");
    public static string DefaultMods { get; } = Path.Combine(DefaultInstance, "mods");

    public static string GetProfileInstance(string profileId) =>
        Path.Combine(Instances, SanitizePathSegment(profileId));

    public static string GetProfileInstance(string profileId, string? minecraftVersionId) =>
        string.IsNullOrWhiteSpace(minecraftVersionId)
            ? GetProfileInstance(profileId)
            : Path.Combine(GetProfileInstance(profileId), "versions", SanitizePathSegment(minecraftVersionId));

    public static void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Instances);
        Directory.CreateDirectory(Java);
        Directory.CreateDirectory(DefaultInstance);
        Directory.CreateDirectory(DefaultMods);
    }

    public static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "profile" : sanitized;
    }
}
