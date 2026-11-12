using System.Text.Json;
using TakeTaikoSimulator.Charts;

namespace TakeTaikoSimulator.Configs;

// プレイヤーデータごとの設定（Config/PlayerData/<番号>/PlayerSettings.json）
//
//   11/12 たけらい「プレイヤーデータごとに設定とかも変えれるようにしよ」。分け方（①A からキーを外したもの）：
//     データごと（人のもの）：音符の速さ・音色・オート（連打速度・精度・毎回同じ出方）・判定の板・王冠とランクの集計・音量
//     全員共通（PCのもの）  ：叩くキー・画面・スキン・入力の精度・デバッグ・Discord・音オフセット・譜面オフセット・音の出し方
//
//   しくみ（ゲームのほかの所は、今までどおり ConfigManager.Game / Sound を読むだけでいい）：
//     ・メニューを動かす人（最初に入った人）のデータが決まったら Activate：その人の値を ConfigManager.Game / Sound に入れる
//     ・設定を保存するとき（ConfigManager.SaveAll / SaveSound）に SaveActive：今の値を、その人のファイルにも書く
//     ・2人目は LocalSession.Options[側] に自分の値を持つ（音量は、メニューを動かす人のものを使う）
//     ・エントリー画面に戻ったら Deactivate：値はそのまま残るが、もうだれのファイルにも書かない
//   引きつぎ：名前はあるのにこのファイルが無いデータには、起動のとき、今の共通の設定をそのまま写す（Migrate）
public sealed class PlayerSettingsFile
{
    public float HiSpeed { get; set; } = 0f;
    public string HitSound { get; set; } = "";
    public bool AutoPlay { get; set; } = false;
    public int AutoRollPerSecond { get; set; } = 20;
    public int AutoGoodPercent { get; set; } = 100;
    public int AutoOkPercent { get; set; } = 0;
    public int AutoBadPercent { get; set; } = 0;
    public bool AutoFixedPattern { get; set; } = false;
    public bool ShowScoreboard { get; set; } = true;
    public RecordGroup RecordGroup { get; set; } = RecordGroup.OniAndUra;
    public int MasterVolume { get; set; } = 100;
    public int SeVolume { get; set; } = 100;
    public int MusicVolume { get; set; } = 100;
    public int BgmVolume { get; set; } = 100;
    public int VoiceVolume { get; set; } = 100;
}

public static class PlayerSettings
{
    public const string FileName = "PlayerSettings.json";

    /// <summary>今、設定を持ち主のファイルにも書いているデータ（0 = だれのでもない）</summary>
    public static int ActiveSlot { get; private set; }

    public static string PathFor(int slot) => Path.Combine(PlayerDataStore.Folder(slot), FileName);
    public static bool Exists(int slot) => File.Exists(PathFor(slot));

    /// <summary>今の設定（ConfigManager.Game / Sound）から作る</summary>
    public static PlayerSettingsFile FromCurrent()
    {
        PlayerSettingsFile file = new();
        ReadGame(ConfigManager.Game, file);
        ReadSound(ConfigManager.Sound, file);
        return file;
    }

    /// <summary>新しいデータの最初の値：演奏の設定は初期値、音量は今のまま（急に音が大きくならないように）</summary>
    public static PlayerSettingsFile ForNewData()
    {
        PlayerSettingsFile file = new();
        ReadSound(ConfigManager.Sound, file);
        return file;
    }

    /// <summary>そのデータの設定。ファイルが無い・読めないときは、今の設定</summary>
    public static PlayerSettingsFile Load(int slot)
    {
        try
        {
            string path = PathFor(slot);
            if (File.Exists(path))
            {
                PlayerSettingsFile? file = JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.PlayerSettingsFile);
                if (file != null) return file;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.WriteLine($"[PlayerSettings] データ{slot} の設定を読めませんでした（今の設定を使います）: {ex.Message}");
        }
        return FromCurrent();
    }

    public static bool Save(int slot, PlayerSettingsFile file)
    {
        try
        {
            string path = PathFor(slot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(file, AppJsonContext.Default.PlayerSettingsFile));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 保存できなくても、ゲームは止めない
            Console.WriteLine($"[PlayerSettings] データ{slot} の設定を保存できませんでした: {ex.Message}");
            return false;
        }
    }

    /// <summary>2人プレイで、その人の演奏オプション（LocalSession.Options）を変えたとき。音量は前のまま残す</summary>
    public static void Save(int slot, GameConfig options)
    {
        PlayerSettingsFile file = Load(slot);
        ReadGame(options, file);
        Save(slot, file);
        // メニューを動かす人のものなら、今の設定にも入れる（あとの SaveActive で、古い値に戻さないように）
        if (slot == ActiveSlot) ApplyGame(file, ConfigManager.Game);
    }

    /// <summary>メニューを動かす人のデータが決まったとき：その人の値を今の設定に入れる</summary>
    public static void Activate(int slot)
    {
        PlayerSettingsFile file = Load(slot);
        ApplyGame(file, ConfigManager.Game);
        ApplySound(file, ConfigManager.Sound);
        ConfigManager.Game.Fix();
        ConfigManager.Sound.Fix();
        ActiveSlot = slot;
        if (!Exists(slot)) Save(slot, FromCurrent());
        Scenes.SceneManager.Current?.ApplyVolume();   // 今鳴っている曲に、その人の音量をかける
    }

