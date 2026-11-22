using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TakeTaikoSimulator.Updates;

var builder = WebApplication.CreateBuilder(args);
string Require(string key) => Environment.GetEnvironmentVariable(key) is { Length: > 0 } value
    ? value : throw new InvalidOperationException($"Missing environment variable {key}.");

string allowlist = Path.GetFullPath(Require("TAKETAIKO_RELAY_INVITES"));
string? tag = Environment.GetEnvironmentVariable("TAKETAIKO_RELAY_TAG");
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
{
    Timeout = Timeout.InfiniteTimeSpan
};
var github = new GitHubReleaseSource(http, Require("TAKETAIKO_RELAY_REPOSITORY"),
    Require("TAKETAIKO_RELAY_GITHUB_TOKEN"));
var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "private, no-store";
    string header = context.Request.Headers.Authorization.ToString();
    const string scheme = "Bearer ";
    if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase) || header.Length != scheme.Length + 64 ||
        !header[scheme.Length..].All(char.IsAsciiHexDigit))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    try
    {
        // ファイルをリクエストのたびに読み、招待をサーバー再起動なしで失効できるようにする。
        string[] hashes = await File.ReadAllLinesAsync(allowlist, context.RequestAborted);
        byte[] candidate = SHA256.HashData(Encoding.UTF8.GetBytes(header[scheme.Length..]));
        bool accepted = false;
        foreach (string line in hashes)
        {
            string hash = line.Trim();
            if (hash.Length == 64 && hash.All(char.IsAsciiHexDigit))
                accepted |= CryptographicOperations.FixedTimeEquals(candidate, Convert.FromHexString(hash));
        }
        if (!accepted)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return;
    }

    await next(context);
});

app.MapGet("/latest.json", (HttpContext context) => ServeAsync(context, null));
app.MapGet("/packages/{id:long}", (HttpContext context, long id) => ServeAsync(context, id));
await app.RunAsync();

async Task ServeAsync(HttpContext context, long? id)
{
    using var expiry = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    expiry.CancelAfter(TimeSpan.FromMinutes(5));
    CancellationToken token = expiry.Token;
    try
    {
        using var release = await github.ReadReleaseAsync(tag, token);
        if (release.RootElement.GetProperty("draft").GetBoolean())
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var assets = release.RootElement.GetProperty("assets").EnumerateArray();
        JsonElement asset = assets.FirstOrDefault(item =>
            item.GetProperty("state").GetString() == "uploaded" &&
            (id is null ? item.GetProperty("name").GetString() == "latest.json"
                : item.GetProperty("id").GetInt64() == id &&
                  item.GetProperty("name").GetString()?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true));
        if (asset.ValueKind == JsonValueKind.Undefined)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        long length = asset.GetProperty("size").GetInt64();
        long limit = id is null ? UpdateFeed.MetadataLimit : 4L * 1024 * 1024 * 1024;
        if (length <= 0 || length > limit ||
            !Uri.TryCreate(asset.GetProperty("url").GetString(), UriKind.Absolute, out Uri? url) || !github.IsAsset(url))
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        using var upstream = await github.GetAssetAsync(url, token);
        if (!upstream.IsSuccessStatusCode ||
            upstream.Content.Headers.ContentLength is long actual && actual != length)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        context.Response.ContentType = id is null ? "application/json" : "application/zip";
        context.Response.ContentLength = length;
        await upstream.Content.CopyToAsync(context.Response.Body, token);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or
        JsonException or KeyNotFoundException or InvalidOperationException or OperationCanceledException)
    {
        if (context.Response.HasStarted)
            context.Abort();
        else
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
    }
}
