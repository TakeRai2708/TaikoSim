namespace TakeTaikoSimulator.Configs;

// スキンの世代が変わったときだけ一覧を作る。Draw中にはディスクを探さない。
public static class CosmeticCatalog
{
    public static bool SafeId(string? id) => !string.IsNullOrWhiteSpace(id) && id != "." && id != ".."
        && id.IndexOfAny(new[] { '/', '\\', ':', '\0' }) < 0 && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    public static string[] Folders(string? root, string required)
    {
        if (root == null || !Directory.Exists(root)) return Array.Empty<string>();
        try { return Directory.EnumerateDirectories(root).Where(p => File.Exists(Path.Combine(p, required)))
            .Select(Path.GetFileName).OfType<string>().Where(SafeId).OrderBy(x => x, StringComparer.Ordinal).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
    public static string Choose(IReadOnlyList<string> ids, string? id, int index = 0) =>
        id != null && ids.Contains(id) ? id : ids.Count == 0 ? "" : ids[Math.Clamp(index, 0, ids.Count - 1)];
}
