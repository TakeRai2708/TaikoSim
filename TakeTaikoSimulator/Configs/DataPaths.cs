namespace TakeTaikoSimulator.Configs;

public static class DataPaths
{
    public const string FolderName = "TakeTaikoSimulator";
    public static string AppFolder => AppContext.BaseDirectory;
    public static string Root => AppFolder;
    public static string Data(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    private static readonly string[] LegacyFolders = ["Config", "Songs", "Skins", "Cache", "Logs", "Screenshots", "HitSounds"];

    public static void Prepare()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(documents))
            ImportLegacy(Path.Combine(documents, FolderName), AppFolder);
    }

    internal static void ImportLegacy(string source, string destination)
    {
        const string markerName = ".documents-data-imported";
        string marker = Path.Combine(destination, markerName);
        if (File.Exists(marker) || !Directory.Exists(source) || string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)), StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            foreach (string name in LegacyFolders)
            {
                string oldFolder = Path.Combine(source, name);
                string target = Path.Combine(destination, name);
                if (!Directory.Exists(oldFolder) || Directory.Exists(target) || File.Exists(target))
                    continue;
                if ((File.GetAttributes(oldFolder) & FileAttributes.ReparsePoint) != 0)
                    continue;

                // コピー途中のフォルダーを通常のデータとして読み込まない。
                string staged = Path.Combine(destination, ".take-taiko-import-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staged);
                try
                {
                    CopyFolder(oldFolder, staged);
                    Directory.Move(staged, target);
                }
                finally
                {
                    if (Directory.Exists(staged))
                        Directory.Delete(staged, recursive: true);
                }
            }
            File.WriteAllText(marker, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[データ] ドキュメントのデータをコピーできませんでした: {ex.Message}");
        }
    }

    private static void CopyFolder(string source, string destination)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        foreach (string file in Directory.EnumerateFiles(source, "*", options))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
