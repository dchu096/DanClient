using Launcher.Core.Models;

namespace Launcher.Core.Services;

public static class GameCrashDiagnostics
{
    private const int MaxDisplayCharacters = 12_000;
    private const int LogTailLines = 80;

    public static async Task<GameExitDiagnostics> AnalyzeAsync(
        string minecraftDirectory,
        DateTimeOffset startedAt,
        int exitCode,
        string? launcherLogPath,
        CancellationToken cancellationToken = default)
    {
        var crashReport = FindNewestCrashReport(minecraftDirectory, startedAt);
        if (crashReport is not null)
        {
            var reportText = await ReadTextAsync(crashReport, cancellationToken).ConfigureAwait(false);
            return new GameExitDiagnostics(
                true,
                $"Minecraft crashed — see report {Path.GetFileName(crashReport)}.",
                TrimForDisplay(reportText));
        }

        var hsErrLog = FindNewestHsErrLog(minecraftDirectory, startedAt);
        if (hsErrLog is not null)
        {
            var hsText = await ReadTextAsync(hsErrLog, cancellationToken).ConfigureAwait(false);
            return new GameExitDiagnostics(
                true,
                "The Java runtime crashed (hs_err log).",
                TrimForDisplay(hsText));
        }

        if (exitCode != 0)
        {
            var details = await BuildLogTailSummaryAsync(minecraftDirectory, launcherLogPath, cancellationToken)
                .ConfigureAwait(false);
            return new GameExitDiagnostics(
                true,
                $"Minecraft exited with code {exitCode}.",
                details);
        }

        var latestLogPath = Path.Combine(minecraftDirectory, "logs", "latest.log");
        if (File.Exists(latestLogPath))
        {
            var tail = await ReadTailLinesAsync(latestLogPath, LogTailLines, cancellationToken).ConfigureAwait(false);
            if (ContainsCrashSignal(tail))
            {
                return new GameExitDiagnostics(
                    true,
                    "Minecraft reported a crash in the game log.",
                    TrimForDisplay(tail));
            }
        }

        return new GameExitDiagnostics(false, "Ready to play.", string.Empty);
    }

    private static string? FindNewestCrashReport(string minecraftDirectory, DateTimeOffset startedAt)
    {
        var crashReportsDirectory = Path.Combine(minecraftDirectory, "crash-reports");
        if (!Directory.Exists(crashReportsDirectory))
        {
            return null;
        }

        return Directory.EnumerateFiles(crashReportsDirectory, "*.txt")
            .Select(path => new FileInfo(path))
            .Where(info => info.LastWriteTimeUtc >= startedAt.UtcDateTime.AddSeconds(-5))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => info.FullName)
            .FirstOrDefault();
    }

    private static string? FindNewestHsErrLog(string minecraftDirectory, DateTimeOffset startedAt)
    {
        if (!Directory.Exists(minecraftDirectory))
        {
            return null;
        }

        return Directory.EnumerateFiles(minecraftDirectory, "hs_err_pid*.log")
            .Select(path => new FileInfo(path))
            .Where(info => info.LastWriteTimeUtc >= startedAt.UtcDateTime.AddSeconds(-5))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => info.FullName)
            .FirstOrDefault();
    }

    private static async Task<string> BuildLogTailSummaryAsync(
        string minecraftDirectory,
        string? launcherLogPath,
        CancellationToken cancellationToken)
    {
        var sections = new List<string>();
        var latestLogPath = Path.Combine(minecraftDirectory, "logs", "latest.log");
        if (File.Exists(latestLogPath))
        {
            sections.Add("=== logs/latest.log (tail) ===");
            sections.Add(await ReadTailLinesAsync(latestLogPath, LogTailLines, cancellationToken).ConfigureAwait(false));
        }

        if (!string.IsNullOrWhiteSpace(launcherLogPath) && File.Exists(launcherLogPath))
        {
            sections.Add("=== launcher output (tail) ===");
            sections.Add(await ReadTailLinesAsync(launcherLogPath, LogTailLines, cancellationToken).ConfigureAwait(false));
        }

        if (sections.Count == 0)
        {
            return "No crash report or log output was captured.";
        }

        return TrimForDisplay(string.Join(Environment.NewLine + Environment.NewLine, sections));
    }

    private static bool ContainsCrashSignal(string logTail) =>
        logTail.Contains("Game crashed", StringComparison.OrdinalIgnoreCase)
        || logTail.Contains("Minecraft Crash Report", StringComparison.OrdinalIgnoreCase)
        || logTail.Contains("Fatal error", StringComparison.OrdinalIgnoreCase)
        || logTail.Contains("Exception in thread", StringComparison.OrdinalIgnoreCase)
        || logTail.Contains("Process exited with exit code", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadTailLinesAsync(string path, int lineCount, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new Queue<string>(lineCount + 1);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lines.Enqueue(line);
            while (lines.Count > lineCount)
            {
                lines.Dequeue();
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string TrimForDisplay(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "No crash details were captured.";
        }

        var trimmed = text.Trim();
        if (trimmed.Length <= MaxDisplayCharacters)
        {
            return trimmed;
        }

        return trimmed[^MaxDisplayCharacters..];
    }
}
