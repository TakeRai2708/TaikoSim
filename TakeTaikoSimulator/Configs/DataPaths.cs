namespace TakeTaikoSimulator.Configs;

public static class DataPaths
{
    public const string FolderName = "TakeTaikoSimulator";
    public static string AppFolder => AppContext.BaseDirectory;
    // データ(設定・曲・スキン・ログ)を置く場所
    //   Windows … exe の横(今までどおり)
    //   macOS   … 書類(~/Documents)の TakeTaiko。.app の中は書き換えられない(署名がこわれる)ので外に出す。曲を入れやすいように書類にした
    //   そのほか … exe の横
    public static string Root { get; } = OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TakeTaiko") + Path.DirectorySeparatorChar
        : AppFolder;
    public static string Data(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    private static readonly string[] LegacyFolders = ["Config", "Songs", "Skins", "Cache", "Logs", "Screenshots", "HitSounds"];

    public static void Prepare()
    {
        if (!string.Equals(Path.GetFullPath(Root), Path.GetFullPath(AppFolder), StringComparison.OrdinalIgnoreCase))
            CopyBundled();
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

    // データの場所が exe の横でないとき(macOS)：アプリに入れて配った Simple スキンと音色を、無ければデータの場所へコピーする
    //   上書きはしない(自分で変えたものを消さないように)。新しい版のスキンは、あとで更新のしくみで届ける
    private static readonly string[] BundledFolders = [Path.Combine("Skins", "TaikoSim-Simple"), "HitSounds"];

    private static void CopyBundled()
    {
        try
        {
            Directory.CreateDirectory(Root);
            foreach (string name in BundledFolders)
            {
                string source = Path.Combine(AppFolder, name);
                string target = Path.Combine(Root, name);
                if (!Directory.Exists(source) || Directory.Exists(target)) continue;
                CopyFolder(source, target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[データ] アプリのスキン・音色をコピーできませんでした: {ex.Message}");
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
