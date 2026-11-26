using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TakeTaikoSimulator.Charts;

namespace TakeTaikoSimulator.Online;

// オンラインの「曲の配布」の中身（通信のしかたには関係しない部分）
//   11/26 たけらい：曲はホストのものを使う。持っていない人には、ホストから配ってダウンロードする。もらった曲は残す
//   ・SongShare.Create  … ホスト：曲(.tja と、そこに書いてある音源・画像・動画)の一覧と指紋を作る
//   ・SongPackageReader … ホスト：送るデータを小さく分けて取り出す
//   ・SongReceiver      … もらう人：小分けのデータを受け取って、確かめてから曲フォルダに置く
//   ・SongLibrary       … 自分の曲の中から同じ曲を探す・置き場所(ジャンル)を決める
//   通信(EOS)は、ここで作った「マニフェスト(JSON)」と「小分けのデータ」を運ぶだけ。docs/online-protocol.md も見てね

// 曲の中の1ファイル（Path は .tja のフォルダからの場所。区切りは「/」）
public sealed record SongFile(string Path, long Size, string Sha256);

// ホストの曲が入っているジャンル（box.def の #TITLE と #GENRE）。null なら「ジャンルなし」
public sealed record SongGenre(string Title, string Genre);

// 配る曲の情報（最初にこれを送る）
public sealed class SongManifest
{
    public const int CurrentFormat = 1;

    public int Format { get; init; } = CurrentFormat;
    public string Identity { get; init; } = "";       // 曲の指紋(.tja と音源の中身から作る)。同じなら「同じ曲を持っている」
    public string Title { get; init; } = "";          // 画面に出す名前(.tja の TITLE)
    public string FolderName { get; init; } = "";     // ホストでの曲フォルダの名前(もらった人のフォルダ名に使う)
    public SongGenre? Genre { get; init; }
    public List<SongFile> Files { get; init; } = new();   // 1つ目は必ず .tja

    public long TotalSize => Files.Sum(f => f.Size);
    public int ChunkCount => (int)((TotalSize + SongShare.ChunkSize - 1) / SongShare.ChunkSize);

    public byte[] ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("format", Format);
            w.WriteString("identity", Identity);
            w.WriteString("title", Title);
            w.WriteString("folderName", FolderName);
            if (Genre != null)
            {
                w.WriteStartObject("genre");
                w.WriteString("title", Genre.Title);
                w.WriteString("genre", Genre.Genre);
                w.WriteEndObject();
            }
            w.WriteStartArray("files");
            foreach (SongFile file in Files)
            {
                w.WriteStartObject();
                w.WriteString("path", file.Path);
                w.WriteNumber("size", file.Size);
                w.WriteString("sha256", file.Sha256);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    // 読めないとき・決まりに合わないときは InvalidDataException
    public static SongManifest FromJson(ReadOnlySpan<byte> json)
    {
        if (json.Length > SongShare.MaxManifestBytes) throw new InvalidDataException("曲の情報が大きすぎます");
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json.ToArray());
            JsonElement root = doc.RootElement;
            SongGenre? genre = null;
            if (root.TryGetProperty("genre", out JsonElement g) && g.ValueKind == JsonValueKind.Object)
                genre = new SongGenre(g.GetProperty("title").GetString() ?? "", g.GetProperty("genre").GetString() ?? "");
            var files = new List<SongFile>();
            foreach (JsonElement f in root.GetProperty("files").EnumerateArray())
                files.Add(new SongFile(f.GetProperty("path").GetString() ?? "", f.GetProperty("size").GetInt64(), f.GetProperty("sha256").GetString() ?? ""));
            var manifest = new SongManifest
            {
                Format = root.GetProperty("format").GetInt32(),
                Identity = root.GetProperty("identity").GetString() ?? "",
                Title = root.GetProperty("title").GetString() ?? "",
                FolderName = root.GetProperty("folderName").GetString() ?? "",
                Genre = genre,
                Files = files,
            };
            SongShare.Validate(manifest);
            return manifest;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"曲の情報を読めませんでした：{ex.Message}");
        }
    }
}

public static class SongShare
{
    // 1回に送る大きさ。EOS の P2P は1つの包みが 1170 バイトまでなので、見出し(種類・番号など)の分を残して 1100
    public const int ChunkSize = 1100;
    public const long MaxTotalBytes = 300L * 1024 * 1024;   // 1曲 300MB まで
    public const int MaxFiles = 16;
    public const int MaxManifestBytes = 64 * 1024;
    public const int MaxPathLength = 200;
    private static readonly char[] WindowsInvalid = ['<', '>', ':', '"', '|', '?', '*'];

