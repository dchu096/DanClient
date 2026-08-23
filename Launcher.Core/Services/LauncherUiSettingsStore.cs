using System.Text.Json;

namespace Launcher.Core.Services;

public static class LauncherUiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string SettingsFile => Path.Combine(AppPaths.Root, "ui-settings.json");

    public static LauncherUiSettings Load()
    {
        AppPaths.Ensure();
        if (!File.Exists(SettingsFile))
        {
            return LauncherUiSettings.Default;
        }

        try
        {
            var json = File.ReadAllText(SettingsFile);
            return JsonSerializer.Deserialize<LauncherUiSettings>(json, JsonOptions) ?? LauncherUiSettings.Default;
        }
        catch
        {
            return LauncherUiSettings.Default;
        }
    }

    public static void Save(LauncherUiSettings settings)
    {
        AppPaths.Ensure();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsFile, json);
    }
}

public sealed record LauncherUiSettings(
    bool ShowModrinthBrowse = true,
    bool HideModAlphaVersions = false,
    bool HideModBetaVersions = false)
{
    public static LauncherUiSettings Default { get; } = new();
}