    public static void Deactivate() => ActiveSlot = 0;

    /// <summary>設定を保存するとき（ConfigManager から）：持ち主のファイルにも書く</summary>
    public static void SaveActive()
    {
        if (ActiveSlot != 0) Save(ActiveSlot, FromCurrent());
    }

    /// <summary>起動のとき：名前はあるのに設定のファイルが無いデータに、今の共通の設定を写す</summary>
    public static void Migrate()
    {
        for (int slot = 1; slot <= 3; slot++)
            if (PlayerDataStore.HasName(slot) && !Exists(slot)) Save(slot, FromCurrent());
    }

    public static void Delete(int slot)
    {
        string path = PathFor(slot);
        if (File.Exists(path)) File.Delete(path);
        if (ActiveSlot == slot) ActiveSlot = 0;
    }

    public static void ApplyGame(PlayerSettingsFile file, GameConfig game)
    {
        game.HiSpeed = file.HiSpeed;
        game.HitSound = file.HitSound ?? "";
        game.AutoPlay = file.AutoPlay;
        game.AutoRollPerSecond = file.AutoRollPerSecond;
        game.AutoGoodPercent = file.AutoGoodPercent;
        game.AutoOkPercent = file.AutoOkPercent;
        game.AutoBadPercent = file.AutoBadPercent;
        game.AutoFixedPattern = file.AutoFixedPattern;
        game.ShowScoreboard = file.ShowScoreboard;
        game.RecordGroup = file.RecordGroup;
    }

    private static void ReadGame(GameConfig game, PlayerSettingsFile file)
    {
        file.HiSpeed = game.HiSpeed;
        file.HitSound = game.HitSound;
        file.AutoPlay = game.AutoPlay;
        file.AutoRollPerSecond = game.AutoRollPerSecond;
        file.AutoGoodPercent = game.AutoGoodPercent;
        file.AutoOkPercent = game.AutoOkPercent;
        file.AutoBadPercent = game.AutoBadPercent;
        file.AutoFixedPattern = game.AutoFixedPattern;
        file.ShowScoreboard = game.ShowScoreboard;
        file.RecordGroup = game.RecordGroup;
    }

    public static void ApplySound(PlayerSettingsFile file, SoundConfig sound)
    {
        sound.MasterVolume = file.MasterVolume;
        sound.SeVolume = file.SeVolume;
        sound.MusicVolume = file.MusicVolume;
        sound.BgmVolume = file.BgmVolume;
        sound.VoiceVolume = file.VoiceVolume;
    }

    private static void ReadSound(SoundConfig sound, PlayerSettingsFile file)
    {
        file.MasterVolume = sound.MasterVolume;
        file.SeVolume = sound.SeVolume;
        file.MusicVolume = sound.MusicVolume;
        file.BgmVolume = sound.BgmVolume;
        file.VoiceVolume = sound.VoiceVolume;
    }
}

// プレイヤーデータの初期化（設定画面の「プレイヤーデータ」から）
//   11/12 たけらい「全部と選ぶをボタンだしておいて、選べる押したら一覧だしてチェックボックス式でチェックしたやつだけ」
[Flags]
public enum PlayerDataParts
{
    None = 0,
    Name = 1,          // 名前と名札（消すと「新しく遊ぶ」に戻る）
    Appearance = 2,    // 着せ替え
    SongRecords = 4,   // 曲の記録（王冠・スコア・ランク）
    DanRecords = 8,    // 段位の記録
    Favorites = 16,    // お気に入り
    Recent = 32,       // 最近遊んだ曲
    Settings = 64,     // このデータの設定
    All = Name | Appearance | SongRecords | DanRecords | Favorites | Recent | Settings,
}

public static class PlayerDataReset
{
    public static readonly (PlayerDataParts Part, string Label)[] Items =
    {
        (PlayerDataParts.Name, "名前と名札（「新しく遊ぶ」に戻ります）"),
        (PlayerDataParts.Appearance, "着せ替え"),
        (PlayerDataParts.SongRecords, "曲の記録（王冠・スコア・ランク）"),
        (PlayerDataParts.DanRecords, "段位の記録"),
        (PlayerDataParts.Favorites, "お気に入り"),
        (PlayerDataParts.Recent, "最近遊んだ曲"),
        (PlayerDataParts.Settings, "このデータの設定（速さ・音色・音量など）"),
    };

    /// <summary>選んだ物を消す。失敗したら例外（IOException・UnauthorizedAccessException・JsonException）</summary>
    public static void Run(int slot, PlayerDataParts parts)
    {
        if (parts.HasFlag(PlayerDataParts.Name)) DeleteFile(PlayerDataStore.NamePath(slot));
        if (parts.HasFlag(PlayerDataParts.Appearance)) DeleteFile(PlayerDataStore.AppearancePath(slot));
        if (parts.HasFlag(PlayerDataParts.Settings)) PlayerSettings.Delete(slot);
        SongHistory.ClearProfile(slot,
            parts.HasFlag(PlayerDataParts.SongRecords), parts.HasFlag(PlayerDataParts.DanRecords),
            parts.HasFlag(PlayerDataParts.Favorites), parts.HasFlag(PlayerDataParts.Recent));
        PlayerDataStore.Reload();
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
