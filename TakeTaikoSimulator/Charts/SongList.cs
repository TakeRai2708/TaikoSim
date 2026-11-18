using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Raylib_cs;
using TakeTaikoSimulator.Configs;

namespace TakeTaikoSimulator.Charts
{
    // 選曲画面に出す曲1つ分(.tja 1ファイル)
    public class SongEntry
    {
        public string TjaPath { get; init; } = "";

        // Songs フォルダから見た場所(例：「08 ナムコオリジナル\Angel Dream」)
        public string RelativeFolder { get; init; } = "";

        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";

        // 入っている難易度と★(かんたん → … → 段位 の順)
        public List<(CourseType Type, int Level)> Courses { get; init; } = new();

        // 読み込めなかったときの理由(読めたら空)
        public string Error { get; init; } = "";

        // .tja の更新日時(UTC の Ticks。「最近追加された曲」に使う。取れなかったら 0)
        public long WriteTime { get; init; }

        // Songs フォルダから見た .tja の場所(遊んだ曲・お気に入りの記録に使う)
        public string Key => Path.GetRelativePath(SongList.SongsFolder, TjaPath);

        public bool HasError => Error.Length > 0;

        // 検索用の文字(曲名・サブタイトル・フォルダ名をまとめて、検索しやすい形にしたもの)
        private string? searchKey;
        public string SearchKey => searchKey ??= SongSearch.Normalize($"{Title}\n{Subtitle}\n{RelativeFolder}");
    }

    // 検索のための文字の変換
    public static class SongSearch
    {
        // 検索しやすい形にそろえる
        //   ・全角/半角をそろえる(ＡＢＣ→ABC、ｶﾀｶﾅ→カタカナ)
        //   ・大文字/小文字をそろえる(ABC→abc)
        //   ・カタカナをひらがなにする(ドン→どん)
        public static string Normalize(string text)
        {
            string normalized = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();

            StringBuilder builder = new(normalized.Length);
            foreach (char c in normalized)
            {
                // ァ(U+30A1)〜ヶ(U+30F6) → ぁ〜ゖ(0x60 引くとひらがなになる)
                builder.Append(c >= '\u30A1' && c <= '\u30F6' ? (char)(c - 0x60) : c);
            }
            return builder.ToString();
        }

        public static bool MatchesFilters(SongEntry song, SongCategory category, string normalizedQuery,
            CourseType? course = null, int? stars = null, string? genre = null)
        {
            if (!Matches(song.SearchKey, normalizedQuery)) return false;
            string categoryGenre = string.IsNullOrWhiteSpace(category.Genre) ? category.Title : category.Genre;
            if (genre != null && !string.Equals(genre, categoryGenre, StringComparison.OrdinalIgnoreCase)) return false;
            return course == null && stars == null || song.Courses.Any(c => c.Type <= CourseType.Edit
                && (course == null || c.Type == course) && (stars == null || c.Level == stars));
        }

        // text の中に query が入っているか(どちらも Normalize したものを渡す)
        public static bool Matches(string normalizedText, string normalizedQuery)
        {
            return normalizedQuery.Length == 0 || normalizedText.Contains(normalizedQuery, StringComparison.Ordinal);
        }
    }

    // カテゴリ1つ分(box.def があるフォルダ)
    // ※ IsLoose が true のものは、どのカテゴリにも入らない曲の入れ物(画面にはカテゴリとして出さない)
    public class SongCategory
    {
        public string Title { get; init; } = "";
        public string Genre { get; init; } = "";
        public Color Color { get; init; } = SongList.UncategorizedColor;

        // box.def の #BOXTYPE・#BGTYPE(スキンの板・背景の番号。TaikøNauts / OpenTaiko の決まり)。書いてなければ null
        public int? BoxType { get; init; }
        public int? BgType { get; init; }

        // どのカテゴリにも入らない曲の入れ物か(選曲画面では、カテゴリの行を出さずに曲をそのまま並べる)
        // ※ 今は使っていない(Songs のすぐ下の曲は「その他」カテゴリに入る)。選曲画面の処理は残してある
        public bool IsLoose { get; init; } = false;
        public List<SongEntry> Songs { get; } = new();
    }

