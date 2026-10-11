using System.Net;
using System.Security.Cryptography;

namespace LaunchDesk.Core;

public sealed record ReleaseAsset(string Name, long Size, string Sha256, Uri DownloadUri);
public sealed record DownloadProgress(long Received, long Total);
public sealed record VerifiedDownload(string Path, string Sha256, string Publisher);
public interface IUpdateSignatureVerifier
{
    string Verify(string path);
}
public sealed class UpdateDownloader
{
    public const long MaximumBytes = 256 * 1024 * 1024;
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient client;
    private readonly IUpdateSignatureVerifier verifier;
    public UpdateDownloader(HttpClient? client = null, IUpdateSignatureVerifier? verifier = null)
    { this.client = client ?? Client; this.verifier = verifier ?? new AuthenticodeVerifier(); }
    public async Task<VerifiedDownload> DownloadAsync(ReleaseUpdate release, string cacheRoot, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (verifier is AuthenticodeVerifier authenticode) authenticode.EnsureConfigured();
        var asset = release.Installer ?? throw new InvalidDataException("发布包缺少安装文件、大小或 SHA-256 信息，请查看发布页面。");
        var expectedName = $"LaunchDesk-v{release.Version.ToString(3)}-win-x64-setup.exe";
        var expectedUri = "https://github.com/hailaij/LaunchDesk/releases/download/" + Uri.EscapeDataString(release.Tag) + "/" + expectedName;
        if (asset.Name != expectedName || asset.DownloadUri.AbsoluteUri != expectedUri || asset.Size is <= 0 or > MaximumBytes
            || asset.Sha256.Length != 64 || !asset.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("更新包信息无效。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var token = timeout.Token;
        var directory = Path.Combine(Path.GetFullPath(cacheRoot), Guid.NewGuid().ToString("N"));
        for (var parent = new DirectoryInfo(directory); parent is not null; parent = parent.Parent)
            if (parent.Exists && parent.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("更新缓存不能经过符号链接或目录联接。");
        var partial = Path.Combine(directory, asset.Name + ".partial");
        var verified = Path.Combine(directory, asset.Name);
        try
        {
            Directory.CreateDirectory(directory);
            using var response = await OpenDownloadAsync(asset.DownloadUri, token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is { } length && length != asset.Size) throw new InvalidDataException("安装包长度与发布信息不符。");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            {
                var buffer = new byte[81920]; long received = 0;
                while (true)
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(token); idle.CancelAfter(TimeSpan.FromSeconds(30));
                    var count = await input.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
                    if (count == 0) break;
                    received += count;
                    if (received > asset.Size || received > MaximumBytes) throw new InvalidDataException("安装包超过声明大小。");
                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, count); progress?.Report(new(received, asset.Size));
                }
                if (received != asset.Size) throw new InvalidDataException("安装包下载不完整。");
                await output.FlushAsync(token).ConfigureAwait(false); output.Flush(true);
            }
            var digest = Convert.ToHexString(hash.GetHashAndReset());
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(digest), Convert.FromHexString(asset.Sha256)))
                throw new InvalidDataException("SHA-256 校验失败，更新包已拒绝。");
            token.ThrowIfCancellationRequested();
            // Verification keeps its file locked against writes/deletion until Windows trust validation finishes.
            string publisher;
            using (var guard = new FileStream(partial, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var storedHash = await SHA256.HashDataAsync(guard, token).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(storedHash, Convert.FromHexString(digest)))
                    throw new InvalidDataException("下载文件在验证前发生变化。");
                publisher = await Task.Run(() => verifier.Verify(partial)).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            File.Move(partial, verified, false);
            return new(verified, digest, publisher);
        }
        catch (Exception failure)
        {
            try
            {
                if (File.Exists(partial)) File.Delete(partial);
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
            }
            catch (Exception cleanup) { throw new AggregateException("更新失败，临时文件清理失败：" + directory, failure, cleanup); }
            throw;
        }
    }
    private async Task<HttpResponseMessage> OpenDownloadAsync(Uri uri, CancellationToken token)
    {
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            ValidateDownloadUri(uri);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("LaunchDesk-UpdateDownload/1.0");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            try
            {
                // Injected clients must not silently bypass the redirect allowlist either.
                if (response.RequestMessage?.RequestUri is { } final) ValidateDownloadUri(final);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    var location = response.Headers.Location ?? throw new InvalidDataException("下载重定向没有地址。");
                    uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    response.Dispose(); continue;
                }
                response.EnsureSuccessStatusCode(); return response;
            }
            catch { response.Dispose(); throw; }
        }
        throw new HttpRequestException("安装包重定向次数过多。");
    }
    public static void ValidateDownloadUri(Uri uri)
    {
        if (uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || uri.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com" or "github-releases.githubusercontent.com"))
            throw new InvalidDataException("下载地址不属于允许的 GitHub HTTPS 域名。");
    }
}
