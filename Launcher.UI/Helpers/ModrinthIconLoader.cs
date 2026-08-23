using Avalonia.Media.Imaging;

namespace Launcher.UI.Helpers;

internal static class ModrinthIconLoader
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    static ModrinthIconLoader()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("DanClient/0.1 modrinth");
    }

    public static async Task<Bitmap?> LoadAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        try
        {
            var bytes = await Client.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }
}
