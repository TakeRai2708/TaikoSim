using System.Text.Json;

namespace TaikoSim.Graphics
{
    // どんちゃん・かつちゃんの設定(スキンの Image/99.Common/Chara/<番号.名前>/Config.json)
    //   TaikøNauts の A-Style に入っているものをそのまま読む。位置・倍率・ループの長さ・モーションの拍数が全部ここにある。
    //   キーは camelCase(entryX など)。TaikoSim の設定ファイル(GameConfig など)は PascalCase なので、
    //   ソース生成の AppJsonContext に混ぜるとそっちの読み書きが壊れる。
    //   なので JsonDocument で必要な項目だけ手で読む(JsonDocument.Parse は Native AOT でも使える)。
    //
    //   位置の配列は [1P, 2P] の2つ。TaikoSim は今のところ1人プレイだけなので [0] を使う。
    //   「○○Time」はミリ秒。「○○MotionBeat」は、そのアニメ1周ぶんが曲の何拍ぶんかを表す。
    public sealed class CharaConfig
    {
        public string Name { get; private set; } = "";

        // モード選択(エントリー画面)
        public float[] EntryX { get; private set; } = { 0f, 0f };
        public float[] EntryY { get; private set; } = { 0f, 0f };
        public float EntryScale { get; private set; } = 1f;
        public int EntryModeLoopTime { get; private set; } = 500;

        // 選曲画面
        public float[] SongSelectX { get; private set; } = { 0f, 0f };
        public float[] SongSelectY { get; private set; } = { 0f, 0f };
        public float SongSelectScale { get; private set; } = 1f;
        public int SongSelectLoopTime { get; private set; } = 1000;
        public int SongSelectWaitLoopTime { get; private set; } = 1000;
        public int SongSelectDecideTime { get; private set; } = 1000;
        public int SongSelectSkipMotionTime { get; private set; } = 1000;
        public int SongSelectSongStartTime { get; private set; } = 360;
        public int SongSelectSongStartJumpIndex { get; private set; } = 0;

        // 演奏画面
        public float[] EnsoGameX { get; private set; } = { 0f, 0f };
        public float[] EnsoGameY { get; private set; } = { 0f, 0f };
        public float[] EnsoGameBalloonX { get; private set; } = { 0f, 0f };
        public float[] EnsoGameBalloonY { get; private set; } = { 0f, 0f };
        public float EnsoGameNormalScale { get; private set; } = 1f;
        public float EnsoGameBalloonScale { get; private set; } = 1f;
        public bool GoGoStartMotionFromBeginning { get; private set; } = true;

        // アニメ1周ぶんの拍数。公式ドキュメントいわく「1小節を4拍としたとき、各モーションにかかる時間(単位：拍)」。
        //   曲のBPMに合わせて動くので、小さくすると速く、大きくするとゆっくりになる。
        //   型は Integer ではなく Double(2.5 拍などが書ける)
        public float NormalMotionBeat { get; private set; } = 4f;
        public float Combo10MotionBeat { get; private set; } = 2f;
        public float Combo10MaxMotionBeat { get; private set; } = 2f;
        public float GoGoStartMotionBeat { get; private set; } = 2f;
        public float GoGoMotionBeat { get; private set; } = 4f;
        public float ClearInMotionBeat { get; private set; } = 2f;
        public float ClearMotionBeat { get; private set; } = 2f;
        public float SoulInMotionBeat { get; private set; } = 2f;
        public float ComboCutMotionBeat { get; private set; } = 2f;
        public float MissLoopMotionBeat { get; private set; } = 1f;
        public float FailedLoopMotionBeat { get; private set; } = 1f;
        public float BalloonBrokeMotionBeat { get; private set; } = 1f;
        public float BalloonMissMotionBeat { get; private set; } = 1f;

        // リザルト画面
        public float[] ResultX { get; private set; } = { 0f, 0f };
        public float[] ResultY { get; private set; } = { 0f, 0f };
        public float ResultScale { get; private set; } = 1f;
        public int ResultLoopTime { get; private set; } = 500;
        public int ResultClearLoopTime { get; private set; } = 700;
        public int ResultFailedInTime { get; private set; } = 200;
        public int ResultFailedLoopTime { get; private set; } = 500;

        // 読めなかったら null(そのスキンにはどんちゃんが無い、として何も描かない)
        public static CharaConfig? Load(string jsonPath)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                JsonElement r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object) return null;

