namespace Launcher.Core.Models;

public sealed record DeviceCodeInfo(
    string UserCode,
    string VerificationUri,
    string Message,
    int ExpiresInSeconds,
    int PollIntervalSeconds);

public sealed record MinecraftAccount(
    string AccessToken,
    string UserName,
    string Uuid,
    DateTimeOffset ExpiresAt);

public sealed record AuthProgress(string Stage, string Message);