    // Songs フォルダの中の曲を探して、カテゴリごとに一覧にする係
    // カテゴリの決まり：
    //   ・Songs のすぐ下のフォルダ → 1つずつカテゴリ(box.def があってもなくても)。中の曲(奥のフォルダの曲も)はそこに入る
    //       名前：box.def の #TITLE。無ければフォルダ名
    //       色　：box.def の #BACKCOLOR → #BOXCOLOR → #GENRE の色 → 灰色
    //   ・Songs のすぐ下に直接置いた .tja → 「その他」カテゴリ(一番下)
    //   ・奥のフォルダの box.def は使わない(すぐ下のフォルダのカテゴリに入る)
    public static class SongList
    {
        // 曲を入れる場所(exe の横の Songs フォルダ)
        public static string SongsFolder => Configs.DataPaths.Data("Songs");

        // カテゴリの一覧(フォルダの名前順。「その他」があれば一番下)
        public static List<SongCategory> Categories { get; private set; } = new();

        // 曲の数(全部のカテゴリの合計)
        public static int SongCount => Categories.Sum(c => c.Songs.Count);

        // 一度でも読み終わったか
        public static bool IsScanned { get; private set; } = false;

        // ===== 読み込みの進み具合(読み込み画面・選曲画面に出す) =====

        // 今読み込み中か(StartScan してから、Update で結果を受け取るまで true)
        public static bool IsScanning { get; private set; } = false;

        // .tja の総数(-1 のときは、まだ数えている途中)
        public static int ScanTotal => Volatile.Read(ref scanTotal);

        // 読み終わった .tja の数
        public static int ScanDone => Volatile.Read(ref scanDone);

        // 読めなかった曲の数(最後に読み終わったときの数)
        public static int ErrorCount { get; private set; } = 0;

        // 裏で読んでいる係と、画面の係(メインの流れ)の間でやりとりする値
        // ※2つの流れから同時に触るので、Volatile / Interlocked で読み書きする
        private static int scanTotal = -1;
        private static int scanDone = 0;
        private static ScanResult? finished = null; // 裏の係が読み終わったら、ここに結果を置く

        private sealed record ScanResult(List<SongCategory> Categories, int ErrorCount);

        // ===== キャッシュ(読んだ結果の保存) =====

        // 保存する場所(exe の横の Cache フォルダ。消しても大丈夫。次の起動で全部読み直すだけ)
        public static string CacheFile => Configs.DataPaths.Data("Cache", "SongCache.json");

        // キャッシュの形の版。TjaParser の読み方を変えたら 1 増やす
        // (古いやり方で読んだ結果を使わないように。増やすと次の起動で全部読み直しになる)
        // 2：難易度ごとに LEVEL を読み直すようにした(譜面分岐の対応と一緒に)
        private const int CacheVersion = 2;

        // 読めなかったときの理由。これだけはキャッシュしない(次の起動でもう一度読んでみる)
        // ※「譜面がありません」の方は .tja の中身の問題なので、キャッシュする(.tja を直せば日時が変わって読み直しになる)
        private const string ReadFailedMessage = "読み込めませんでした";

        // Songs のすぐ下に直接置いた曲のカテゴリの名前
        public const string OthersTitle = "その他";

        // ジャンルが分からないカテゴリの色(灰色)
        public static readonly Color UncategorizedColor = new Color(90, 95, 110, 255);

        // ジャンルの色(OpenTaiko の初期値と同じ)
        private static readonly Dictionary<string, Color> GenreColors = new()
        {
            { "ポップス", Hex(0x01455B) }, { "J-POP", Hex(0x01455B) }, { "POPS", Hex(0x01455B) }, { "JPOP", Hex(0x01455B) },
            { "アニメ", Hex(0x99001F) },
            { "ボーカロイド", Hex(0x5B6278) }, { "VOCALOID", Hex(0x5B6278) },
            { "キッズ", Hex(0x9D3800) }, { "どうよう", Hex(0x9D3800) },
            { "バラエティ", Hex(0x366600) },
            { "クラシック", Hex(0x875600) },
            { "ゲームバラエティ", Hex(0x412080) }, { "ゲームミュージック", Hex(0x412080) },
            { "ナムコオリジナル", Hex(0x980E00) },
        };

