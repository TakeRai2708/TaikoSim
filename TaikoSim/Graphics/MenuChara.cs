using Raylib_cs;

namespace TaikoSim.Graphics
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
    //   まだやっていない：曲を決めたときのジャンプ（songSelectSongStartJumpIndex）、段位リザルト
    public sealed class MenuChara : IDisposable
    {
        private CharaStream? stream;
        private string anim = "";
        private double loopSeconds = 1;
        private double clock;
        private string? next;          // 1回だけのアニメのあとに続けるアニメ
        private double nextLoopSeconds = 1;

        public CharaConfig? Config => CharaBaker.Config;

        public void Load()
        {
            Dispose();
            if (CharaBaker.Config == null || CharaBaker.CacheFolder == null) return;
            stream = new CharaStream(CharaBaker.CacheFolder);
        }

        public void Dispose()
        {
            stream?.Dispose();
            stream = null;
        }

        /// <summary>くり返すアニメにする（同じアニメなら続きから）</summary>
        public void Loop(string name, int loopMs)
        {
            if (anim == name && next == null) return;
            anim = name;
            loopSeconds = Math.Max(0.05, loopMs / 1000.0);
            clock = 0;
            next = null;
        }

        /// <summary>first を1回だけ（firstMs で）出して、そのあと then をくり返す</summary>
        public void PlayThen(string first, int firstMs, string then, int thenMs)
        {
            anim = first;
            loopSeconds = Math.Max(0.05, firstMs / 1000.0);
            clock = 0;
            next = then;
            nextLoopSeconds = Math.Max(0.05, thenMs / 1000.0);
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

        /// <summary>(x, y) は元の絵（820×599）の左上、scale は倍率（Config.json の値）</summary>
        public void Draw(float x, float y, float scale)
        {
            if (stream == null || SkinManager.SystemSkin || anim.Length == 0) return;
            int count = stream.FrameCount(anim);
            if (count <= 0) return;
            double phase = next != null ? Math.Min(clock / loopSeconds, 0.9999) : clock / loopSeconds % 1.0;
            int frame = Math.Clamp((int)(phase * count), 0, count - 1);
            stream.Draw(anim, frame, x, y, scale, Color.White);
        }
    }
}
