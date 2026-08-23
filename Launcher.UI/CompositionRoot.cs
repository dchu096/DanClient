using Launcher.Core.Services;
using Launcher.UI.ViewModels;
using System.Net;

namespace Launcher.UI;

internal static class CompositionRoot
{
    public static MainWindowViewModel CreateMainWindowViewModel()
    {
        AppPaths.Ensure();

        var manifestClient = CreateHttpClient();
        var fabricClient = CreateHttpClient();
        var authClient = CreateHttpClient();
        var modrinthClient = CreateHttpClient();
        var javaClient = CreateHttpClient();
        javaClient.Timeout = TimeSpan.FromMinutes(10);

        var discordApplicationId = DiscordSettingsStore.ResolveApplicationId();

        return new MainWindowViewModel(
            new MojangVersionManifestService(manifestClient),
            new MinecraftInstallationService(CreateHttpClient()),
            new ModFileService(),
            new FabricInstallerService(fabricClient),
            new JavaLauncherService(),
            new MicrosoftDeviceCodeAuthService(authClient),
            new AccountSessionService(),
            new LauncherProfileService(),
            new ModrinthService(modrinthClient),
            new DiscordPresenceService(discordApplicationId),
            new JavaRuntimeService(javaClient));
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DanClient/0.1 native-avalonia-launcher");
        return client;
    }
}
