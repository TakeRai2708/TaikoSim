using System.Diagnostics;
using System.IO.Compression;

namespace TakeTaikoSimulator.Updates;

internal static class UpdatePackage
{
    internal const string GameExe = "TakeTaiko.exe";
    internal const string FeedFile = "update-feed.json";
    private static readonly HashSet<string> RuntimeFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        GameExe, "bass.dll", "bassmix.dll", "basswasapi.dll", "bassasio.dll",
        "raylib.dll", "raylib_angle.dll", "libEGL.dll", "libGLESv2.dll", "vulkan-1.dll",
        "THIRD-PARTY-NOTICES.txt", "Assets/Fonts/NotoSansJP-Regular.ttf",
        // 11/26：1.0.17 で ZIP に入れたら、1.0.16 までの更新が「配布対象外のファイル」で止まった。
        //   この版（1.0.18）から受け取れるようにして、PDF を ZIP に入れるのは次の版から
        "遊び方.pdf"
    };

    internal const string SimpleSkinPrefix = "Skins/TaikoSim-Simple/";
    internal const string SkinStageFolder = "skin";
    // Simple スキンの版。スキンの絵を変えたら1つ上げて、Skins/TaikoSim-Simple/skin-revision.txt も同じ数字にする
    //   起動したとき、手元のスキンの版がこれより古ければ「スキンを更新しますか？」を出す（StartupUpdate.OfferSimpleSkin）
    //   2：11/26 王冠・エントリーの板を足した
    internal const int SimpleSkinRevision = 3;
    internal const string SkinRevisionFile = "skin-revision.txt";

    internal static Version ReadVersion(string path)
    {
        var file = FileVersionInfo.GetVersionInfo(path);
        if (file.FileVersion is null)
            throw new InvalidDataException("ゲームのファイルバージョンを読み取れません。");
        return new(file.FileMajorPart, file.FileMinorPart, file.FileBuildPart, file.FilePrivatePart);
    }

    internal static void Extract(string archivePath, string stage, Version version, bool install, CancellationToken token)
    {
        string expanded = Path.Combine(stage, "expanded");
        Directory.CreateDirectory(expanded);
        using var zip = ZipFile.OpenRead(archivePath);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        if (zip.Entries.Count > 10000)
            throw new InvalidDataException("ZIP の項目が多すぎます。");

        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            string name = entry.FullName.Replace('\\', '/');
            bool directory = name.EndsWith('/');
            name = name.TrimEnd('/');
            if (!SafeName(name) || !names.Add(name))
                throw new InvalidDataException("ZIP に不正または重複したパスがあります。");
            if (directory)
                continue;

            bool bundled = name.StartsWith("HitSounds/", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Skins/TaikoSim-Simple/", StringComparison.OrdinalIgnoreCase);
            if (!RuntimeFiles.Contains(name) && !bundled)
                throw new InvalidDataException("ZIP に配布対象外のファイルがあります。");
            bool simpleSkin = name.StartsWith(SimpleSkinPrefix, StringComparison.OrdinalIgnoreCase);
            // 更新のときは音色（HitSounds）はさわらない。Simple スキンは別の場所（stage/skin）に出しておく
            //   ゲームの更新では入れかえない。起動したときに StartupUpdate.OfferSimpleSkin が版を見て、入れかえるかを聞く
            if (bundled && !install && !simpleSkin)
                continue;

            if (entry.Length < 0 || entry.Length > 2L * 1024 * 1024 * 1024 - total)
                throw new InvalidDataException("ZIP の展開サイズが上限を超えました。");
            total += entry.Length;
            string destination = Path.Combine(simpleSkin && !install ? Path.Combine(stage, SkinStageFolder) : expanded, name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = entry.Open();
            using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            byte[] buffer = new byte[64 * 1024];
            long remaining = entry.Length;
            int count;
            while ((count = source.Read(buffer)) != 0)
            {
                token.ThrowIfCancellationRequested();
                remaining -= count;
                if (remaining < 0)
                    throw new InvalidDataException("ZIP の展開サイズが一致しません。");
                target.Write(buffer, 0, count);
            }
            if (remaining != 0)
                throw new InvalidDataException("ZIP の展開サイズが一致しません。");
        }

        string executable = Path.Combine(expanded, GameExe);
        if (!File.Exists(executable) || ReadVersion(executable) != version)
            throw new InvalidDataException("ZIP 内のゲームの版が更新情報と一致しません。");
        if (install && (!File.Exists(Path.Combine(expanded, "raylib.dll")) ||
            !File.Exists(Path.Combine(expanded, "bass.dll")) ||
            !File.Exists(Path.Combine(expanded, "Assets", "Fonts", "NotoSansJP-Regular.ttf"))))
            throw new InvalidDataException("初回インストールには DLL とフォントを含む配布 ZIP が必要です。");
    }

    internal static void Apply(string root, string stage, bool install)
    {
        string expanded = Path.Combine(stage, "expanded");
        string backup = Path.Combine(stage, "backup");
        Directory.CreateDirectory(backup);
        var applied = new List<(string Target, string? Backup)>();
        var files = Directory.EnumerateFiles(expanded, "*", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetFileName(path).Equals(GameExe, StringComparison.OrdinalIgnoreCase)).ToArray();

        try
        {
            foreach (string source in files)
            {
                string relative = Path.GetRelativePath(expanded, source).Replace('\\', '/');
                if (!install && !RuntimeFiles.Contains(relative) && !relative.StartsWith(SimpleSkinPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新対象外のファイルです。");
                string destination = Path.Combine(root, relative);
                RejectLinks(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (File.Exists(destination))
                {
                    if (install)
                        throw new IOException("既存ファイルがあるためインストールできません。");
                    string previous = Path.Combine(backup, applied.Count.ToString());
                    File.Replace(source, destination, previous);
                    applied.Add((destination, previous));
                }
                else
                {
                    File.Move(source, destination);
                    applied.Add((destination, null));
                }
            }
        }
        catch
        {
            bool rollbackFailed = false;
            for (int index = applied.Count - 1; index >= 0; index--)
            {
                var (destination, previous) = applied[index];
                try
                {
                    if (previous is null)
                        File.Delete(destination);
                    else
                        File.Move(previous, destination, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    rollbackFailed = true;
                }
            }
            if (rollbackFailed)
                throw new RollbackFailedException();
            throw;
        }
    }

    internal static void RejectLinks(string root, string relative)
    {
        string path = root;
        foreach (string segment in relative.Split('/'))
        {
            path = Path.Combine(path, segment);
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("リンク先へのファイルの書き込みはできません。");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static bool SafeName(string name) => name.Split('/').All(segment =>
        segment.Length > 0 && !segment.EndsWith('.') && !segment.EndsWith(' ') &&
        segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !ReservedName(segment.Split('.')[0]));

    private static bool ReservedName(string name)
    {
        string upper = name.ToUpperInvariant();
        return upper is "CON" or "PRN" or "AUX" or "NUL" ||
            (upper.Length == 4 && (upper.StartsWith("COM") || upper.StartsWith("LPT")) &&
                ("123456789¹²³".Contains(upper[3])));
    }

    internal sealed class RollbackFailedException : IOException;
}
