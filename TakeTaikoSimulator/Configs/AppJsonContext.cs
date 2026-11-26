using System.Text.Json;
using System.Text.Json.Serialization;
using TakeTaikoSimulator.Charts;

namespace TakeTaikoSimulator.Configs
{
    // Native AOT 用の JSON 設定(逆コンパイル対策で exe を機械語にするときに要る)
    //
    // ふつうの .NET は、実行中にリフレクション(型を後から調べるしくみ)で JSON を読み書きする。
    // でも Native AOT ではリフレクションがほぼ使えないので、代わりに
    //   「どの型をどう読み書きするか」をコンパイル時に作っておく(＝ソース生成)。
    // それがこのクラス。JSON で保存する型が増えたら、ここに [JsonSerializable(typeof(...))] を足す。
    //
    // 設定ファイルとの互換：
    //   ・UseStringEnumConverter … enum(キーの名前など)を数字でなく "F" みたいな文字で保存(前と同じ)
    //   ・WriteIndented           … 改行・インデントして読みやすく(前と同じ)
    //   ・ReadCommentHandling・AllowTrailingCommas … JSON内の // コメントと最後のカンマを許す(前と同じ)
    //   ・日本語は あ のようにエスケープされる(前は生のまま保存していた。中身は同じで、見た目だけ変わる)
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        UseStringEnumConverter = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true)]
    [JsonSerializable(typeof(NamePlateConfig))]
    [JsonSerializable(typeof(PlayerAppearanceConfig))]
    [JsonSerializable(typeof(PlayerSettingsFile))]
    [JsonSerializable(typeof(GameConfig))]
    [JsonSerializable(typeof(SoundConfig))]
    [JsonSerializable(typeof(KeyConfig))]
    [JsonSerializable(typeof(SkinConfig))]
    [JsonSerializable(typeof(PresetConfig))]
    [JsonSerializable(typeof(SongHistoryFile))]
    [JsonSerializable(typeof(SongCacheFile))]
    // どんちゃんのコマのキャッシュのメタ(Graphics/CharaCache.cs)
    [JsonSerializable(typeof(TakeTaikoSimulator.Graphics.CharaCacheMeta))]
    internal partial class AppJsonContext : JsonSerializerContext
    {
    }
}
