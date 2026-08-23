namespace Launcher.Core.Models;

public sealed record ModrinthVersionFilter(bool HideAlpha = false, bool HideBeta = false)
{
    public static ModrinthVersionFilter None { get; } = new();

    public bool Includes(string? versionType)
    {
        var normalized = string.IsNullOrWhiteSpace(versionType)
            ? "release"
            : versionType.Trim().ToLowerInvariant();

        if (HideAlpha && normalized is "alpha")
        {
            return false;
        }

        if (HideBeta && normalized is "beta")
        {
            return false;
        }

        return true;
    }
}
