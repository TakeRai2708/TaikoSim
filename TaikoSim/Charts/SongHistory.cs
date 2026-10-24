using System.Text.Json;
using TaikoSim.Configs;

namespace TaikoSim.Charts
{
    // 遊んだ曲とお気に入りの記録(Config フォルダの SongHistory.json に保存する)
    // 曲は「Songs フォルダから見た .tja の場所」(SongEntry.Key)で覚える
    public static class SongHistory
    {
        public const string FileName = "SongHistory.json";

        // 「最近遊んだ曲」に残す数
        public const int RecentMax = 30;

        private static SongHistoryFile? data;

        private static string FilePath => Path.Combine(ConfigManager.ConfigFolder, FileName);

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

        private static SongHistoryFile Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    SongHistoryFile? file = JsonSerializer.Deserialize<SongHistoryFile>(File.ReadAllText(FilePath));
                    if (file != null) return file;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                Console.WriteLine($"[History] 読めませんでした（空から始めます）: {ex.Message}");
            }
            return new SongHistoryFile();
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigManager.ConfigFolder);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, FilePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"[History] 保存できませんでした: {ex.Message}");
            }
        }
    }

    internal sealed class SongHistoryFile
    {
        public List<string> Recent { get; set; } = new();
        public List<string> Favorites { get; set; } = new();
    }
}
