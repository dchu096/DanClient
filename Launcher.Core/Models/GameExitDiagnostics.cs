namespace Launcher.Core.Models;

public sealed record GameExitDiagnostics(bool Crashed, string Summary, string DisplayText);
