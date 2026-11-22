using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace TakeTaikoLauncher;

internal sealed record GameRelease(Version Version, Uri DownloadUrl, long Size, byte[] Sha256, DateTimeOffset ExpiresUtc);

internal sealed class UpdateFeed(HttpClient http, string downloadDirectory, string tempDirectory)
{
    private const int MetadataLimit = 64 * 1024;
    private const long PackageLimit = 4L * 1024 * 1024 * 1024;
    private const string TargetProduct = "TakeTaikoSimulator/win-x64";

    internal async Task<GameRelease> CheckAsync(string settingsPath, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(settingsPath);
        using var settings = await ReadMetadataAsync(file, cancellationToken);
        Uri manifestUrl = HttpsUri(settings.RootElement.GetProperty("manifestUrl").GetString());
        using RSA key = RSA.Create();
        key.ImportFromPem(settings.RootElement.GetProperty("publicKeyPem").GetString());
        if (key.KeySize < 2048)
            throw new InvalidDataException("配布元の公開鍵が短すぎます。");

        using var response = await http.GetAsync(manifestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var envelope = await ReadMetadataAsync(stream, cancellationToken);
        return VerifyRelease(envelope.RootElement, key);
    }

    private static GameRelease VerifyRelease(JsonElement envelope, RSA key)
    {
        // 配布側で署名した UTF-8 のバイト列をそのまま検証する。
        byte[] payload = envelope.GetProperty("payload").GetBytesFromBase64();
        byte[] signature = envelope.GetProperty("signature").GetBytesFromBase64();
        if (!key.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new InvalidDataException("更新情報の署名を確認できませんでした。");

        using var document = JsonDocument.Parse(payload);
        var release = document.RootElement;
        if (release.GetProperty("schema").GetInt32() != 1 ||
            release.GetProperty("product").GetString() != TargetProduct)
            throw new InvalidDataException("このゲーム用の更新情報ではありません。");

        if (!Version.TryParse(release.GetProperty("version").GetString(), out var version) ||
            version.Revision < 0 || version.Major > ushort.MaxValue || version.Minor > ushort.MaxValue ||
            version.Build > ushort.MaxValue || version.Revision > ushort.MaxValue)
            throw new InvalidDataException("更新版のバージョン番号が無効です。");

        long size = release.GetProperty("size").GetInt64();
        byte[] sha256 = Convert.FromHexString(release.GetProperty("sha256").GetString() ?? "");
        if (size <= 0 || size > PackageLimit || sha256.Length != SHA256.HashSizeInBytes)
            throw new InvalidDataException("配布ファイルのサイズまたはハッシュが無効です。");

        var expiresUtc = release.GetProperty("expiresUtc").GetDateTimeOffset();
        EnsureNotExpired(expiresUtc);
        return new(version, HttpsUri(release.GetProperty("downloadUrl").GetString()), size, sha256, expiresUtc);
    }

    internal async Task DownloadAsync(GameRelease release, Action<long> reportBytes, CancellationToken cancellationToken)
    {
        EnsureNotExpired(release.ExpiresUtc);
        using var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length != release.Size)
            throw new InvalidDataException("配布ファイルのサイズが更新情報と一致しません。");

        string name = $"TakeTaikoSimulator-{release.Version}-win-x64-{Guid.NewGuid():N}";
        Directory.CreateDirectory(downloadDirectory);
        Directory.CreateDirectory(tempDirectory);
        string partial = Path.Combine(tempDirectory, name + ".part");
        string zip = Path.Combine(downloadDirectory, name + ".zip");
        bool ownsPartial = false;
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[128 * 1024];
            long received = 0;
            var timer = Stopwatch.StartNew();
            await using (var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                buffer.Length, FileOptions.Asynchronous))
            {
                ownsPartial = true;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    received += read;
                    if (received > release.Size)
                        throw new InvalidDataException("配布ファイルが署名済みのサイズを超えました。");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    if (timer.ElapsedMilliseconds >= 100 || received == release.Size)
                    {
                        reportBytes(received);
                        timer.Restart();
                    }
                }
            }

            if (received != release.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), release.Sha256))
                throw new InvalidDataException("配布ファイルが署名済みのサイズまたはハッシュと一致しません。");

            EnsureNotExpired(release.ExpiresUtc);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partial, zip);
            ownsPartial = false;
        }
        finally
        {
            if (ownsPartial)
                File.Delete(partial);
        }
    }

    private static async Task<JsonDocument> ReadMetadataAsync(Stream source, CancellationToken cancellationToken)
    {
        var buffer = new byte[MetadataLimit + 1];
        int count = await source.ReadAtLeastAsync(buffer, buffer.Length, false, cancellationToken);
        if (count > MetadataLimit)
            throw new InvalidDataException("更新情報のサイズが上限を超えました。");
        return JsonDocument.Parse(buffer.AsMemory(0, count));
    }

    private static Uri HttpsUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("配布先には HTTPS の URL が必要です。");
        return uri;
    }

    private static void EnsureNotExpired(DateTimeOffset expiry)
    {
        if (expiry <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("更新情報の有効期限が切れています。配布元に確認してください。");
    }
}
