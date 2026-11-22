using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Globalization;

namespace TakeTaikoSimulator.Updates;

internal sealed class GitHubReleaseSource
{
    private readonly HttpClient http;
    private readonly Uri releases;
    private readonly string? token;
    private readonly string publicAssetPrefix;
    internal string AssetUrlField => token is null ? "browser_download_url" : "url";

    internal GitHubReleaseSource(HttpClient http, string? repository, string? accessToken = null, bool anonymous = false)
    {
        string[] parts = (repository ?? "").Split('/');
        if (parts.Length != 2 || parts.Any(part => part.Length == 0 || part is "." or ".." ||
            part.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))))
            throw new InvalidDataException("GitHub のリポジトリは owner/repository 形式で指定してください。");

        this.http = http;
        releases = new Uri($"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases/");
        publicAssetPrefix = $"/{parts[0]}/{parts[1]}/releases/download/";
        token = anonymous ? null : (accessToken ?? Environment.GetEnvironmentVariable("TAKETAIKO_GITHUB_TOKEN"))?.Trim() ?? "";
        if (token == "")
            throw new InvalidDataException("この GitHub リリースを読むには TAKETAIKO_GITHUB_TOKEN が必要です。");
    }

    internal async Task<JsonDocument> ReadReleaseAsync(string? tag, CancellationToken cancellationToken)
    {
        string path = string.IsNullOrWhiteSpace(tag) ? "latest" : "tags/" + Uri.EscapeDataString(tag);
        using var request = MakeRequest(new Uri(releases, path), "application/vnd.github+json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await UpdateFeed.ReadMetadataAsync(stream, cancellationToken, 512 * 1024);
    }

    internal bool IsAsset(Uri url)
    {
        if (url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 ||
            url.Query.Length != 0 || url.Fragment.Length != 0)
            return false;
        if (token is null)
            return url.Host == "github.com" && url.AbsolutePath.StartsWith(publicAssetPrefix, StringComparison.OrdinalIgnoreCase);

        string prefix = releases.AbsolutePath + "assets/";
        return url.Host == releases.Host &&
            url.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(url.AbsolutePath.AsSpan(prefix.Length), out long id) && id > 0 &&
            url.AbsolutePath.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    internal async Task<HttpResponseMessage> GetAssetAsync(Uri asset, CancellationToken cancellationToken,
        long? manifestId = null)
    {
        if (!IsAsset(asset))
            throw new InvalidDataException("GitHub の添付ファイル URL が無効です。");

        // 同名の更新情報をアップロードし直したときは、新しい添付 ID でキャッシュを区別する。
        if (token is null && manifestId is > 0)
            asset = new UriBuilder(asset) { Query = "asset=" + manifestId.Value.ToString(CultureInfo.InvariantCulture) }.Uri;

        using var request = MakeRequest(asset, "application/octet-stream");
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is not (HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect or HttpStatusCode.SeeOther))
            return response;

        using (response)
        {
            Uri? location = response.Headers.Location;
            if (location is not { IsAbsoluteUri: true, Scheme: "https", IsDefaultPort: true } ||
                location.UserInfo.Length != 0 || location.Fragment.Length != 0 ||
                location.Host is not ("release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("GitHub の添付ファイルの転送先を確認できませんでした。");

            // 一時 URL の取得先には、GitHub API の認証ヘッダーを送らない。
            return await http.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
    }

    private HttpRequestMessage MakeRequest(Uri url, string mediaType)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("TakeTaiko-Updater/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (url.Host == releases.Host)
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }
}