    // 送ってよいファイルの種類（ほかの種類は受け取らない）
    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tja",
        ".ogg", ".mp3", ".wav", ".flac", ".opus", ".m4a",
        ".png", ".jpg", ".jpeg", ".bmp", ".webp",
        ".mp4", ".webm", ".avi", ".mkv", ".wmv", ".mov",
        ".lrc", ".vtt",
    };

    // .tja に書いてあって、一緒に送るファイル(値はファイルの場所)
    private static readonly string[] AudioKeys = ["WAVE"];
    private static readonly string[] ImageKeys = ["BGIMAGE", "PREIMAGE", "LYRICS"];
    private static readonly string[] MovieKeys = ["BGMOVIE"];

    // ホスト：配る曲の情報を作る
    //   includeMovie … 背景動画も送るか(ルームの設定「背景映像を配布する」)
    public static SongManifest Create(string tjaPath, SongGenre? genre, bool includeMovie)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(tjaPath)) ?? throw new InvalidDataException(".tja の場所が分かりません");
        Dictionary<string, string> headers = ReadHeaders(tjaPath);

        var paths = new List<string> { Path.GetFileName(tjaPath) };
        string? wave = First(headers, AudioKeys);
        if (wave == null) throw new InvalidDataException(".tja に WAVE(音源)が書いてありません");
        AddReferenced(folder, wave, paths, required: true);
        foreach (string key in ImageKeys) if (headers.TryGetValue(key, out string? v)) AddReferenced(folder, v, paths, required: false);
        if (includeMovie) foreach (string key in MovieKeys) if (headers.TryGetValue(key, out string? v)) AddReferenced(folder, v, paths, required: false);

        var files = new List<SongFile>();
        foreach (string relative in paths)
        {
            string full = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
            files.Add(new SongFile(relative, new FileInfo(full).Length, HashFile(full)));
        }
        var manifest = new SongManifest
        {
            Identity = MakeIdentity(files[0].Sha256, files[1].Sha256),
            Title = headers.TryGetValue("TITLE", out string? title) && title.Length > 0 ? title : Path.GetFileNameWithoutExtension(tjaPath),
            FolderName = Path.GetFileName(folder),
            Genre = genre,
            Files = files,
        };
        Validate(manifest);
        return manifest;
    }

    // 曲の指紋：.tja と音源の中身(SHA-256)から作る。画像・動画は入れない(動画を送らない設定でも同じ曲として扱うため)
    public static string MakeIdentity(string tjaSha256, string audioSha256) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"taketaiko-song/1|tja:{tjaSha256}|audio:{audioSha256}"))).ToLowerInvariant();

    // 自分の .tja の指紋(音源が無ければ null)
    public static string? IdentityOf(string tjaPath)
    {
        try
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(tjaPath))!;
            string? wave = First(ReadHeaders(tjaPath), AudioKeys);
            if (wave == null || !IsSafeRelativePath(Normalize(wave))) return null;
            string audio = Path.Combine(folder, Normalize(wave).Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(audio)) return null;
            return MakeIdentity(HashFile(tjaPath), HashFile(audio));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    // マニフェストの決まりを確かめる(もらう側で必ず呼ぶ)。合わなければ InvalidDataException
    public static void Validate(SongManifest manifest)
    {
        if (manifest.Format != SongManifest.CurrentFormat) throw new InvalidDataException($"曲の情報の形式が違います({manifest.Format})。ゲームのバージョンをそろえてください");
        if (manifest.Identity.Length != 64) throw new InvalidDataException("曲の指紋がおかしいです");
        if (manifest.Files.Count < 2 || manifest.Files.Count > MaxFiles) throw new InvalidDataException("ファイルの数がおかしいです");
        if (!manifest.Files[0].Path.EndsWith(".tja", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("1つ目が .tja ではありません");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (SongFile file in manifest.Files)
        {
            if (!IsSafeRelativePath(file.Path)) throw new InvalidDataException($"ファイルの場所が危ないので受け取りません：{file.Path}");
            if (!AllowedExtensions.Contains(Path.GetExtension(file.Path))) throw new InvalidDataException($"この種類のファイルは受け取りません：{file.Path}");
            if (!seen.Add(file.Path)) throw new InvalidDataException($"同じファイルが2回あります：{file.Path}");
            if (file.Size < 0 || file.Sha256.Length != 64) throw new InvalidDataException($"ファイルの情報がおかしいです：{file.Path}");
            total += file.Size;
            if (total > MaxTotalBytes) throw new InvalidDataException($"曲が大きすぎます（{MaxTotalBytes / 1024 / 1024}MB まで）");
        }
        if (manifest.Identity != MakeIdentity(manifest.Files[0].Sha256, manifest.Files[1].Sha256)) throw new InvalidDataException("曲の指紋が中身と合いません");
    }

    // 「a/b.ogg」のような、フォルダの外に出ない場所か
    public static bool IsSafeRelativePath(string path)
    {
        if (path.Length == 0 || path.Length > MaxPathLength) return false;
        if (path.Contains('\\') || path.Contains(':') || path.Contains('\0') || path.StartsWith('/')) return false;
        foreach (string part in path.Split('/'))
        {
            if (part.Length == 0 || part == "." || part == "..") return false;
            if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            if (part.IndexOfAny(WindowsInvalid) >= 0) return false;   // Mac・Linux のホストからでも、Windows で作れない名前ははじく
            if (part.EndsWith(' ') || part.EndsWith('.')) return false;   // Windows で別の名前になってしまう
        }
        return true;
    }

    public static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    // .tja の見出し(「KEY:値」)を読む。同じ KEY は最初のものを使う
    private static Dictionary<string, string> ReadHeaders(string tjaPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in ChartText.Read(tjaPath).Split('\n'))
        {
            string line = raw.Trim();
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line[..comment].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            result.TryAdd(key, value);
        }
        return result;
    }

    private static string? First(Dictionary<string, string> headers, string[] keys)
    {
        foreach (string key in keys) if (headers.TryGetValue(key, out string? v) && v.Length > 0) return v;
        return null;
    }

    private static string Normalize(string value) => value.Trim().Trim('"').Replace('\\', '/');

    private static void AddReferenced(string folder, string value, List<string> paths, bool required)
    {
        string relative = Normalize(value);
        if (relative.Length == 0) return;
        if (!IsSafeRelativePath(relative))
        {
            if (required) throw new InvalidDataException($"音源の場所が .tja のフォルダの外なので配れません：{value}");
            return;
        }
        string full = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
        {
            if (required) throw new InvalidDataException($"音源が見つかりません：{relative}");
            return;
        }
        if (!AllowedExtensions.Contains(Path.GetExtension(relative))) return;
        if (!paths.Contains(relative, StringComparer.OrdinalIgnoreCase)) paths.Add(relative);
    }
}

