namespace Launcher.Core.Models;

public sealed record ModrinthBrowseResult(
    IReadOnlyList<ModrinthProject> Projects,
    int TotalHits,
    int Offset,
    int Limit);

public sealed record ModrinthProject(
    string ProjectId,
    string Slug,
    string Title,
    string Description,
    string Author,
    string ProjectType,
    int Downloads,
    string? IconUrl);

public sealed record ModrinthProjectVersion(
    string Id,
    string Name,
    string VersionNumber,
    string VersionType = "release");

public sealed record ModrinthInstallResult(
    string ProjectSlug,
    string VersionName,
    string FileName,
    string TargetPath,
    long Size);
