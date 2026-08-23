using System.IO.Compression;

namespace Launcher.Core.Services;

public sealed class JavaRuntimeService : IJavaRuntimeService
{
    private readonly HttpClient _httpClient;

    public JavaRuntimeService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public int GetRequiredJavaFeatureVersion(string? minecraftVersionId, string? minecraftDirectory = null) =>
        JavaVersionResolver.GetRequiredJavaFeatureVersion(minecraftVersionId, minecraftDirectory);

    public IReadOnlyList<int> GetInstalledJavaVersions() =>
        JavaVersionResolver.SupportedVersions
            .Where(version => File.Exists(GetBundledJavaExecutable(version)))
            .ToArray();

    public bool IsJavaExecutableAvailable(string javaExecutable)
    {
        if (string.IsNullOrWhiteSpace(javaExecutable))
        {
            return false;
        }

        if (Path.IsPathRooted(javaExecutable)
            || javaExecutable.Contains(Path.DirectorySeparatorChar)
            || javaExecutable.Contains(Path.AltDirectorySeparatorChar))
        {
            return File.Exists(javaExecutable);
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (File.Exists(Path.Combine(directory, javaExecutable)))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<string> EnsureJavaRuntimeAsync(
        int featureVersion,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!JavaVersionResolver.SupportedVersions.Contains(featureVersion))
        {
            throw new ArgumentOutOfRangeException(nameof(featureVersion), $"DanClient supports bundled Java versions: {string.Join(", ", JavaVersionResolver.SupportedVersions)}.");
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Bundled Java install is currently implemented for Windows builds.");
        }

        var javaExecutable = GetBundledJavaExecutable(featureVersion);
        if (File.Exists(javaExecutable))
        {
            progress?.Report($"{JavaVersionResolver.GetRuntimeLabel(featureVersion)} is ready.");
            return javaExecutable;
        }

        progress?.Report($"Downloading {JavaVersionResolver.GetRuntimeLabel(featureVersion)} JRE.");
        Directory.CreateDirectory(AppPaths.Java);
        var archivePath = Path.Combine(AppPaths.Cache, $"temurin-{featureVersion}-jre-windows-x64.zip");
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        await DownloadUtility.DownloadFileAsync(
            _httpClient,
            BuildDownloadUri(featureVersion),
            archivePath,
            progress,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        progress?.Report($"Extracting {JavaVersionResolver.GetRuntimeLabel(featureVersion)}.");
        var runtimeRoot = AppPaths.GetBundledJavaRoot(featureVersion);
        var extractRoot = runtimeRoot + ".extract";
        DeleteDirectoryIfSafe(extractRoot);
        Directory.CreateDirectory(extractRoot);
        ZipFile.ExtractToDirectory(archivePath, extractRoot, overwriteFiles: true);

        var extractedJava = Directory.EnumerateFiles(extractRoot, "java.exe", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.EndsWith(Path.Combine("bin", "java.exe"), StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Downloaded Java {featureVersion} runtime did not include bin\\java.exe.");

        var extractedRuntimeRoot = Directory.GetParent(Directory.GetParent(extractedJava)!.FullName)!.FullName;
        DeleteDirectoryIfSafe(runtimeRoot);
        Directory.Move(extractedRuntimeRoot, runtimeRoot);
        DeleteDirectoryIfSafe(extractRoot);

        javaExecutable = GetBundledJavaExecutable(featureVersion);
        if (!File.Exists(javaExecutable))
        {
            throw new FileNotFoundException($"Bundled Java {featureVersion} was extracted, but bin\\java.exe was not found.");
        }

        progress?.Report($"{JavaVersionResolver.GetRuntimeLabel(featureVersion)} is ready.");
        return javaExecutable;
    }

    public async Task EnsureAllSupportedRuntimesAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var featureVersion in JavaVersionResolver.SupportedVersions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureJavaRuntimeAsync(featureVersion, progress, cancellationToken).ConfigureAwait(false);
        }
    }

    public string GetBundledJavaExecutable(int featureVersion) =>
        Path.Combine(AppPaths.GetBundledJavaRoot(featureVersion), "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");

    private static Uri BuildDownloadUri(int featureVersion)
    {
        var architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86";
        return new Uri($"https://api.adoptium.net/v3/binary/latest/{featureVersion}/ga/windows/{architecture}/jre/hotspot/normal/eclipse");
    }

    private static void DeleteDirectoryIfSafe(string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        var fullJavaRoot = Path.GetFullPath(AppPaths.Java);
        if (!fullDirectory.StartsWith(fullJavaRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to delete directory outside DanClient Java root: {fullDirectory}");
        }

        if (Directory.Exists(fullDirectory))
        {
            Directory.Delete(fullDirectory, recursive: true);
        }
    }
}