        /// <summary>ジャンルの色（知らないジャンルは灰色）</summary>
        public static Color GenreColor(string genre) =>
            GenreColors.TryGetValue(genre, out Color color) ? color : UncategorizedColor;

        // ===== 探す =====

        // 読み込みを始める(裏で読むので、画面は止まらない)
        // 読み込み中にもう一度呼ばれても何もしない
        public static void StartScan()
        {
            if (IsScanning) return;

            IsScanning = true;
            Volatile.Write(ref scanTotal, -1);
            Volatile.Write(ref scanDone, 0);
            Volatile.Write(ref finished, null);

            Task.Run(ScanInBackground);
        }

        // 毎フレーム呼ぶ(Game から)。裏の読み込みが終わっていたら、新しい一覧に入れ替える
        // 入れ替えはここ(メインの流れ)でだけやるので、画面を描いている途中で一覧が変わることはない
        public static void Update()
        {
            if (!IsScanning) return;

            ScanResult? result = Volatile.Read(ref finished);
            if (result == null) return; // まだ読んでいる

            Volatile.Write(ref finished, null);
            Categories = result.Categories;
            ErrorCount = result.ErrorCount;
            IsScanned = true;
            IsScanning = false;
        }

        // 裏で動く係。何が起きても必ず結果を置く(置かないと、ずっと「読み込み中」のままになる)
        private static void ScanInBackground()
        {
            List<SongCategory> categories = new();
            int errors = 0;

            try
            {
                categories = Scan(out errors);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Songs] 読み込み中にエラーが出ました: {ex}");
            }

            Volatile.Write(ref scanTotal, Math.Max(0, ScanTotal)); // 数える前に失敗したときも 0 / 0 にする
            Volatile.Write(ref finished, new ScanResult(categories, errors));
        }

        // 全部読む(裏の係から呼ばれる)
        //   1. フォルダを一度ずつ列挙し、.tja の場所と更新情報を集める → 総数が決まる
        //   2. 1曲ずつ：キャッシュと「更新日時・大きさ」が同じならそのまま使う。違えば .tja を読み直す
        //   3. カテゴリに入れて、キャッシュを保存する
        private static List<SongCategory> Scan(out int errorCount)
        {
            Stopwatch watch = Stopwatch.StartNew();

            List<SongCategory> categories = new();
            SongCategory others = new SongCategory { Title = OthersTitle }; // Songs のすぐ下に直接置いた曲
            List<(SongCategory Category, string Tja, FileInfo Info)> found = new();
            int searchedFolders = 0;
            string root = SongsFolder;

            // ----- 1. .tja を探す -----
            try
            {
                Directory.CreateDirectory(root); // 無ければ作っておく

                // 1回の列挙でTJAと子フォルダを同時に取得する。
                List<DirectoryInfo> topFolders = new();
                ReadSongFolder(new DirectoryInfo(root),
                    file => found.Add((others, file.FullName, file)),
                    folder => topFolders.Add(folder), ref searchedFolders);
                topFolders.Sort((x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.FullName, y.FullName));
                foreach (DirectoryInfo folder in topFolders)
                {
                    SongCategory category = CreateCategory(folder.FullName);
                    categories.Add(category);
                    Walk(folder, category, found, ref searchedFolders);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"[Songs] 探せませんでした: {ex.Message}");
            }

            double searchMs = watch.Elapsed.TotalMilliseconds;
            Console.WriteLine($"[Songs] 探索：{searchedFolders}フォルダ・{found.Count}曲、{searchMs / 1000:F2}秒");
            Volatile.Write(ref scanTotal, found.Count);

            // ----- 2. 1曲ずつ読む(CPU のコアを使って何曲か同時に読む。1コアは画面用に残す) -----
            Dictionary<string, SongCacheItem> cache = LoadCache();
            SongEntry[] entries = new SongEntry[found.Count];
            SongCacheItem?[] newCache = new SongCacheItem?[found.Count];
            int cachedCount = 0;

            ParallelOptions options = new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
            Parallel.For(0, found.Count, options, i =>
            {
                entries[i] = LoadEntry(root, found[i].Tja, cache, out newCache[i], out bool fromCache, found[i].Info);
                if (fromCache) Interlocked.Increment(ref cachedCount);
                Interlocked.Increment(ref scanDone);
            });

            double loadMs = watch.Elapsed.TotalMilliseconds - searchMs;
            Console.WriteLine($"[Songs] 譜面情報の読み込み：{loadMs / 1000:F2}秒");
            // ----- 3. カテゴリに入れる(見つけた順＝フォルダの名前順) -----
            for (int i = 0; i < found.Count; i++)
            {
                found[i].Category.Songs.Add(entries[i]);
            }

            // 曲が入っていないカテゴリは出さない。「その他」は一番下
            categories.RemoveAll(c => c.Songs.Count == 0);
            if (others.Songs.Count > 0) categories.Add(others);

            // カテゴリの中の曲はフォルダの名前順
            foreach (SongCategory category in categories)
            {
                category.Songs.Sort((a, b) => string.Compare(a.TjaPath, b.TjaPath, StringComparison.OrdinalIgnoreCase));
            }

            SaveCache(newCache);

            errorCount = entries.Count(e => e.HasError);
            Console.WriteLine($"[Songs] {found.Count}曲を {watch.Elapsed.TotalSeconds:F2} 秒で読みました"
                + $"（キャッシュから {cachedCount}曲・読み直し {found.Count - cachedCount}曲・読めなかった曲 {errorCount}曲）");

            return categories;
        }

