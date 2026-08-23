namespace Launcher.Core.Services;

public static class DiscordSettingsStore
{
    public const string DefaultApplicationId = "1540636670163165315";

    public static string ResolveApplicationId()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("DANCLIENT_DISCORD_APP_ID");
        return string.IsNullOrWhiteSpace(fromEnvironment)
            ? DefaultApplicationId
            : fromEnvironment.Trim();
    }
}
