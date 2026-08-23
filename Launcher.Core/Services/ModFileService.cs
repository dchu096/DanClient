using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class ModFileService : IModFileService
{
    private readonly string _iconCacheDirectory;
    private FileSystemWatcher? _watcher;

    public event EventHandler? ModsChanged;

    public string ModsDirectory { get; private set; }

    public ModFileService(string? modsDirectory = null)
    {
        AppPaths.Ensure();
        ModsDirectory = modsDirectory ?? AppPaths.DefaultMods;
        Directory.CreateDirectory(ModsDirectory);
        _iconCacheDirectory = Path.Combine(AppPaths.Cache, "mod-icons");
        Directory.CreateDirectory(_iconCacheDirectory);

        ConfigureWatcher();
    }

    public void SetModsDirectory(string modsDirectory)
    {
        if (string.Equals(ModsDirectory, modsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ModsDirectory = modsDirectory;
        Directory.CreateDirectory(ModsDirectory);
        ConfigureWatcher();
        ModsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<ModInfo>> LoadModsAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ModsDirectory);
        var jarFiles = Directory.EnumerateFiles(ModsDirectory, "*.jar")
            .Concat(Directory.EnumerateFiles(ModsDirectory, "*.jar.disabled"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var tasks = jarFiles.Select(path => ReadModInfoAsync(path, cancellationToken));
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public async Task<ModInfo> SetEnabledAsync(ModInfo mod, bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = mod.FilePath;
        var target = enabled
            ? source.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? source[..^".disabled".Length]
                : source
            : source.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? source
                : source + ".disabled";

        if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => File.Move(source, target, overwrite: true), cancellationToken).ConfigureAwait(false);
        }

        return await ReadModInfoAsync(target, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteModAsync(ModInfo mod, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            if (File.Exists(mod.FilePath))
            {
                File.Delete(mod.FilePath);
            }

            ModsChanged?.Invoke(this, EventArgs.Empty);
        }, cancellationToken);
    }

    public void OpenModsFolder()
    {
        Directory.CreateDirectory(ModsDirectory);
        ProcessStartInfo startInfo;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            startInfo = new ProcessStartInfo("explorer.exe", ModsDirectory);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            startInfo = new ProcessStartInfo("open", ModsDirectory);
        }
        else
        {
            startInfo = new ProcessStartInfo("xdg-open", ModsDirectory);
        }

        startInfo.UseShellExecute = false;
        Process.Start(startInfo);
    }

    public void Dispose() => _watcher?.Dispose();

    private void ConfigureWatcher()
    {
        if (_watcher is not null)
        {
            _watcher.Created -= OnWatcherChanged;
            _watcher.Deleted -= OnWatcherChanged;
            _watcher.Renamed -= OnWatcherChanged;
            _watcher.Changed -= OnWatcherChanged;
            _watcher.Dispose();
        }

        _watcher = new FileSystemWatcher(ModsDirectory)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            Filter = "*.jar*",
            EnableRaisingEvents = true
        };

        _watcher.Created += OnWatcherChanged;
        _watcher.Deleted += OnWatcherChanged;
        _watcher.Renamed += OnWatcherChanged;
        _watcher.Changed += OnWatcherChanged;
    }

    private void OnWatcherChanged(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            || e.FullPath.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
        {
            ModsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<ModInfo> ReadModInfoAsync(string path, CancellationToken cancellationToken)
    {
        return await Task.Run(async () =>
        {
            var isEnabled = path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
            try
            {
                using var archive = ZipFile.OpenRead(path);
                var fabricEntry = archive.GetEntry("fabric.mod.json");
                if (fabricEntry is not null)
                {
                    return await ReadFabricModAsync(path, isEnabled, archive, fabricEntry, cancellationToken).ConfigureAwait(false);
                }

                var forgeEntry = archive.GetEntry("mcmod.info");
                if (forgeEntry is not null)
                {
                    return await ReadForgeModAsync(path, isEnabled, archive, forgeEntry, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                // Bad or locked JARs still appear in the UI with filename fallback metadata.
            }

            var fallbackName = Path.GetFileNameWithoutExtension(path.Replace(".disabled", string.Empty, StringComparison.OrdinalIgnoreCase));
            return new ModInfo(fallbackName, fallbackName, "unknown", path, null, isEnabled, "Unknown");
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ModInfo> ReadFabricModAsync(
        string path,
        bool isEnabled,
        ZipArchive archive,
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var id = GetString(root, "id") ?? Path.GetFileNameWithoutExtension(path);
        var name = GetString(root, "name") ?? id;
        var version = GetString(root, "version") ?? "unknown";
        var iconPath = await ExtractIconAsync(path, archive, ResolveFabricIcon(root), cancellationToken).ConfigureAwait(false);
        return new ModInfo(id, name, version, path, iconPath, isEnabled, "Fabric");
    }

    private async Task<ModInfo> ReadForgeModAsync(
        string path,
        bool isEnabled,
        ZipArchive archive,
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var mod = document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() > 0
            ? document.RootElement[0]
            : document.RootElement;

        var id = GetString(mod, "modid") ?? Path.GetFileNameWithoutExtension(path);
        var name = GetString(mod, "name") ?? id;
        var version = GetString(mod, "version") ?? "unknown";
        var iconPath = await ExtractIconAsync(path, archive, GetString(mod, "logoFile"), cancellationToken).ConfigureAwait(false);
        return new ModInfo(id, name, version, path, iconPath, isEnabled, "Forge");
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? ResolveFabricIcon(JsonElement root)
    {
        if (!root.TryGetProperty("icon", out var icon))
        {
            return null;
        }

        if (icon.ValueKind == JsonValueKind.String)
        {
            return icon.GetString();
        }

        if (icon.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? fallback = null;
        foreach (var property in icon.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            fallback ??= property.Value.GetString();
            if (property.Name == "64" && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return fallback;
    }

    private async Task<string?> ExtractIconAsync(
        string jarPath,
        ZipArchive archive,
        string? iconEntryPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(iconEntryPath))
        {
            return null;
        }

        var entry = archive.GetEntry(iconEntryPath.Replace('\\', '/'));
        if (entry is null)
        {
            return null;
        }

        var extension = Path.GetExtension(iconEntryPath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".png";
        }

        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(jarPath + File.GetLastWriteTimeUtc(jarPath))));
        var iconPath = Path.Combine(_iconCacheDirectory, hash + extension);
        if (File.Exists(iconPath))
        {
            return iconPath;
        }

        await using var input = entry.Open();
        await using var output = File.Create(iconPath);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        return iconPath;
    }
}
