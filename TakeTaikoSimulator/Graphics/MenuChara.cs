using Raylib_cs;

namespace TakeTaikoSimulator.Graphics
{
    // メニューの画面（モード選択・選曲・段位選択・リザルト）のどんちゃん
    //
    //   演奏画面とちがって、こっちは BPM ではなく「1周○ミリ秒」で回す（Config.json の ○○LoopTime。TaikøNauts のドキュメントのとおり）。
    //   11/8 に TaikøNauts を録画して、コマを1枚ずつ照らし合わせて確かめた（ずれはほぼ 0）：
    //     ・モード選択・選曲：songselect_loop（236コマ）を 1000ms で1周。位置 (-185, 436)、1倍。放っておいても wait_loop にはならない
    //       （songselect_wait_loop は2人で遊ぶときの「相手待ち」。OpenTaiko の WAIT と同じ）
    //     ・段位選択：songselect_loop を (-190, 445)、1倍。右側はパネルに隠れる
    //     ・リザルト：result_loop（117コマ）を 500ms で1周、(-70, 538)、0.75倍。
    //       吹き出し（クリアだドン！）が出たら、クリアは result_clear_loop（156コマ・700ms）
    //       失敗は result_failed_in（200ms）→ result_failed_loop（500ms）※失敗のほうはまだ録画で見ていない
    //     ・曲を決めたとき：10combo を 360ms で元の66コマめまで進めて、そこで止める（PlayHold。songSelectSongStartTime・JumpIndex）
    //   まだやっていない：段位リザルト
    public sealed class MenuChara : IDisposable
    {
        private CharaStream? stream;
        private readonly Dictionary<int, CharaStream> otherStreams = new();
        private int generation = -1;
        private string anim = "";
        private double loopSeconds = 1;
        private double clock;
        private string? next;          // 1回だけのアニメのあとに続けるアニメ
        private double nextLoopSeconds = 1;
        private int holdAt = -1;       // 0以上なら、元のコマ番号 holdAt まで進めて止める（PlayHold）

        public CharaConfig? Config => CharaBaker.Config;

        public void Load()
        {
            Dispose();
            generation = CharaBaker.Generation;
            if (CharaBaker.Config == null || CharaBaker.CacheFolder == null) return;
            stream = new CharaStream(CharaBaker.CacheFolder);
        }

        /// <summary>そのアニメが焼けていて出せるか（シンプルなど、どんちゃんがいないスキンでは false）</summary>
        public bool HasAnim(string name) => stream != null && !SkinManager.SystemSkin && stream.Has(name);

        public void Dispose()
        {
            stream?.Dispose();
            stream = null;
            foreach (var other in otherStreams.Values) other.Dispose();
            otherStreams.Clear();
        }

        /// <summary>くり返すアニメにする（同じアニメなら続きから）</summary>
        public void Loop(string name, int loopMs)
        {
            if (anim == name && next == null && holdAt < 0) return;
            anim = name;
            loopSeconds = Math.Max(0.05, loopMs / 1000.0);
            clock = 0;
            next = null;
            holdAt = -1;
        }

        /// <summary>name を最初から、元のコマ番号 holdIndex まで toMs で進めて、そこで止める（曲を決めたときのジャンプ）</summary>
        public void PlayHold(string name, int toMs, int holdIndex)
        {
            anim = name;
            loopSeconds = Math.Max(0.05, toMs / 1000.0);
            clock = 0;
            next = null;
            holdAt = Math.Max(0, holdIndex);
        }

        /// <summary>first を1回だけ（firstMs で）出して、そのあと then をくり返す</summary>
        public void PlayThen(string first, int firstMs, string then, int thenMs)
        {
            anim = first;
            loopSeconds = Math.Max(0.05, firstMs / 1000.0);
            clock = 0;
            next = then;
            nextLoopSeconds = Math.Max(0.05, thenMs / 1000.0);
            holdAt = -1;
        }

        public void Update(float dt)
        {
            clock += dt;
            if (next != null && clock >= loopSeconds)
            {
                anim = next;
                loopSeconds = nextLoopSeconds;
                clock -= loopSeconds;
                if (clock < 0) clock = 0;
                next = null;
            }
        }

        public void DrawPlayers(float[] xs, float[] ys, float scale, IReadOnlyList<int> players)
        {
            if (generation != CharaBaker.Generation) Load();
            bool entry = ReferenceEquals(xs, Config?.EntryX);
            foreach (int side in players)
            {
                int slot = TakeTaikoSimulator.Scenes.LocalSession.Profiles[side];
                CharaConfig? config = CharaBaker.ConfigFor(slot);
                if (config == null || CharaBaker.CacheFor(slot) is not string cache) continue;
                CharaStream? own = stream;
                if (slot != TakeTaikoSimulator.Scenes.LocalSession.Profiles[TakeTaikoSimulator.Scenes.LocalSession.Controller])
                {
                    if (!otherStreams.TryGetValue(slot, out own)) otherStreams[slot] = own = new CharaStream(cache);
                }
                if (own == null) continue;
                float[] px = entry ? config.EntryX : config.SongSelectX;
                float[] py = entry ? config.EntryY : config.SongSelectY;
                float ownScale = entry ? config.EntryScale : config.SongSelectScale;
                float x = px[0], y = py[0];
                if (side == 1)
                {
                    x = px.Length > 1 && px[1] != px[0] ? px[1] : 1920 - px[0] - own.SourceWidth(anim) * ownScale;
                    y = py.Length > 1 ? py[1] : py[0];
                }
                DrawStream(own, x, y, ownScale, side == 1, config);
            }
        }

        /// <summary>(x, y) は元の絵（820×599）の左上、scale は倍率（Config.json の値）</summary>
        public void Draw(float x, float y, float scale, bool flip = false)
        {
            if (generation != CharaBaker.Generation) Load();
            if (stream != null) DrawStream(stream, x, y, scale, flip);
        }

        private void DrawStream(CharaStream stream, float x, float y, float scale, bool flip, CharaConfig? timing = null)
        {
            if (SkinManager.SystemSkin || anim.Length == 0) return;
            int count = stream.FrameCount(anim);
            if (count <= 0) return;
            double duration = timing != null && anim == "songselect_loop" ? Math.Max(.05, timing.SongSelectLoopTime / 1000.0) : loopSeconds;
            int hold = timing != null && holdAt >= 0 ? timing.SongSelectSongStartJumpIndex : holdAt;
            if (hold >= 0)
            {
                // 焼いたコマは間引いてあるので、元のコマ番号を焼いたコマの番号に直す
                int source = Math.Max(1, stream.SourceFrameCount(anim));
                double at = Math.Min(hold, clock / Math.Max(.05, timing?.SongSelectSongStartTime / 1000.0 ?? loopSeconds) * hold);
                int held = Math.Clamp((int)(at * count / source), 0, count - 1);
                stream.Draw(anim, held, x, y, scale, Color.White, flip);
                return;
            }
            double phase = next != null ? Math.Min(clock / duration, 0.9999) : clock / duration % 1.0;
            int frame = Math.Clamp((int)(phase * count), 0, count - 1);
            stream.Draw(anim, frame, x, y, scale, Color.White, flip);
        }
    }
}