        // Songs のすぐ下のフォルダから、カテゴリを作る(box.def があれば読む。無ければフォルダ名・灰色)
        private static SongCategory CreateCategory(string folder)
        {
            string name = Path.GetFileName(folder);
            string boxDef = Path.Combine(folder, "box.def");
            if (File.Exists(boxDef)) return ReadBoxDef(boxDef, name);
            return new SongCategory { Title = name, Color = UncategorizedColor };
        }

        // 深いフォルダでも呼び出しを積み重ねずに探索する。曲の表示順は最後に一度だけ整える。
        private static void Walk(DirectoryInfo folder, SongCategory category,
            List<(SongCategory Category, string Tja, FileInfo Info)> found, ref int searchedFolders)
        {
            Stack<DirectoryInfo> pending = new();
            pending.Push(folder);
            while (pending.Count > 0)
            {
                DirectoryInfo current = pending.Pop();
                ReadSongFolder(current, file => found.Add((category, file.FullName, file)),
                    child => pending.Push(child), ref searchedFolders);
            }
        }

        // 名前と種類、サイズ、更新日時を同じ列挙結果から使う。
        // 権限のないフォルダで止まらず、ほかのフォルダの探索は続ける。
        private static void ReadSongFolder(DirectoryInfo folder, Action<FileInfo> addSong,
            Action<DirectoryInfo> addFolder, ref int searchedFolders)
        {
            searchedFolders++;
            try
            {
                foreach (FileSystemInfo entry in folder.EnumerateFileSystemInfos())
                {
                    if (entry is DirectoryInfo child) addFolder(child);
                    else if (entry is FileInfo file &&
                        string.Equals(file.Extension, ".tja", StringComparison.OrdinalIgnoreCase)) addSong(file);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"[Songs] {folder.FullName} を開けませんでした: {ex.Message}");
            }
        }

