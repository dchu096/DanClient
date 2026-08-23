namespace Launcher.Core.Models;

public sealed record FabricLoaderResolution(
    string LoaderVersion,
    string IntermediaryVersion,
    Uri ProfileJsonUri);

public sealed record ModrinthDownload(
    string ProjectSlug,
    string VersionName,
    string FileName,
    Uri DownloadUri,
    long Size);
