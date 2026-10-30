namespace TaikoSim.Charts
{
    // 段位道場の一覧。Songs フォルダの下から dan.def を探して、その下の dan.json を段位として読む。
    //   フォルダの作りは TaikøNauts と同じ(TAIKONAUTS-SPEC.md 参照)：
    //     Songs/<なにか>/<カテゴリ>/dan.def
    //     Songs/<なにか>/<カテゴリ>/<段位>/dan.json ＋ 譜面 ＋ 音源
    //   dan.def が複数あれば、ゲーム内でカテゴリを選べる。
    //
    //   読み込みは裏のスレッドでやる(譜面の中身まで読むので時間がかかる)。
    //   SongList と同じく、StartScan() で始めて、毎フレーム Update() を呼ぶと結果が入る。
    public static class DanList
    {
        /// <summary>読み終わった段位カテゴリ(danIndex 順に並べてある)</summary>
        public static List<DanCategory> Categories { get; private set; } = new();

        public static bool IsScanned { get; private set; }
        public static bool IsScanning { get; private set; }

        /// <summary>段位の数(全カテゴリ合計)</summary>
        public static int CourseCount => Categories.Sum(c => c.Courses.Count);

        /// <summary>読めなかった段位の数(段位選択画面に出す)</summary>
        public static int ErrorCount => Categories.Sum(c => c.Courses.Count(x => x.Error.Length > 0));

        // 裏のスレッドが作ったもの。出来たら Update() で Categories に移す
        private static List<DanCategory>? pending;
        private static readonly object pendingLock = new();

        // dan.def を探すときに、どれだけ深くまで見るか(Songs の下が深すぎるときの保険)
        private const int MaxDepth = 6;

        public static void StartScan()
        {
            if (IsScanning) return;
            IsScanning = true;
            lock (pendingLock) pending = null;

            Thread thread = new(ScanInBackground) { IsBackground = true, Name = "DanListScan" };
            thread.Start();
        }

        /// <summary>もう一度読み直す(段位選択画面の F2)</summary>
        public static void Reload()
        {
            IsScanned = false;
            StartScan();
        }

        /// <summary>毎フレーム呼ぶ。裏で読み終わっていたら Categories に移す</summary>
        public static void Update()
        {
            List<DanCategory>? done;
            lock (pendingLock)
            {
                done = pending;
                pending = null;
            }
            if (done == null) return;

            Categories = done;
            IsScanned = true;
            IsScanning = false;
        }

        private static void ScanInBackground()
        {
            List<DanCategory> found = new();
            try
            {
                CollectCategories(SongList.SongsFolder, 0, found);

                foreach (DanCategory category in found)
                {
                    foreach (string folder in SafeDirectories(category.FolderPath))
                    {
                        string jsonPath = Path.Combine(folder, DanJson.JsonName);
                        if (!File.Exists(jsonPath)) continue;

                        DanCourse course = DanJson.ReadCourse(jsonPath);
                        FillSongInfo(course);
                        category.Courses.Add(course);
                    }
                    category.Courses.Sort(Compare);
                }

                // 段位が1つも無いカテゴリは出さない
                found.RemoveAll(c => c.Courses.Count == 0);
                found.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCulture));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dan] 読み込みで失敗しました: {ex.Message}");
            }

            lock (pendingLock) pending = found;
        }

        // danIndex の順。同じなら題名の順
        private static int Compare(DanCourse a, DanCourse b)
        {
            int byIndex = a.DanIndex.CompareTo(b.DanIndex);
            return byIndex != 0 ? byIndex : string.Compare(a.Title, b.Title, StringComparison.CurrentCulture);
        }

        // dan.def のあるフォルダを探す。見つかったらその下はもう探さない(その中は段位のフォルダなので)
        private static void CollectCategories(string folder, int depth, List<DanCategory> into)
        {
            if (depth > MaxDepth || !Directory.Exists(folder)) return;

            string defPath = Path.Combine(folder, DanJson.DefName);
            if (File.Exists(defPath))
            {
                if (DanJson.ReadDef(defPath) is { } category) into.Add(category);
                return;
            }

            foreach (string child in SafeDirectories(folder)) CollectCategories(child, depth + 1, into);
        }

        private static IEnumerable<string> SafeDirectories(string folder)
        {
            try
            {
                return Directory.EnumerateDirectories(folder).OrderBy(f => f, StringComparer.CurrentCulture).ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        // 課題曲の曲名・サブタイトル・★を、譜面ファイルから読んで入れる
        private static void FillSongInfo(DanCourse course)
        {
            List<string> missing = new();
            foreach (DanSong song in course.Songs)
            {
                if (song.FullPath.Length == 0 || !File.Exists(song.FullPath))
                {
                    missing.Add(song.RelativePath);
                    continue;
                }
                // .osu はまだ読めない。曲名はファイル名で代わりにする
                if (!song.FullPath.EndsWith(".tja", StringComparison.OrdinalIgnoreCase))
                {
                    song.Title = Path.GetFileNameWithoutExtension(song.FullPath);
                    continue;
                }

                try
                {
                    Chart chart = TjaParser.Load(song.FullPath);
                    song.Title = chart.Title;
                    song.Subtitle = chart.Subtitle;
                    song.Level = chart.Courses.FirstOrDefault(c => c.Type == song.Course)?.Level ?? 0;
                }
                catch
                {
                    song.Title = Path.GetFileNameWithoutExtension(song.FullPath);
                }
            }

            if (missing.Count > 0 && course.Error.Length == 0)
                course.Error = $"譜面ファイルが見つかりません：{string.Join("、", missing)}";
        }

        // ===== 探す =====

        public static IEnumerable<DanCourse> AllCourses => Categories.SelectMany(c => c.Courses);

        /// <summary>dan.json のパスから段位を探す(requirements.json の danClear の相手を見つけるのに使う)</summary>
        public static DanCourse? FindByPath(string fullJsonPath)
        {
            if (string.IsNullOrEmpty(fullJsonPath)) return null;
            string target = Normalize(fullJsonPath);
            return AllCourses.FirstOrDefault(c => Normalize(c.JsonPath) == target);
        }

        /// <summary>その段位が入っているカテゴリ</summary>
        public static DanCategory? CategoryOf(DanCourse course) =>
            Categories.FirstOrDefault(c => c.Courses.Contains(course));

        private static string Normalize(string path)
        {
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant(); }
            catch { return path.ToLowerInvariant(); }
        }
    }
}