        // box.def を読む(#TITLE・#GENRE・#BACKCOLOR・#BOXCOLOR・#BOXTYPE・#BGTYPE だけ使う)
        private static SongCategory ReadBoxDef(string path, string folderName)
        {
            string title = folderName; // #TITLE が無いときはフォルダ名
            string genre = "";
            Color? backColor = null;
            Color? boxColor = null;
            int? boxType = null, bgType = null;

            try
            {
                foreach (string rawLine in ReadText(path).Split('\n'))
                {
                    string line = rawLine.Trim();
                    if (!line.StartsWith('#')) continue;

                    int colon = line.IndexOf(':');
                    if (colon <= 0) continue;

                    string key = line[..colon].Trim().ToUpperInvariant();
                    string value = line[(colon + 1)..].Trim();

                    switch (key)
                    {
                        case "#TITLE": if (value.Length > 0) title = value; break;
                        case "#GENRE": genre = value; break;
                        case "#BACKCOLOR": backColor = ParseColor(value); break;
                        case "#BOXCOLOR": boxColor = ParseColor(value); break;
                        case "#BOXTYPE": if (int.TryParse(value, out int box)) boxType = box; break;
                        case "#BGTYPE": if (int.TryParse(value, out int bg)) bgType = bg; break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"[Songs] box.def を読めませんでした: {ex.Message}");
            }

            // 色：#BACKCOLOR → #BOXCOLOR → ジャンルの色 → 灰色
            Color color = backColor ?? boxColor
                ?? (GenreColors.TryGetValue(genre, out Color genreColor) ? genreColor : UncategorizedColor);

            return new SongCategory { Title = title, Genre = genre, Color = color, BoxType = boxType, BgType = bgType };
        }

        // 1曲分を用意する。キャッシュが使えればキャッシュから、使えなければ .tja を読む(裏の係から何曲か同時に呼ばれる)
        //   newItem   … 次に保存するキャッシュ(保存しないときは null)
        //   fromCache … キャッシュから作ったか
        private static SongEntry LoadEntry(string root, string tja, Dictionary<string, SongCacheItem> cache,
            out SongCacheItem? newItem, out bool fromCache, FileInfo? metadata = null)
        {
            string key = Path.GetRelativePath(root, tja); // キャッシュの見出し(Songs から見た場所)
            string relativeFolder = Path.GetRelativePath(root, Path.GetDirectoryName(tja) ?? root);
            newItem = null;
            fromCache = false;

            // 更新日時と大きさ(取れなければキャッシュは使わずに読む)
            long writeTime = 0;
            long size = -1;
            try
            {
                FileInfo info = metadata ?? new FileInfo(tja);
                writeTime = info.LastWriteTimeUtc.Ticks;
                size = info.Length;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 読むときにもう一度失敗するので、ここでは何もしない
            }

            // ----- キャッシュが使えるか(日時も大きさも同じ＝中身は変わっていない) -----
            if (size >= 0 && cache.TryGetValue(key, out SongCacheItem? item) && item.WriteTime == writeTime && item.Size == size)
            {
                newItem = item;
                fromCache = true;
                return new SongEntry
                {
                    TjaPath = tja,
                    RelativeFolder = relativeFolder,
                    Title = item.Title,
                    Subtitle = item.Subtitle,
                    Courses = item.Courses.Select(c => (c.Type, c.Level)).ToList(),
                    Error = item.Error,
                    WriteTime = writeTime,
                };
            }

            // ----- 読み直す -----
            SongEntry entry = ReadEntry(tja, relativeFolder, writeTime);
            if (size >= 0 && entry.Error != ReadFailedMessage)
            {
                newItem = new SongCacheItem
                {
                    Path = key,
                    WriteTime = writeTime,
                    Size = size,
                    Title = entry.Title,
                    Subtitle = entry.Subtitle,
                    Courses = entry.Courses.Select(c => new SongCacheCourse { Type = c.Type, Level = c.Level }).ToList(),
                    Error = entry.Error,
                };
            }
            return entry;
        }

        // .tja を1つ読んで、一覧に出す情報だけ取り出す
        private static SongEntry ReadEntry(string tja, string relativeFolder, long writeTime)
        {
            try
            {
                Chart chart = TjaParser.Load(tja);
                return new SongEntry
                {
                    TjaPath = tja,
                    RelativeFolder = relativeFolder,
                    Title = chart.Title, // TITLE: が無ければファイル名になっている
                    Subtitle = chart.Subtitle,
                    Courses = chart.Courses
                        .OrderBy(c => c.Type)
                        .Select(c => (c.Type, c.Level))
                        .ToList(),
                    Error = chart.Courses.Count == 0 ? "譜面（#START〜#END）がありません" : "",
                    WriteTime = writeTime,
                };
            }
            catch (Exception ex)
            {
                // 裏で何曲も同時に読んでいるので、1曲の失敗で全部止まらないように、どんなエラーでもここで受け止める
                Console.WriteLine($"[Songs] {tja} を読めませんでした: {ex.Message}");
                return new SongEntry
                {
                    TjaPath = tja,
                    RelativeFolder = relativeFolder,
                    Title = Path.GetFileNameWithoutExtension(tja),
                    Error = ReadFailedMessage,
                    WriteTime = writeTime,
                };
            }
        }

        // ===== キャッシュの読み書き =====

        // キャッシュを読む(無い・壊れている・版が違う → 空。全部読み直しになるだけ)
        private static Dictionary<string, SongCacheItem> LoadCache()
        {
            Dictionary<string, SongCacheItem> result = new(StringComparer.OrdinalIgnoreCase); // Windows は大文字小文字を区別しない
            try
            {
                if (!File.Exists(CacheFile)) return result;

                SongCacheFile? file = JsonSerializer.Deserialize(File.ReadAllText(CacheFile), AppJsonContext.Default.SongCacheFile);
                if (file == null || file.Version != CacheVersion) return result;

                foreach (SongCacheItem item in file.Songs)
                {
                    result[item.Path] = item;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                Console.WriteLine($"[Songs] キャッシュを読めませんでした（全部読み直します）: {ex.Message}");
            }
            return result;
        }

        // キャッシュを保存する(今ある曲の分だけ。消した曲の分は残らない)
        private static void SaveCache(SongCacheItem?[] items)
        {
            try
            {
                SongCacheFile file = new SongCacheFile
                {
                    Version = CacheVersion,
                    Songs = items.Where(i => i != null).Select(i => i!).ToList(),
                };

                Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);

                // 書いている途中で落ちても壊れないように、別のファイルに書いてから入れ替える
                string temp = CacheFile + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(file, AppJsonContext.Default.SongCacheFile));
                File.Move(temp, CacheFile, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"[Songs] キャッシュを保存できませんでした: {ex.Message}");
            }
        }