// ホスト：送るデータ(全部のファイルを順番につなげたもの)を、小分けで取り出す
public sealed class SongPackageReader : IDisposable
{
    private readonly SongManifest manifest;
    private readonly FileStream[] streams;
    private readonly long[] starts;

    public SongPackageReader(string tjaPath, SongManifest manifest)
    {
        this.manifest = manifest;
        string folder = Path.GetDirectoryName(Path.GetFullPath(tjaPath))!;
        streams = new FileStream[manifest.Files.Count];
        starts = new long[manifest.Files.Count];
        long position = 0;
        try
        {
            for (int i = 0; i < manifest.Files.Count; i++)
            {
                starts[i] = position;
                position += manifest.Files[i].Size;
                streams[i] = File.OpenRead(Path.Combine(folder, manifest.Files[i].Path.Replace('/', Path.DirectorySeparatorChar)));
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    // index 番目の小分け(最後以外は ChunkSize バイト)
    public byte[] ReadChunk(int index)
    {
        if (index < 0 || index >= manifest.ChunkCount) throw new ArgumentOutOfRangeException(nameof(index));
        long begin = (long)index * SongShare.ChunkSize;
        int length = (int)Math.Min(SongShare.ChunkSize, manifest.TotalSize - begin);
        byte[] buffer = new byte[length];
        int written = 0;
        for (int i = 0; i < streams.Length && written < length; i++)
        {
            long fileEnd = starts[i] + manifest.Files[i].Size;
            long at = begin + written;
            if (at >= fileEnd) continue;
            streams[i].Position = at - starts[i];
            int want = (int)Math.Min(length - written, fileEnd - at);
            streams[i].ReadExactly(buffer, written, want);
            written += want;
        }
        return buffer;
    }

    public void Dispose()
    {
        foreach (FileStream? stream in streams) stream?.Dispose();
    }
}

// もらう人：小分けのデータを受け取って、そろったら確かめて曲フォルダに置く
//   受け取り中は一時フォルダ(Cache/Online/受け取り中)に1つのファイルとしてためる。途中でやめたら消す
public sealed class SongReceiver : IDisposable
{
    public SongManifest Manifest { get; }
    private readonly string workFolder;
    private readonly FileStream blob;
    private readonly bool[] received;
    private int receivedCount;

    public SongReceiver(SongManifest manifest, string tempRoot)
    {
        SongShare.Validate(manifest);
        Manifest = manifest;
        workFolder = Path.Combine(tempRoot, manifest.Identity);
        if (Directory.Exists(workFolder)) Directory.Delete(workFolder, recursive: true);
        Directory.CreateDirectory(workFolder);
        blob = new FileStream(Path.Combine(workFolder, "data.bin"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        blob.SetLength(manifest.TotalSize);
        received = new bool[manifest.ChunkCount];
    }

    public bool IsComplete => receivedCount == received.Length;
    public double Progress => received.Length == 0 ? 1 : (double)receivedCount / received.Length;

    // 受け取った小分けを書く。番号・大きさがおかしいときは false(捨てる)。同じ番号が2回来ても大丈夫
    public bool Accept(int index, ReadOnlySpan<byte> data)
    {
        if (index < 0 || index >= received.Length) return false;
        long begin = (long)index * SongShare.ChunkSize;
        int expected = (int)Math.Min(SongShare.ChunkSize, Manifest.TotalSize - begin);
        if (data.Length != expected) return false;
        if (received[index]) return true;
        blob.Position = begin;
        blob.Write(data);
        received[index] = true;
        receivedCount++;
        return true;
    }

    // まだ来ていない番号(送り直してもらう用)。多すぎるときは先頭から limit 個
    public List<int> Missing(int limit = 256)
    {
        var list = new List<int>();
        for (int i = 0; i < received.Length && list.Count < limit; i++) if (!received[i]) list.Add(i);
        return list;
    }

    // 全部そろったら呼ぶ。中身を確かめて、曲フォルダ(ジャンルのフォルダ)に置く。置いた .tja の場所を返す
    //   確かめて合わなければ InvalidDataException(何も置かない)
    public string Finish(string songsRoot)
    {
        if (!IsComplete) throw new InvalidOperationException("まだ全部届いていません");
        blob.Flush();

        // 1. ファイルに分けながら、中身の指紋を確かめる
        string staging = Path.Combine(workFolder, "files");
        Directory.CreateDirectory(staging);
        blob.Position = 0;
        byte[] buffer = new byte[81920];
        foreach (SongFile file in Manifest.Files)
        {
            string target = Path.Combine(staging, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                long left = file.Size;
                while (left > 0)
                {
                    int n = blob.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                    if (n <= 0) throw new InvalidDataException("データが途中で切れています");
                    output.Write(buffer, 0, n);
                    hash.AppendData(buffer, 0, n);
                    left -= n;
                }
                string actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (actual != file.Sha256) throw new InvalidDataException($"届いたデータが壊れています：{file.Path}");
            }
        }

        // 2. 置き場所(ホストと同じジャンル。無ければ「未分類」)に、かぶらない名前のフォルダを作って移す
        string genreFolder = SongLibrary.ResolveGenreFolder(songsRoot, Manifest.Genre);
        Directory.CreateDirectory(genreFolder);
        string name = SongLibrary.SafeFolderName(Manifest.FolderName.Length > 0 ? Manifest.FolderName : Manifest.Title);
        string destination = SongLibrary.UniqueFolder(genreFolder, name);
        Directory.Move(staging, destination);
        return Path.Combine(destination, Manifest.Files[0].Path.Replace('/', Path.DirectorySeparatorChar));
    }

    public void Dispose()
    {
        blob.Dispose();
        try { if (Directory.Exists(workFolder)) Directory.Delete(workFolder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

// 自分の曲フォルダのこと
public static class SongLibrary
{
    // box.def の無い所(＝選曲画面の「未分類」)に置くときのフォルダ名
    public const string UncategorizedFolder = "未分類";
    private const int MaxSearchDepth = 6;

    // もらった曲を置くフォルダ
    //   11/26 たけらい「保存先は box.def のジャンルを参照してそこに入れてほしい。なかったら未分類で」
    //   1. ホストの #GENRE と同じ #GENRE の box.def があるフォルダ
    //   2. 無ければ、ホストの #TITLE と同じ #TITLE(無ければフォルダ名)の box.def があるフォルダ
    //   3. それも無ければ Songs/未分類（box.def は作らない＝選曲画面では「未分類」に入る）
    //   同じ条件のフォルダがいくつもあるときは、場所の名前順で最初のもの
    public static string ResolveGenreFolder(string songsRoot, SongGenre? genre)
    {
        string fallback = Path.Combine(songsRoot, UncategorizedFolder);
        if (genre == null || (genre.Genre.Length == 0 && genre.Title.Length == 0)) return fallback;

        List<(string Folder, string Title, string Genre)> boxes = FindBoxes(songsRoot);
        if (genre.Genre.Length > 0)
            foreach (var box in boxes)
                if (string.Equals(box.Genre, genre.Genre, StringComparison.OrdinalIgnoreCase)) return box.Folder;
        if (genre.Title.Length > 0)
            foreach (var box in boxes)
                if (string.Equals(box.Title, genre.Title, StringComparison.OrdinalIgnoreCase)) return box.Folder;
        return fallback;
    }

    // ホスト：曲の .tja から、入っているジャンル(いちばん近い親の box.def)を調べる。無ければ null
    public static SongGenre? GenreOf(string songsRoot, string tjaPath)
    {
        string root = Path.GetFullPath(songsRoot).TrimEnd(Path.DirectorySeparatorChar);
        string? folder = Path.GetDirectoryName(Path.GetFullPath(tjaPath));
        SongGenre? found = null;
        // 選曲画面と同じく「外側の box.def」が優先(中の box.def は使わない)なので、Songs に近いほうまで見る
        while (folder != null && folder.Length > root.Length && folder.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            string boxDef = Path.Combine(folder, "box.def");
            if (File.Exists(boxDef)) found = ReadBox(boxDef, Path.GetFileName(folder));
            folder = Path.GetDirectoryName(folder);
        }
        return found;
    }

    // 指紋が同じ曲を、自分の曲から探す(見つからなければ null)
    //   全部の曲の指紋を計算すると重いので、まず .tja のファイル名か TITLE が同じものだけ調べる
    public static string? FindLocal(string songsRoot, SongManifest manifest)
    {
        if (!Directory.Exists(songsRoot)) return null;
        string tjaName = Path.GetFileName(manifest.Files[0].Path);
        foreach (string tja in Directory.EnumerateFiles(songsRoot, "*.tja", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
        {
            if (!string.Equals(Path.GetFileName(tja), tjaName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileName(Path.GetDirectoryName(tja)), manifest.FolderName, StringComparison.OrdinalIgnoreCase)) continue;
            if (new FileInfo(tja).Length != manifest.Files[0].Size) continue;
            if (SongShare.IdentityOf(tja) == manifest.Identity) return tja;
        }
        return null;
    }

    // フォルダ名に使えない文字を「_」にする
    public static string SafeFolderName(string name)
    {
        var builder = new StringBuilder();
        foreach (char c in name.Trim())
            builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c);
        string result = builder.ToString().Trim().TrimEnd('.');
        if (result.Length == 0 || result == "." || result == "..") result = "song";
        return result.Length > 80 ? result[..80] : result;
    }

    // 同じ名前のフォルダがあれば「名前 (2)」「名前 (3)」…
    public static string UniqueFolder(string parent, string name)
    {
        string candidate = Path.Combine(parent, name);
        for (int n = 2; Directory.Exists(candidate) || File.Exists(candidate); n++)
            candidate = Path.Combine(parent, $"{name} ({n})");
        return candidate;
    }

    private static List<(string Folder, string Title, string Genre)> FindBoxes(string songsRoot)
    {
        var result = new List<(string, string, string)>();
        if (!Directory.Exists(songsRoot)) return result;
        var pending = new Stack<(string Folder, int Depth)>();
        pending.Push((songsRoot, 0));
        while (pending.Count > 0)
        {
            (string folder, int depth) = pending.Pop();
            string[] children;
            try { children = Directory.GetDirectories(folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (string child in children)
            {
                string boxDef = Path.Combine(child, "box.def");
                if (File.Exists(boxDef))
                {
                    SongGenre box = ReadBox(boxDef, Path.GetFileName(child));
                    result.Add((child, box.Title, box.Genre));
                    continue;   // box.def の中の box.def は使わない(選曲画面と同じ)
                }
                if (depth + 1 < MaxSearchDepth) pending.Push((child, depth + 1));
            }
        }
        result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Item1, b.Item1));
        return result;
    }

    private static SongGenre ReadBox(string boxDef, string folderName)
    {
        string title = folderName, genre = "";
        try
        {
            foreach (string raw in ChartText.Read(boxDef).Split('\n'))
            {
                string line = raw.Trim();
                int colon = line.IndexOf(':');
                if (!line.StartsWith('#') || colon <= 0) continue;
                string key = line[..colon].Trim().ToUpperInvariant();
                string value = line[(colon + 1)..].Trim();
                if (key == "#TITLE" && value.Length > 0) title = value;
                else if (key == "#GENRE") genre = value;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return new SongGenre(title, genre);
    }
}
