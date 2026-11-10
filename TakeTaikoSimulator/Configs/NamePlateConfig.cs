using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TakeTaikoSimulator.Configs;

// TaikøNautsと同じ6項目。着せ替えで選べる段位は合格記録にあるものだけ。
public sealed class NamePlateConfig
{
    [JsonPropertyName("name")] public string Name { get; set; } = "プレイヤー";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("rank")] public string Rank { get; set; } = "";
    [JsonPropertyName("isRankGold")] public bool IsRankGold { get; set; }
    [JsonPropertyName("namePlateId")] public string NamePlateId { get; set; } = "00";
    [JsonPropertyName("rankType")] public int RankType { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public static string CleanText(string? value, int count, bool trim = true)
    {
        string text = string.Concat((value ?? "").EnumerateRunes()
            .Where(r => !RuneIsControl(r)).Select(r => r.ToString()));
        if (trim) text = text.Trim();
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        return starts.Length > count ? text[..starts[count]] : text;
    }
    private static bool RuneIsControl(System.Text.Rune rune) => System.Text.Rune.IsControl(rune);
    public void Fix(int slot)
    {
        Name = CleanText(Name, 24);
        if (Name.Length == 0) Name = $"プレイヤー{slot}";
        Title = CleanText(Title, 48);
        Rank = CleanText(Rank, 24);
        NamePlateId = CosmeticCatalog.SafeId(NamePlateId) ? NamePlateId : "00";
        RankType = Math.Clamp(RankType, 0, 2);
    }
}

// フォルダ名を優先。整数インデックスだけの従来形式も読めるようにする。
public sealed class PlayerAppearanceConfig
{
    [JsonPropertyName("donchanId")] public string DonchanId { get; set; } = "";
    [JsonPropertyName("donchanType")] public int DonchanType { get; set; }
    [JsonPropertyName("isUsePuchiChara")] public bool IsUsePuchiChara { get; set; }
    [JsonPropertyName("puchiCharaId")] public string PuchiCharaId { get; set; } = "";
    [JsonPropertyName("puchiCharaType")] public int PuchiCharaType { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
