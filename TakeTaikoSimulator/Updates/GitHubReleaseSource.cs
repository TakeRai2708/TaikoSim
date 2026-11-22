using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TakeTaikoSimulator.Updates;

internal sealed class GitHubReleaseSource
{
    private readonly HttpClient http;
    private readonly Uri releases;
    private readonly string token;

    internal GitHubReleaseSource(HttpClient http, string? repository, string? accessToken = null)
    {
        string[] parts = (repository ?? "").Split('/');
        if (parts.Length != 2 || parts.Any(part => part.Length == 0 || part is "." or ".." ||
            part.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))))
            throw new InvalidDataException("GitHub のリポジトリは owner/repository 形式で指定してください。");

        this.http = http;
        releases = new Uri($"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases/");
        token = (accessToken ?? Environment.GetEnvironmentVariable("TAKETAIKO_GITHUB_TOKEN"))?.Trim() ?? "";
        if (token.Length == 0)
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
        string prefix = releases.AbsolutePath + "assets/";
        return url.Scheme == "https" && url.Host == releases.Host && url.IsDefaultPort &&
            url.UserInfo.Length == 0 && url.Query.Length == 0 && url.Fragment.Length == 0 &&
            url.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(url.AbsolutePath.AsSpan(prefix.Length), out long id) && id > 0 &&
            url.AbsolutePath.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    internal async Task<HttpResponseMessage> GetAssetAsync(Uri asset, CancellationToken cancellationToken)
    {
        if (!IsAsset(asset))
            throw new InvalidDataException("GitHub の添付ファイル URL が無効です。");

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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }
}
