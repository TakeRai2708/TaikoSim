using TaikoSim.Configs;

namespace TaikoSim.Scenes;

// 同じPCで遊ぶ参加者。配列の番号は物理的な1P/2Pで、データ枠とは別。
public static class LocalSession
{
    public static bool[] Joined { get; } = new bool[2];
    public static int[] Profiles { get; } = { 1, 2 };
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
        Options = new[] { ConfigManager.Copy(ConfigManager.Game), ConfigManager.Copy(ConfigManager.Game) };
    }

    // trueなら「参加済みの側が押した」ので次へ。未参加側なら参加するだけ。
    public static bool Don(int player)
    {
        if (player is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(player));
        if (Joined[player]) return true;
        if (Count == 0) Controller = player;
        Joined[player] = true;
        return false;
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
        finally { WriteOptions(current, previous); OptionsContext = oldOptions; InputOverride = oldInput; }
    }
}
