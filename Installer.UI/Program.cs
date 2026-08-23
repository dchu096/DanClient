using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Skia;

namespace Installer.UI;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            throw;
        }
    }

    static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseWin32()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .LogToTrace();

    static void LogCrash(object error)
    {
        try
        {
            var message = error.ToString() ?? "Unknown error";
            var logPath = Path.Combine(Path.GetTempPath(), "DanClientSetup-crash.log");
            File.WriteAllText(logPath, message);
            MessageBox(IntPtr.Zero, $"{message}\n\nLog: {logPath}", "DanClient Setup", 0x10);
        }
        catch { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