        // ===== 小さい道具 =====

        // 文字コードを自動で見分けて読む(UTF-8 か Shift-JIS)。.tja と同じやり方
        // 文字コードの見分けは Charts/ChartText.cs にまとめてある
        private static string ReadText(string path) => ChartText.Read(path);

        // 「#RRGGBB」「#RGB」を色にする(読めなければ null)
        private static Color? ParseColor(string value)
        {
            string hex = value.Split(';')[0].Trim().TrimStart('#');
            if (hex.Length == 3) hex = string.Concat(hex.Select(c => $"{c}{c}"));
            if (hex.Length != 6) return null;

            return int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb) ? Hex(rgb) : null;
        }

        // 0xRRGGBB → 色
        private static Color Hex(int rgb)
        {
            return new Color((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF), (byte)255);
        }
    }

    // ===== キャッシュの形(SongCache.json に保存する中身) =====

    internal sealed class SongCacheFile
    {
        public int Version { get; set; }
        public List<SongCacheItem> Songs { get; set; } = new();
    }

    // 1曲分
    internal sealed class SongCacheItem
    {
        public string Path { get; set; } = "";     // Songs フォルダから見た .tja の場所
        public long WriteTime { get; set; }        // .tja の更新日時(UTC。Ticks という細かい単位)
        public long Size { get; set; }             // .tja の大きさ(バイト)
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public List<SongCacheCourse> Courses { get; set; } = new();
        public string Error { get; set; } = "";
    }

    // 難易度1つ分
    internal sealed class SongCacheCourse
    {
        public CourseType Type { get; set; }
        public int Level { get; set; }
    }
}