using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TakeTaikoSimulator.Charts;

namespace TakeTaikoSimulator.Configs;

// 記録ファイルは移動しない。名前と着せ替えだけを各データ枠の下に置く。
//
//   ゲスト（11/13 たけらい「ゲストプレイを実装しようか」「遊んでる時だけ記録」）：
//     データ番号 0 がゲスト。名前は「どんちゃん」、着せ替えは初期値。ファイルは持たない。
//     遊んでいる間に変えたもの（着せ替え・段位・設定）は、ゲームを閉じる・エントリーに戻るまでは覚えている（Reload で消える）
public static class PlayerDataStore
{
    public const int GuestSlot = 0;
    public const string GuestName = "どんちゃん";
    public static bool IsGuest(int slot) => slot == GuestSlot;

    private static readonly Dictionary<int, NamePlateConfig> plates = new();
    private static readonly Dictionary<int, PlayerAppearanceConfig> appearances = new();
    public static int Generation { get; private set; }
    public static string Folder(int slot)
    {
        if (slot is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(slot));
        return Path.Combine(ConfigManager.ConfigFolder, "PlayerData", slot.ToString());
    }
    public static string NamePath(int slot) => Path.Combine(Folder(slot), "NamePlateConfig.json");
    public static string AppearancePath(int slot) => Path.Combine(Folder(slot), "PlayerConfig.json");
    public static bool HasName(int slot) => IsGuest(slot) || File.Exists(NamePath(slot));
    public static void Reload()
    {
        plates.Clear(); appearances.Clear(); Generation++;
    }
    public static NamePlateConfig NamePlate(int slot)
    {
        if (!plates.TryGetValue(slot, out var data))
        {
            data = IsGuest(slot) ? new() { Name = GuestName }
                : Read(NamePath(slot), AppJsonContext.Default.NamePlateConfig) ?? new() { Name = $"プレイヤー{slot}" };
            data.Fix(slot); plates[slot] = data;
        }
        return data;
    }
    public static PlayerAppearanceConfig Appearance(int slot)
    {
        if (!appearances.TryGetValue(slot, out var data))
            appearances[slot] = data = IsGuest(slot) ? new()
                : Read(AppearancePath(slot), AppJsonContext.Default.PlayerAppearanceConfig) ?? new();
        return data;
    }
    public static string DisplayName(int slot)
    {
        try { return NamePlate(slot).Name; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return $"プレイヤー{slot}"; }
    }
    public static NamePlateConfig Copy(NamePlateConfig data) => JsonSerializer.Deserialize(
        JsonSerializer.Serialize(data, AppJsonContext.Default.NamePlateConfig), AppJsonContext.Default.NamePlateConfig)!;
    public static PlayerAppearanceConfig Copy(PlayerAppearanceConfig data) => JsonSerializer.Deserialize(
        JsonSerializer.Serialize(data, AppJsonContext.Default.PlayerAppearanceConfig), AppJsonContext.Default.PlayerAppearanceConfig)!;
    public static void Save(int slot, NamePlateConfig data)
    {
        var copy = Copy(data); copy.Fix(slot);
        if (!IsGuest(slot)) Write(NamePath(slot), copy, AppJsonContext.Default.NamePlateConfig);   // ゲストは覚えるだけ
        plates[slot] = copy; Generation++;
    }
    // 一度だけ：ネームプレートが "00" のデータを「おまかせ」（空）にする（GameConfig.NamePlateAutoMigrated）
    public static void MigrateDefaultPlates()
    {
        if (ConfigManager.Game.NamePlateAutoMigrated) return;
        for (int slot = 1; slot <= 3; slot++)
        {
            try
            {
                if (!File.Exists(NamePath(slot))) continue;
                var data = NamePlate(slot);
                if (data.NamePlateId != "00") continue;
                var copy = Copy(data);
                copy.NamePlateId = "";
                Save(slot, copy);
                Console.WriteLine($"[NamePlate] データ{slot}：ネームプレートを「おまかせ」にしました（前は 00）");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // 読めない・書けないデータはそのまま（次に起動したときもやり直さない。データを上書きしないため）
                Console.WriteLine($"[NamePlate] データ{slot}：おまかせにできませんでした：{ex.Message}");
            }
        }
        ConfigManager.Game.NamePlateAutoMigrated = true;
        ConfigManager.SaveGame();
    }

    public static void Save(int slot, PlayerAppearanceConfig data)
    {
        var copy = Copy(data);
        if (!IsGuest(slot)) Write(AppearancePath(slot), copy, AppJsonContext.Default.PlayerAppearanceConfig);
        appearances[slot] = copy; Generation++;
    }
    private static T? Read<T>(string path, JsonTypeInfo<T> info) where T : class => !File.Exists(path) ? null
        : JsonSerializer.Deserialize(File.ReadAllText(path), info) ?? throw new JsonException("設定が空です：" + path);
    private static void Write<T>(string path, T data, JsonTypeInfo<T> info)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(data, info));
        File.Move(temporary, path, overwrite: true);
    }

    private static readonly string[] Ranks = { "五級", "四級", "三級", "二級", "一級", "初段", "二段", "三段", "四段", "五段", "六段", "七段", "八段", "九段", "十段", "玄人", "名人", "超人", "達人" };
    public static string RankName(int index) => index >= 0 && index < Ranks.Length ? Ranks[index] : "";
    public static int RankIndex(string rank)
    {
        int found = Array.IndexOf(Ranks, rank);
        if (found >= 0) return found;
        if (rank.Length > 1 && rank.EndsWith('段') && int.TryParse(rank[..^1], out int number) && number is >= 1 and <= 10) return number + 4;
        return -1;
    }
    public static bool Promote(NamePlateConfig data, int danIndex, bool gold, SongClearType combo)
    {
        // 外伝は上下関係が無いので、腕前の段位を書き換えない。
        if (danIndex < 0 || danIndex >= Ranks.Length || danIndex < RankIndex(data.Rank)) return false;
        bool same = danIndex == RankIndex(data.Rank);
        int type = combo == SongClearType.AllPerfect ? 2 : combo == SongClearType.FullCombo ? 1 : 0;
        string oldRank = data.Rank; bool oldGold = data.IsRankGold; int oldType = data.RankType;
        data.Rank = Ranks[danIndex];
        data.IsRankGold = same ? data.IsRankGold || gold : gold;
        data.RankType = same ? Math.Max(data.RankType, type) : type;
        return oldRank != data.Rank || oldGold != data.IsRankGold || oldType != data.RankType;
    }
    public static void UpdateDan(int slot, int danIndex, bool gold, SongClearType combo)
    {
        try
        {
            var plate = Copy(NamePlate(slot));
            if (Promote(plate, danIndex, gold, combo)) Save(slot, plate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { Console.WriteLine($"[NamePlate] 段位を反映できませんでした：{ex.Message}"); }
    }
}
