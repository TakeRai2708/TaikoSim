using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace TakeTaikoSimulator.Updates;

internal sealed record GameRelease(Version Version, Uri DownloadUrl, long Size, byte[] Sha256,
    DateTimeOffset ExpiresUtc, string Notes, GitHubReleaseSource? GitHub = null, InviteReleaseSource? Invite = null);

internal sealed class UpdateFeed(HttpClient http)
{
    internal const int MetadataLimit = 64 * 1024;
    private const long PackageLimit = 4L * 1024 * 1024 * 1024;
    private const string TargetProduct = "TakeTaikoSimulator/win-x64";

    internal async Task<GameRelease> CheckAsync(string settingsPath, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(settingsPath);
        return await CheckAsync(file, cancellationToken);
    }

    internal async Task<GameRelease> CheckAsync(Stream configuration, CancellationToken cancellationToken)
    {
        using var settings = await ReadMetadataAsync(configuration, cancellationToken);
        using RSA key = RSA.Create();
        key.ImportFromPem(settings.RootElement.GetProperty("publicKeyPem").GetString());
        if (key.KeySize < 2048)
            throw new InvalidDataException("配布元の公開鍵が短すぎます。");

        if (settings.RootElement.TryGetProperty("relayUrl", out var relayUrl))
        {
            var invite = new InviteReleaseSource(http, HttpsUri(relayUrl.GetString()),
                settings.RootElement.GetProperty("inviteCode").GetString());
            using var relayResponse = await invite.ReadManifestAsync(cancellationToken);
            var release = await ReadSignedReleaseAsync(relayResponse, key, cancellationToken);
            return release with { Invite = invite };
        }

        if (settings.RootElement.TryGetProperty("githubRepository", out var repository))
        {
            bool anonymous = settings.RootElement.TryGetProperty("githubPublic", out var publicFeed) && publicFeed.GetBoolean();
            var github = new GitHubReleaseSource(http, repository.GetString(), anonymous: anonymous);
            string? tag = settings.RootElement.TryGetProperty("githubTag", out var configuredTag) ? configuredTag.GetString() : null;
            using var metadata = await github.ReadReleaseAsync(tag, cancellationToken);
            if (metadata.RootElement.GetProperty("draft").GetBoolean())
                throw new InvalidDataException("GitHub の下書きリリースは使用できません。");
            var assets = metadata.RootElement.GetProperty("assets").EnumerateArray().ToArray();
            var envelopes = assets.Where(asset => asset.GetProperty("name").GetString() == "latest.json").ToArray();
            if (envelopes.Length != 1)
                throw new InvalidDataException("GitHub リリースに署名済みの latest.json がありません。");

            var manifestAsset = HttpsUri(envelopes[0].GetProperty(github.AssetUrlField).GetString());
            using var assetResponse = await github.GetAssetAsync(manifestAsset, cancellationToken,
                anonymous ? envelopes[0].GetProperty("id").GetInt64() : null);
            var release = await ReadSignedReleaseAsync(assetResponse, key, cancellationToken);
            if (!github.IsAsset(release.DownloadUrl) ||
                !assets.Any(asset => asset.GetProperty("name").GetString()?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true &&
                    Uri.TryCreate(asset.GetProperty(github.AssetUrlField).GetString(), UriKind.Absolute, out var assetUrl) &&
                    release.DownloadUrl == assetUrl))
                throw new InvalidDataException("署名済みの ZIP が同じ GitHub リリースに見つかりません。");
            return release with { GitHub = github };
        }

        Uri manifestUrl = HttpsUri(settings.RootElement.GetProperty("manifestUrl").GetString());
        using var response = await http.GetAsync(manifestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return await ReadSignedReleaseAsync(response, key, cancellationToken);
    }

    private static async Task<GameRelease> ReadSignedReleaseAsync(HttpResponseMessage response, RSA key,
        CancellationToken cancellationToken)
    {
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
        string notes = release.TryGetProperty("notes", out var value) ? value.GetString() ?? "" : "";
        if (notes.Length > 1600 || notes.Contains('\0'))
            throw new InvalidDataException("更新内容が長すぎるか無効です。");
        return new(version, HttpsUri(release.GetProperty("downloadUrl").GetString()), size, sha256, expiresUtc, notes);
    }

    internal async Task<string> DownloadAsync(GameRelease release, string directory, Action<long> reportBytes,
        CancellationToken cancellationToken)
    {
        EnsureNotExpired(release.ExpiresUtc);
        using var response = release.Invite is { } invite
            ? await invite.DownloadAsync(release.DownloadUrl, cancellationToken)
            : release.GitHub is { } github
            ? await github.GetAssetAsync(release.DownloadUrl, cancellationToken)
            : await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length != release.Size)
            throw new InvalidDataException("配布ファイルのサイズが更新情報と一致しません。");

        string partial = Path.Combine(directory, "update.part");
        string zip = Path.Combine(directory, "update.zip");
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
            return zip;
        }
        finally
        {
            if (ownsPartial)
                File.Delete(partial);
        }
    }

    internal static async Task<JsonDocument> ReadMetadataAsync(Stream source, CancellationToken cancellationToken,
        int limit = MetadataLimit)
    {
        var buffer = new byte[limit + 1];
        int count = await source.ReadAtLeastAsync(buffer, buffer.Length, false, cancellationToken);
        if (count > limit)
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