                CharaConfig c = new()
                {
                    Name = Text(r, "name", ""),

                    EntryX = Pair(r, "entryX"),
                    EntryY = Pair(r, "entryY"),
                    EntryScale = Num(r, "entryScale", 1f),
                    EntryModeLoopTime = Int(r, "entryModeLoopTime", 500),

                    SongSelectX = Pair(r, "songSelectX"),
                    SongSelectY = Pair(r, "songSelectY"),
                    SongSelectScale = Num(r, "songSelectScale", 1f),
                    SongSelectLoopTime = Int(r, "songSelectLoopTime", 1000),
                    SongSelectWaitLoopTime = Int(r, "songSelectWaitLoopTime", 1000),
                    SongSelectDecideTime = Int(r, "songSelectDecideTime", 1000),
                    SongSelectSkipMotionTime = Int(r, "songSelectSkipMotionTime", 1000),
                    SongSelectSongStartTime = Int(r, "songSelectSongStartTime", 360),
                    SongSelectSongStartJumpIndex = Int(r, "songSelectSongStartJumpIndex", 0),

                    EnsoGameX = Pair(r, "ensoGameX"),
                    EnsoGameY = Pair(r, "ensoGameY"),
                    EnsoGameBalloonX = Pair(r, "ensoGameBalloonX"),
                    EnsoGameBalloonY = Pair(r, "ensoGameBalloonY"),
                    EnsoGameNormalScale = Num(r, "ensoGameNormalScale", 1f),
                    EnsoGameBalloonScale = Num(r, "ensoGameBalloonScale", 1f),
                    GoGoStartMotionFromBeginning = Flag(r, "goGoStartMotionFromBeginning", true),

                    NormalMotionBeat = Beat(r, "normalMotionBeat", 4f),
                    Combo10MotionBeat = Beat(r, "combo10MotionBeat", 2f),
                    Combo10MaxMotionBeat = Beat(r, "combo10MaxMotionBeat", 2f),
                    GoGoStartMotionBeat = Beat(r, "goGoStartMotionBeat", 2f),
                    // A-Style のキーは goGoMotionMotionBeat（Motion が2回入っている）
                    GoGoMotionBeat = Beat(r, "goGoMotionMotionBeat", 4f),
                    ClearInMotionBeat = Beat(r, "clearInMotionBeat", 2f),
                    ClearMotionBeat = Beat(r, "clearMotionBeat", 2f),
                    SoulInMotionBeat = Beat(r, "soulInMotionBeat", 2f),
                    ComboCutMotionBeat = Beat(r, "comboCutMotionBeat", 2f),
                    MissLoopMotionBeat = Beat(r, "missLoopMotionBeat", 1f),
                    FailedLoopMotionBeat = Beat(r, "failedLoopMotionBeat", 1f),
                    BalloonBrokeMotionBeat = Beat(r, "balloonBrokeMotionBeat", 1f),
                    BalloonMissMotionBeat = Beat(r, "balloonMissMotionBeat", 1f),

                    ResultX = Pair(r, "resultX"),
                    ResultY = Pair(r, "resultY"),
                    ResultScale = Num(r, "resultScale", 1f),
                    ResultLoopTime = Int(r, "resultLoopTime", 500),
                    ResultClearLoopTime = Int(r, "resultClearLoopTime", 700),
                    ResultFailedInTime = Int(r, "resultFailedInTime", 200),
                    ResultFailedLoopTime = Int(r, "resultFailedLoopTime", 500),
                };
                return c;
            }
            catch
            {
                return null;
            }
        }

        // ===== JSON から1つ読む小物 =====

        private static string Text(JsonElement r, string key, string fallback) =>
            r.TryGetProperty(key, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? fallback : fallback;

        private static float Num(JsonElement r, string key, float fallback) =>
            r.TryGetProperty(key, out JsonElement e) && e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out double v) && double.IsFinite(v)
                ? (float)v : fallback;

        private static int Int(JsonElement r, string key, int fallback)
        {
            float v = Num(r, key, fallback);
            return v is > int.MinValue and < int.MaxValue ? (int)MathF.Round(v) : fallback;
        }

        // 拍数は 0 や マイナス だと「割る」ときに壊れるので、下限をつける
        private static float Beat(JsonElement r, string key, float fallback) => Math.Max(0.01f, Num(r, key, fallback));

        private static bool Flag(JsonElement r, string key, bool fallback) =>
            r.TryGetProperty(key, out JsonElement e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : fallback;

        // [1P, 2P] の配列。1つしか無ければ両方それにする
        private static float[] Pair(JsonElement r, string key)
        {
            if (!r.TryGetProperty(key, out JsonElement e)) return new[] { 0f, 0f };
            if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out double one) && double.IsFinite(one))
                return new[] { (float)one, (float)one };
            if (e.ValueKind != JsonValueKind.Array) return new[] { 0f, 0f };

            float[] got = new float[2];
            int i = 0;
            foreach (JsonElement item in e.EnumerateArray())
            {
                if (i >= 2) break;
                got[i++] = item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out double v) && double.IsFinite(v) ? (float)v : 0f;
            }
            if (i == 1) got[1] = got[0];
            return got;
        }
    }
}
