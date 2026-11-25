using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace TakeTaikoSimulator.Updates;

internal sealed class InviteReleaseSource
{
    private readonly HttpClient http;
    private readonly Uri relay;
    private readonly string code;

    internal InviteReleaseSource(HttpClient http, Uri relay, string? code)
    {
        if (!relay.IsAbsoluteUri || relay.Scheme != Uri.UriSchemeHttps || relay.UserInfo.Length != 0 ||
            relay.Query.Length != 0 || relay.Fragment.Length != 0 ||
            code is null || code.Length != 64 || !code.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("招待の配布設定が無効です。");
        this.http = http;
        this.relay = new Uri(relay.AbsoluteUri.TrimEnd('/') + "/");
        this.code = code;
    }

    internal Task<HttpResponseMessage> ReadManifestAsync(CancellationToken token) => GetAsync("latest.json", token);

    internal Task<HttpResponseMessage> DownloadAsync(Uri asset, CancellationToken token)
    {
        string[] pieces = asset.AbsolutePath.Split('/');
        if (asset.Host != "api.github.com" || !asset.IsDefaultPort || asset.Query.Length != 0 ||
            pieces.Length != 7 || pieces[1] != "repos" || pieces[4] != "releases" || pieces[5] != "assets" ||
            !long.TryParse(pieces[6], out long id) || id <= 0 ||
            !pieces[6].All(char.IsAsciiDigit))
            throw new InvalidDataException("署名付き更新情報の ZIP は GitHub 添付ファイルである必要があります。");
        return GetAsync("packages/" + pieces[6], token);
    }

    private async Task<HttpResponseMessage> GetAsync(string path, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(relay, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", code);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            response.Dispose();
            throw new InvalidDataException("この招待では配布サーバーにアクセスできません。");
        }
        return response;
    }
}
