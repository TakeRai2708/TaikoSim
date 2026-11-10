// dan-check 用の、ゲーム本体の代わり。
//   Charts/Chart.cs や Charts/SongHistory.cs を丸ごと持ってくると Raylib や設定ファイルが要るので、
//   パーサと判定ロジックのテストに必要な分だけここに置いている。
//   ★ 本体側の並び・名前を変えたら、こっちも直すこと。
namespace TakeTaikoSimulator.Charts
{
    // Charts/Chart.cs の CourseType（並びを必ず同じにする）
    public enum CourseType { Easy = 0, Normal = 1, Hard = 2, Oni = 3, Edit = 4, Tower = 5, Dan = 6 }

    // Charts/SongHistory.cs の代わり。記録はメモリの中だけに持つ（テスト用に差し替えられる）
    public static class SongHistory
    {
        public static Dictionary<string, DanClearRecord> DanRecords { get; } = new(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<(string, CourseType), (SongClearType Clear, long Score, int Rank)> Records { get; }
            = new();

        public static string KeyFor(string fullPath) => fullPath ?? "";

        public static DanClearRecord GetDanRecord(string key) =>
            DanRecords.TryGetValue(key, out DanClearRecord r) ? r : new DanClearRecord(false, SongClearType.None);

        public static void SaveDanRecord(string key, DanClearRecord record) => DanRecords[key] = record;

        public static (SongClearType Clear, long Score, int Rank) GetRecord(string key, CourseType course) =>
            Records.TryGetValue((key, course), out var r) ? r : (SongClearType.None, 0L, 0);

        public static void SaveRecord(string key, CourseType course, SongClearType clear, long score, int rank) =>
            Records[(key, course)] = (clear, score, rank);
    }
}
