using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Launcher.Core.Services;

internal static class DownloadUtility
{
    public static async Task DownloadFileAsync(
        HttpClient httpClient,
        Uri uri,
        string targetPath,
        IProgress<string>? progress = null,
        long? expectedSize = null,
        string? expectedSha1 = null,
        CancellationToken cancellationToken = default)
    {
        if (IsComplete(targetPath, expectedSize, expectedSha1))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var tempPath = targetPath + ".download";
        var existingBytes = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;
        if (IsComplete(tempPath, expectedSize, expectedSha1))
        {
            File.Move(tempPath, targetPath, overwrite: true);
            return;
        }

        if (expectedSize is not null && existingBytes > expectedSize.Value)
        {
            File.Delete(tempPath);
            existingBytes = 0;
        }

        progress?.Report(existingBytes > 0
            ? $"Resuming {Path.GetFileName(targetPath)}."
            : $"Downloading {Path.GetFileName(targetPath)}.");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (existingBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);
        }

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (existingBytes > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            File.Delete(tempPath);
            existingBytes = 0;
        }

        response.EnsureSuccessStatusCode();
        var mode = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent
            ? FileMode.Append
            : FileMode.Create;

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = new FileStream(tempPath, mode, FileAccess.Write, FileShare.None))
        {
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        if (!IsComplete(tempPath, expectedSize, expectedSha1))
        {
            throw new IOException($"{Path.GetFileName(targetPath)} did not pass download validation.");
        }

        File.Move(tempPath, targetPath, overwrite: true);
    }

    private static bool IsComplete(string path, long? expectedSize, string? expectedSha1)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (expectedSize is not null && new FileInfo(path).Length != expectedSize.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedSha1)
            && !string.Equals(ComputeSha1(path), expectedSha1, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string ComputeSha1(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
    }
}
