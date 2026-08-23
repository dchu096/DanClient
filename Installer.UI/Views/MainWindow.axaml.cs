using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Installer.UI.Views;

public partial class MainWindow : Window
{
    private const string AppName = "DanClient";
    private const string ExeName = "Launcher.UI.exe";
    private const string UninstallName = "uninstall.exe";
    private const string AppId = "{A7E3F2B1-8C4D-4E9A-B5F6-1D2C3E4F5A6B}";
    private const string InstallDir = @"C:\Program Files\DanClient";

    private bool _isInstalling;

    public MainWindow() => InitializeComponent();

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void Install_Click(object? sender, RoutedEventArgs e)
    {
        if (_isInstalling) return;
        _isInstalling = true;
        InstallButton.IsVisible = false;
        OptionsPanel.IsVisible = false;
        ProgressPanel.IsVisible = true;
        _ = Task.Run(DoInstall);
    }

    private void DoInstall()
    {
        try
        {
            Dispatcher.UIThread.Post(() => StatusText.Text = "Preparing installation...");

            KillRunningLauncher();
            Thread.Sleep(300);

            Dispatcher.UIThread.Post(() => StatusText.Text = "Creating directories...");
            Directory.CreateDirectory(InstallDir);

            Dispatcher.UIThread.Post(() => StatusText.Text = "Extracting files...");
            ExtractPayload(InstallDir);

            Dispatcher.UIThread.Post(() => StatusText.Text = "Creating shortcuts...");
            CreateShortcuts();

            Dispatcher.UIThread.Post(() => StatusText.Text = "Registering uninstaller...");
            RegisterUninstaller();

            Dispatcher.UIThread.Post(() =>
            {
                StatusText.Text = "Installation complete!";
                ProgressPanel.IsVisible = false;
                FinishPanel.IsVisible = true;
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusText.Text = $"Error: {ex.Message}";
                ProgressPanel.IsVisible = false;
                ErrorPanel.IsVisible = true;
                ErrorText.Text = ex.Message;
            });
        }
        finally
        {
            _isInstalling = false;
        }
    }

    private void ExtractPayload(string targetDir)
    {
        var payloadPath = Path.Combine(AppContext.BaseDirectory, "payload.zip");
        if (File.Exists(payloadPath))
        {
            ZipFile.ExtractToDirectory(payloadPath, targetDir, overwriteFiles: true);
            return;
        }

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = Array.Find(assembly.GetManifestResourceNames(),
            n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));

        if (resourceName is not null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                var dest = Path.Combine(targetDir, entry.FullName);
                var dir = Path.GetDirectoryName(dest);
                if (dir is not null) Directory.CreateDirectory(dir);
                if (entry.Length == 0) continue;
                entry.ExtractToFile(dest, overwrite: true);
            }
            return;
        }

        CopyFromSiblingDir(targetDir);
    }

    private void CopyFromSiblingDir(string targetDir)
    {
        var publishDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "Launcher.UI", "bin", "Release", "net10.0", "win-x64", "publish"));

        if (!Directory.Exists(publishDir))
            publishDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "publish"));

        if (!Directory.Exists(publishDir))
            throw new DirectoryNotFoundException("Could not find launcher files. payload.zip is missing.");

        foreach (var file in Directory.GetFiles(publishDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(publishDir, file);
            var dest = Path.Combine(targetDir, rel);
            var dir = Path.GetDirectoryName(dest);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private void CreateShortcuts()
    {
        var exePath = Path.Combine(InstallDir, ExeName);
        var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", AppName);
        Directory.CreateDirectory(startMenuDir);
        CreateShortcut(Path.Combine(startMenuDir, $"{AppName}.lnk"), exePath, InstallDir);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            var shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = workingDir;
            shortcut.Description = $"{AppName} Minecraft Launcher";
            shortcut.IconLocation = targetPath;
            shortcut.Save();
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }

    private void RegisterUninstaller()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{AppId}_is1");
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", "0.1.5");
        key.SetValue("Publisher", AppName);
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("DisplayIcon", Path.Combine(InstallDir, ExeName));
        key.SetValue("UninstallString", Path.Combine(InstallDir, UninstallName));
        key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);

        CreateUninstaller();
    }

    private void CreateUninstaller()
    {
        var uninstallPath = Path.Combine(InstallDir, UninstallName);
        var script = $""""
@echo off
echo Uninstalling {AppName}...
taskkill /IM {ExeName} /F >nul 2>&1
timeout /t 1 /nobreak >nul
rd /s /q "{InstallDir}" 2>nul
rd /s /q "%ProgramData%\Microsoft\Windows\Start Menu\Programs\{AppName}" 2>nul
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{AppId}_is1" /f 2>nul
echo Done.
del "%~f0"
"""";

        File.WriteAllText(uninstallPath, script);
    }

    private static void KillRunningLauncher()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
                proc.Kill();
        }
        catch { }
    }

    private void LaunchAndClose_Click(object? sender, RoutedEventArgs e)
    {
        var exePath = Path.Combine(InstallDir, ExeName);
        if (File.Exists(exePath))
            Process.Start(new ProcessStartInfo { FileName = exePath, UseShellExecute = true });
        Close();
    }

    private void Finish_Click(object? sender, RoutedEventArgs e) => Close();
}
