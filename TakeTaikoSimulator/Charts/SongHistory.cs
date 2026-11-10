using System.Text.Json;
using TakeTaikoSimulator.Configs;

namespace TakeTaikoSimulator.Charts
{
    // 遊んだ曲とお気に入りの記録(Config フォルダの SongHistory.json に保存する)
    // 曲は「Songs フォルダから見た .tja の場所」(SongEntry.Key)で覚える
    public static class SongHistory
    {
        public const string FileName = "SongHistory.json";

        // 「最近遊んだ曲」に残す数
        public const int RecentMax = 30;

        private static SongHistoryFile? data;
        private static SongRecordSummary? summary;
        private static readonly Dictionary<int, SongRecordSummary> courseSummaries = new();

        private static readonly Dictionary<int, SongHistoryFile> profileViews = new();
        private static readonly Dictionary<(int Profile, RecordGroup Group), SongRecordSummary> groupSummaries = new();

        private static void InvalidateGroups(int slot)
        {
            foreach (var key in groupSummaries.Keys.Where(k => k.Profile == slot).ToArray()) groupSummaries.Remove(key);
        }

        private static SongHistoryFile ProfileView(int slot)
        {
            if (slot == ActiveProfile) return Data;
            if (!profileViews.TryGetValue(slot, out var file)) profileViews[slot] = file = ReadProfile(slot);
            return file;
        }

        public static int ActiveProfile { get; private set; } = 1;
        public static string ProfileName(int slot) => PlayerDataStore.DisplayName(slot);
        public static string ProfilePath(int slot)
        {
            if (slot < 1 || slot > 3) throw new ArgumentOutOfRangeException(nameof(slot));
            // 1枠目は従来の保存先を使う。移動や上書きなしで記録を引き継ぐ。
            return slot == 1 ? Path.Combine(ConfigManager.ConfigFolder, FileName)
                : Path.Combine(ConfigManager.ConfigFolder, "PlayerData", slot.ToString(), FileName);
        }
        private static string FilePath => ProfilePath(ActiveProfile);

        private static SongHistoryFile ReadProfile(int slot)
        {
            string path = ProfilePath(slot);
            if (!File.Exists(path)) return new SongHistoryFile();
            var loaded = JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.SongHistoryFile)
                ?? throw new JsonException("プレイヤーの記録が空です");
            if (loaded.Records == null || loaded.DanRecords == null || loaded.Recent == null || loaded.Favorites == null
                || loaded.Records.Any(e => e == null) || loaded.DanRecords.Any(e => e == null))
                throw new JsonException("プレイヤーの記録の形式が不正です");
            return loaded;
        }

        public static void SelectProfile(int slot)
        {
            // 読み取りに失敗したら、現在のプレイヤーも記録も変えない。
            SongHistoryFile loaded = ReadProfile(slot);
            ActiveProfile = slot;
            data = loaded;
            profileViews[slot] = loaded;
            InvalidateGroups(slot);
            summary = null;
            courseSummaries.Clear();
        }

        public static SongRecordSummary PreviewProfile(int slot, RecordGroup group = RecordGroup.OniAndUra)
        {
            var file = ReadProfile(slot);
            profileViews[slot] = file;
            InvalidateGroups(slot);
            return Summarize(file, group);
        }

        private static SongRecordSummary Summarize(SongHistoryFile file, RecordGroup group) => SongRecordSummary.From(
            file.Records.Where(e => group == RecordGroup.OniAndUra ? e.Course is 3 or 4 : e.Course == (int)group)
                .Select(e => new SongBestRecord((SongClearType)e.Clear, e.Score, e.Rank)));

        public static SongRecordSummary GetSummary(RecordGroup group, int? profile = null)
        {
            int slot = profile ?? ActiveProfile;
            if (!groupSummaries.TryGetValue((slot, group), out var result))
                groupSummaries[(slot, group)] = result = Summarize(ProfileView(slot), group);
            return result;
        }

        public static (SongClearType Clear, long Score, int Rank) GetRecord(int profile, string key, CourseType course)
        {
            var entry = ProfileView(profile).Records.FirstOrDefault(r => r.Course == (int)course
                && string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            return entry == null ? (SongClearType.None, 0L, 0) : ((SongClearType)entry.Clear, entry.Score, entry.Rank);
        }

        private static SongHistoryFile Data => data ??= Load();

        // 最近遊んだ曲(新しい順)
        public static IReadOnlyList<string> Recent => Data.Recent;

        // お気に入りか
        public static bool IsFavorite(string key) => Data.Favorites.Contains(key, StringComparer.OrdinalIgnoreCase);

        // お気に入り(入れた順)
        public static IReadOnlyList<string> Favorites => Data.Favorites;

        // 遊んだ曲を記録する(一番上に入れる。同じ曲が前にあったら消す)
        public static void AddPlayed(string key)
        {
            SongHistoryFile file = Data;
            file.Recent.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            file.Recent.Insert(0, key);
            if (file.Recent.Count > RecentMax) file.Recent.RemoveRange(RecentMax, file.Recent.Count - RecentMax);
            Save();
        }

        // お気に入りを切り替える。切り替えたあとの状態を返す(true = お気に入りに入った)
        public static bool ToggleFavorite(string key)
        {
            SongHistoryFile file = Data;
            int removed = file.Favorites.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) file.Favorites.Add(key);
            Save();
            return removed == 0;
        }

        // ===== クリア記録 =====
        //   段位の解禁条件(requirements.json の songClear / songScore / songRank / danClear)を
        //   判定するのに要る。曲は「ファイル＋難易度」ごと、段位は dan.json ごとに、一番良い記録だけ残す。

