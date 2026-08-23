using System.Collections.ObjectModel;
using System.Diagnostics;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class JavaLauncherService : IJavaLauncherService
{
    public Task<GameProcessHandle> LaunchAsync(
        GameLaunchOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var classPath = BuildClassPath(options.MinecraftDirectory, options.VersionId);
        Directory.CreateDirectory(Path.Combine(options.MinecraftDirectory, "logs"));
        var launcherLogPath = Path.Combine(options.MinecraftDirectory, "logs", "danclient-launch.log");
        var startedAt = DateTimeOffset.UtcNow;

        var processStartInfo = new ProcessStartInfo
        {
            FileName = options.JavaExecutable,
            WorkingDirectory = options.MinecraftDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        processStartInfo.ArgumentList.Add($"-Xmx{Math.Max(1024, options.MaxMemoryMegabytes)}M");
        processStartInfo.ArgumentList.Add("-Djava.library.path=natives");
        processStartInfo.ArgumentList.AddRange(options.ExtraJvmArgs);
        processStartInfo.ArgumentList.Add("-cp");
        processStartInfo.ArgumentList.Add(classPath);
        processStartInfo.ArgumentList.Add(options.MainClass);
        processStartInfo.ArgumentList.Add("--username");
        processStartInfo.ArgumentList.Add(options.PlayerName);
        processStartInfo.ArgumentList.Add("--version");
        processStartInfo.ArgumentList.Add(options.VersionId);
        processStartInfo.ArgumentList.Add("--gameDir");
        processStartInfo.ArgumentList.Add(options.MinecraftDirectory);
        processStartInfo.ArgumentList.Add("--assetsDir");
        processStartInfo.ArgumentList.Add(Path.Combine(options.MinecraftDirectory, "assets"));
        processStartInfo.ArgumentList.Add("--assetIndex");
        processStartInfo.ArgumentList.Add(options.AssetIndexId);
        processStartInfo.ArgumentList.Add("--uuid");
        processStartInfo.ArgumentList.Add(options.Uuid);
        processStartInfo.ArgumentList.Add("--accessToken");
        processStartInfo.ArgumentList.Add(options.AccessToken);
        processStartInfo.ArgumentList.Add("--userType");
        processStartInfo.ArgumentList.Add("msa");

        var process = Process.Start(processStartInfo)
                      ?? throw new InvalidOperationException("Java process could not be started.");

        _ = Task.Run(() => CaptureProcessOutputAsync(process, launcherLogPath, startedAt, cancellationToken));

        return Task.FromResult(new GameProcessHandle(
            process.Id,
            startedAt,
            options.MinecraftDirectory,
            launcherLogPath));
    }

    private static async Task CaptureProcessOutputAsync(
        Process process,
        string launcherLogPath,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var writer = new StreamWriter(launcherLogPath, append: false);
            await writer.WriteLineAsync($"# DanClient launch log — {startedAt:u}").ConfigureAwait(false);
            await writer.WriteLineAsync($"# PID {process.Id}").ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);

            var stdout = PumpStreamAsync(process.StandardOutput, "stdout", writer, cancellationToken);
            var stderr = PumpStreamAsync(process.StandardError, "stderr", writer, cancellationToken);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
            await writer.WriteLineAsync($"# Process exited with code {process.ExitCode}").ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // Best-effort logging only.
        }
    }

    private static async Task PumpStreamAsync(
        StreamReader reader,
        string label,
        StreamWriter writer,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            await writer.WriteLineAsync($"[{label}] {line}").ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildClassPath(string minecraftDirectory, string versionId)
    {
        var libraries = Path.Combine(minecraftDirectory, "libraries");
        var versionJar = Path.Combine(minecraftDirectory, "versions", versionId, $"{versionId}.jar");
        var entries = new List<string>();

        if (Directory.Exists(libraries))
        {
            entries.AddRange(Directory.EnumerateFiles(libraries, "*.jar", SearchOption.AllDirectories));
        }

        if (File.Exists(versionJar))
        {
            entries.Add(versionJar);
        }

        if (entries.Count == 0)
        {
            throw new DirectoryNotFoundException("No Minecraft libraries or client JARs were found for launch.");
        }

        return string.Join(Path.PathSeparator, entries);
    }
}

internal static class ProcessArgumentListExtensions
{
    public static void AddRange(this Collection<string> target, IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
        {
            target.Add(argument);
        }
    }
}
