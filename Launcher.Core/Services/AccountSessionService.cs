using System.Text.Json;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class AccountSessionService : IAccountSessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<MinecraftAccount?> LoadAsync(CancellationToken cancellationToken = default)
    {
        AppPaths.Ensure();
        if (!File.Exists(AppPaths.AccountSessionFile))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(AppPaths.AccountSessionFile);
            var session = await JsonSerializer.DeserializeAsync<StoredAccountSession>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (session is null || session.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(5))
            {
                await ClearAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            return session.ToAccount();
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(MinecraftAccount account, CancellationToken cancellationToken = default)
    {
        AppPaths.Ensure();
        await using var stream = File.Create(AppPaths.AccountSessionFile);
        await JsonSerializer.SerializeAsync(
            stream,
            StoredAccountSession.FromAccount(account),
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(AppPaths.AccountSessionFile))
        {
            File.Delete(AppPaths.AccountSessionFile);
        }

        return Task.CompletedTask;
    }
}
