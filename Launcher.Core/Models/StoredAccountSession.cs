namespace Launcher.Core.Models;

public sealed record StoredAccountSession(
    string AccessToken,
    string UserName,
    string Uuid,
    DateTimeOffset ExpiresAt)
{
    public MinecraftAccount ToAccount() => new(AccessToken, UserName, Uuid, ExpiresAt);

    public static StoredAccountSession FromAccount(MinecraftAccount account) =>
        new(account.AccessToken, account.UserName, account.Uuid, account.ExpiresAt);
}
