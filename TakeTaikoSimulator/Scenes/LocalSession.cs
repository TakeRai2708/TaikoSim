using TakeTaikoSimulator.Configs;

namespace TakeTaikoSimulator.Scenes;

// 同じPCで遊ぶ参加者。配列の番号は「側」（0 = ひだり・1P・赤、1 = みぎ・2P・青）で、データ枠とは別。
//
//   11/12 たけらい「どっちの太鼓で遊びますか」：側と、叩くキーの組を分けた。
//   ・キーの組（0 = 設定の1Pのキー、1 = 2Pのキー）は、最初に叩いた方をそのまま使う
//   ・側は、エントリーの「ひだり／みぎ」で決める。KeySets[側] = その側の人が叩くキーの組
//   ・1人で「みぎ」を選ぶと、メニューの見た目は 2P（青・右）。演奏画面だけは 1P の置き方のまま（PlayScene）
//   ・あとから入る2人目は、空いている側と、残りのキーの組になる（FreeSide・FreeKeySet）
public static class LocalSession
{
    public static bool[] Joined { get; } = new bool[2];
    public static int[] Profiles { get; } = { 1, 2 };
    /// <summary>その側の人が叩くキーの組（0 か 1）。2つは必ず別の組</summary>
    public static int[] KeySets { get; } = { 0, 1 };
    /// <summary>空いている側（2人ともいるときは 1）</summary>
    public static int FreeSide => Joined[0] ? 1 : 0;
    /// <summary>まだだれも使っていないキーの組</summary>
    public static int FreeKeySet => KeySets[FreeSide];
    public static GameConfig[] Options { get; private set; } = { new(), new() };
    public static int Controller { get; private set; }
    public static int? InputOverride { get; private set; }
    public static int MenuPlayer => InputOverride ?? Controller;
    public static GameConfig? OptionsContext { get; private set; }
    public static int Count => Joined.Count(x => x);
    public static int[] Players => Enumerable.Range(0, 2).Where(p => Joined[p]).ToArray();

    public static void Reset()
    {
        Array.Clear(Joined);
        Controller = 0;
        Profiles[0] = 1; Profiles[1] = 2;
        KeySets[0] = 0; KeySets[1] = 1;
        PlayerSettings.Deactivate();   // だれもいない間は、設定をだれのデータにも書かない
        Options = new[] { ConfigManager.Copy(ConfigManager.Game), ConfigManager.Copy(ConfigManager.Game) };
    }

    /// <summary>side の側に、keySet のキーの組で参加する。もう片方の側は、残りのキーの組になる</summary>
    public static void Join(int side, int keySet, int? profile = null)
    {
        if (side is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(side));
        if (keySet is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(keySet));
        if (Count == 0) Controller = side;
        Joined[side] = true;
        KeySets[side] = keySet;
        KeySets[1 - side] = 1 - keySet;
        if (profile is not int slot) return;
        Profiles[side] = slot;
        // その人の設定（Configs/PlayerSettings.cs）
        if (side == Controller)
        {
            // メニューを動かす人：今の設定そのものを、その人の値にする
            PlayerSettings.Activate(slot);
            Options[side] = ConfigManager.Copy(ConfigManager.Game);
        }
        else
        {
            // あとから入った人：演奏オプションだけ自分の値（音量などは、メニューを動かす人のもの）
            Options[Controller] = ConfigManager.Copy(ConfigManager.Game);   // 1人で遊んでいる間に変えた分を写す
            GameConfig own = ConfigManager.Copy(ConfigManager.Game);
            PlayerSettings.ApplyGame(PlayerSettings.Load(slot), own);
            own.Fix();
            Options[side] = own;
        }
    }

    private static (bool Auto, float Speed, int Roll, string Sound, bool Fixed, bool Board) ReadOptions(GameConfig cfg)
        => (cfg.AutoPlay, cfg.HiSpeed, cfg.AutoRollPerSecond, cfg.HitSound, cfg.AutoFixedPattern, cfg.ShowScoreboard);

    private static void WriteOptions(GameConfig cfg, (bool Auto, float Speed, int Roll, string Sound, bool Fixed, bool Board) value)
    {
        (cfg.AutoPlay, cfg.HiSpeed, cfg.AutoRollPerSecond, cfg.HitSound, cfg.AutoFixedPattern, cfg.ShowScoreboard) = value;
    }

    public static void WithOptions(int player, Action action)
    {
        if (player is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(player));
        GameConfig current = ConfigManager.Game;
        var previous = ReadOptions(current);
        WriteOptions(current, ReadOptions(Options[player]));
        var oldOptions = OptionsContext;
        var oldInput = InputOverride;
        OptionsContext = Options[player];
        InputOverride = player;
        try { action(); }
        finally
        {
            // メニューを動かす人の分は、変えた値をそのまま今の設定に残す（その人のデータに保存されるので、古い値に戻さない）
            WriteOptions(current, player == Controller ? ReadOptions(Options[player]) : previous); OptionsContext = oldOptions; InputOverride = oldInput; }
    }
}