        /// <summary>フルパスを記録のキー(Songs フォルダからの相対パス)にする。
        /// Songs の外のファイルは、そのままのパスをキーにする</summary>
        public static string KeyFor(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return "";
            try
            {
                string relative = Path.GetRelativePath(SongList.SongsFolder, fullPath);
                return relative.StartsWith("..", StringComparison.Ordinal) ? fullPath : relative;
            }
            catch
            {
                return fullPath;
            }
        }

        /// <summary>曲1つの記録。遊んだことがなければ全部 0</summary>
        public static (SongClearType Clear, long Score, int Rank) GetRecord(string key, CourseType course)
        {
            SongRecordEntry? e = FindRecord(key, course);
            return e == null ? (SongClearType.None, 0L, 0) : ((SongClearType)e.Clear, e.Score, e.Rank);
        }

        /// <summary>曲1つの記録を残す。前の記録より良いところだけ上書きする</summary>
        public static SongRecordUpdate SaveRecord(string key, CourseType course, SongClearType clear, long score, int rank)
        {
            if (string.IsNullOrWhiteSpace(key)) return default;
            SongRecordEntry? e = FindRecord(key, course);
            bool created = e == null;
            if (e == null)
            {
                e = new SongRecordEntry { Key = key, Course = (int)course };
                Data.Records.Add(e);
            }
            var before = new SongBestRecord((SongClearType)e.Clear, e.Score, e.Rank);
            SongRecordUpdate update = before.Improve(clear, score, rank);
            e.Clear = (int)update.After.Clear;
            e.Score = update.After.Score;
            e.Rank = update.After.Rank;
            bool saved = Save();
            summary = null;
            courseSummaries.Clear();
            InvalidateGroups(ActiveProfile);
            if (!saved)
            {
                // 保存できなかったときは、次に遊んだとき再挑戦できるようにメモリ上も戻す。
                if (created) Data.Records.Remove(e);
                else { e.Clear = (int)before.Clear; e.Score = before.Score; e.Rank = before.Rank; }
            }
            return update with { Saved = saved };
        }

        public static bool HasRecord(string key, CourseType course) => FindRecord(key, course) != null;

        // 保存済みの最高記録から数え直すので、昇格時に旧状態が残ったり重複加算されたりしない。
        public static SongRecordSummary GetSummary() => summary ??= SongRecordSummary.From(
            Data.Records.Select(e => new SongBestRecord((SongClearType)e.Clear, e.Score, e.Rank)));

        // おに・裏おには、お手本の2つの印が付いたパネルでまとめて数える。
        public static SongRecordSummary GetSummary(CourseType course)
        {
            int group = Math.Clamp((int)course, 0, 3);
            if (!courseSummaries.TryGetValue(group, out SongRecordSummary? result))
            {
                result = SongRecordSummary.From(Data.Records
                    .Where(e => group == 3 ? e.Course == 3 || e.Course == 4 : e.Course == group)
                    .Select(e => new SongBestRecord((SongClearType)e.Clear, e.Score, e.Rank)));
                courseSummaries[group] = result;
            }
            return result;
        }

        /// <summary>段位1つの記録。挑戦したことがなければ「不合格でも何でもない」が返る</summary>
        public static DanClearRecord GetDanRecord(string key)
        {
            DanRecordEntry? e = Data.DanRecords.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            return e == null ? new DanClearRecord(false, SongClearType.None) : new DanClearRecord(e.Gold, (SongClearType)e.Combo);
        }

        /// <summary>段位1つの記録を残す。金かどうかとコンボの種別を、それぞれ良い方で上書きする</summary>
        public static void SaveDanRecord(string key, DanClearRecord record)
        {
            if (key.Length == 0) return;
            DanRecordEntry? e = Data.DanRecords.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            if (e == null)
            {
                e = new DanRecordEntry { Key = key };
                Data.DanRecords.Add(e);
            }
            e.Gold |= record.Gold;
            e.Combo = Math.Max(e.Combo, (int)record.Combo);
            Save();
        }

        private static SongRecordEntry? FindRecord(string key, CourseType course) =>
            Data.Records.FirstOrDefault(r => r.Course == (int)course
                                             && string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));

        private static SongHistoryFile Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    SongHistoryFile? file = JsonSerializer.Deserialize(File.ReadAllText(FilePath), AppJsonContext.Default.SongHistoryFile);
                    if (file != null) return file;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                Console.WriteLine($"[History] 読めませんでした（空から始めます）: {ex.Message}");
            }
            return new SongHistoryFile();
        }

        private static bool Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(Data, AppJsonContext.Default.SongHistoryFile));
                File.Move(temp, FilePath, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"[History] 保存できませんでした: {ex.Message}");
                return false;
            }
        }
    }

    internal sealed class SongHistoryFile
    {
        public List<string> Recent { get; set; } = new();
        public List<string> Favorites { get; set; } = new();
        // クリア記録(段位の解禁条件の判定に使う)
        public List<SongRecordEntry> Records { get; set; } = new();
        public List<DanRecordEntry> DanRecords { get; set; } = new();
    }

    // 曲1つ＋難易度1つぶんの一番良い記録
    internal sealed class SongRecordEntry
    {
        public string Key { get; set; } = "";   // Songs フォルダから見た .tja の場所
        public int Course { get; set; }         // CourseType
        public int Clear { get; set; }          // SongClearType
        public long Score { get; set; }
        public int Rank { get; set; }           // スコアランク(1〜7)
    }

    // 段位1つぶんの一番良い記録
    internal sealed class DanRecordEntry
    {
        public string Key { get; set; } = "";   // Songs フォルダから見た dan.json の場所
        public bool Gold { get; set; }          // 金合格したことがあるか
        public int Combo { get; set; }          // SongClearType(クリア／フルコン／全良)
    }
}


