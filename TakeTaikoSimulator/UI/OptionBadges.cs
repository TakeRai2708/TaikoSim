using Raylib_cs;
using TakeTaikoSimulator.Configs;
using static TakeTaikoSimulator.UI.MenuDraw;

using TakeTaikoSimulator.Graphics;
namespace TakeTaikoSimulator.UI
{
    // 演奏オプションのバッジ(「AUTO」「×1.5」など)。
    //   A-Style の Image/99.Common/Option_Icon/ の画像を使う。1コマ 40×40 を横に並べた画像で、
    //   コマ0 は空っぽ(＝オフ)、コマ1 から中身が入っている。
    //     Auto.png        80×40  …… 2コマ（空 / AUTO）
    //     Speed.png      600×40  …… 15コマ（空 / ×1.1 1.2 1.3 1.4 1.5 1.6 1.7 1.8 1.9 2.0 2.5 3.0 3.5 4.0）
    //     Abekobe / Doron / HiSpeedLock / Adjust は 80×40 の2コマ、Random は 120×40 の3コマ（空 / きまぐれ / でたらめ）、
    //     PlaySpeed は 480×40 の12コマ（0.5〜0.9 / 空 / 1.1〜1.5 / 2.0）。
    //     11/12：ドロン・あべこべ・ランダム・演奏速度を作ったので、これらも出す（HiSpeedLock・Adjust は機能が無いので出さない）
    //
    //   画像が無いスキン(シンプルなど)では、代わりに文字でバッジを描く。
    public static class OptionBadges
    {
        private const string Folder = "Image/99.Common/Option_Icon/";
        private const float Cell = 40f;   // 画像1コマの大きさ
        private const float Gap = 6f;     // バッジどうしのすき間(原寸のとき)

        // Speed.png のコマ1から順に、どの倍率のバッジかを並べたもの
        //   TakeTaikoSimulator の HiSpeedSteps から 0.5 と 1.0 を抜いたものと同じ(0.5・1.0 はバッジを出さない)
        private static readonly float[] SpeedValues =
            { 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.7f, 1.8f, 1.9f, 2.0f, 2.5f, 3.0f, 3.5f, 4.0f };

        private static GameConfig Cfg => ConfigManager.Game;

        /// <summary>はやさ倍率 → Speed.png のコマ番号。1.0 以下や、どの段階でもないときは 0(＝出さない)</summary>
        private static int SpeedCell(float hiSpeed)
        {
            if (hiSpeed <= 1.0f) return 0;
            for (int i = 0; i < SpeedValues.Length; i++)
                if (Math.Abs(SpeedValues[i] - hiSpeed) < 0.0001f) return i + 1;

            // 段階にない値(数字で直接入れたとき)は、一番近い段階のバッジにする
            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < SpeedValues.Length; i++)
            {
                float d = Math.Abs(SpeedValues[i] - hiSpeed);
                if (d < best) { best = d; nearest = i + 1; }
            }
            return nearest;
        }

        // 並べる順（11/12 たけらいのスクショ：TaikøNauts で全部つけたときの並び）
        //   はやさ → ドロン → あべこべ → ランダム → AUTO → 演奏速度。つけていない物は詰める
        private static List<(string File, int CellIndex, string Text)> Collect(GameConfig cfg)
        {
            List<(string File, int CellIndex, string Text)> badges = new();
            var inv = System.Globalization.CultureInfo.InvariantCulture;

            // はやさ(1.0 倍のときは出さない。0 は「譜面どおり」＝1.0倍)
            float hiSpeed = cfg.HiSpeed > 0f ? cfg.HiSpeed : 1f;
            int speedCell = SpeedCell(hiSpeed);
            if (speedCell > 0) badges.Add(("Speed.png", speedCell, $"×{hiSpeed.ToString("0.0#", inv)}"));

            if (cfg.Doron) badges.Add(("Doron.png", 1, "ドロン"));
            if (cfg.Abekobe) badges.Add(("Abekobe.png", 1, "あべ"));
            // Random.png：空 / きまぐれ / でたらめ
            if (cfg.Random == NoteRandom.Kimagure) badges.Add(("Random.png", 1, "きま"));
            else if (cfg.Random == NoteRandom.Detarame) badges.Add(("Random.png", 2, "でた"));

            // オート演奏
            if (cfg.AutoPlay) badges.Add(("Auto.png", 1, "AUTO"));

            // 演奏速度。PlaySpeed.png は 0.5〜0.9 / 空（1.0）/ 1.1〜1.5 / 2.0 の12コマ ＝ GameConfig.PlaySpeeds と同じ順
            int playCell = Array.FindIndex(GameConfig.PlaySpeeds, s => Math.Abs(s - cfg.PlaySpeed) < 0.001f);
            if (playCell >= 0 && Math.Abs(cfg.PlaySpeed - 1f) > 0.001f)
                badges.Add(("PlaySpeed.png", playCell, $"♪{cfg.PlaySpeed.ToString("0.0", inv)}"));
            return badges;
        }

        /// <summary>今つけているオプションのバッジを、左はしをそろえて右へ並べる。描いた幅を返す</summary>
        public static float DrawLeftAligned(float left, float y, float scale = 1f) => DrawGrid(Cfg, left, y, scale, int.MaxValue);

        /// <summary>
        /// cfg のオプションのバッジを、左上 (left, top) から右へ並べる。perRow 個で次の段へ。描いた幅を返す
        ///   演奏画面：3個ずつ2段。選曲：名札の下に横1列
        /// </summary>
        public static float DrawGrid(GameConfig cfg, float left, float top, float scale, int perRow, float gap = Gap, float alpha = 1f)
        {
            var badges = Collect(cfg);
            if (badges.Count == 0) return 0f;
            float size = Cell * scale, step = size + gap * scale;
            Color tint = Fade(Color.White, alpha);
            for (int i = 0; i < badges.Count; i++)
            {
                (string file, int cell, string text) = badges[i];
                Rectangle dest = new(left + i % perRow * step, top + i / perRow * step, size, size);
                bool drawn = MenuSkin.Part(Folder + file, new Rectangle(cell * Cell, 0f, Cell, Cell), dest, tint);
                if (!drawn && alpha > 0.5f) DrawTextBadge(dest, text);
            }
            int columns = Math.Min(badges.Count, perRow);
            return columns * size + (columns - 1) * gap * scale;
        }

        /// <summary>今つけているオプションのバッジを、右はしをそろえて描く。描いた幅を返す</summary>
        public static float DrawRightAligned(float right, float y, float scale = 1f)
        {
            int count = Collect(Cfg).Count;
            if (count == 0) return 0f;
            float total = count * Cell * scale + (count - 1) * Gap * scale;
            return DrawGrid(Cfg, right - total, y, scale, int.MaxValue);
        }

        // 画像が無いスキン用。黄色い角丸の札に、黒っぽい字で書く(A-Style の Speed バッジに寄せた色)
        private static void DrawTextBadge(Rectangle box, string text)
        {
            float radius = box.Height * 0.22f;
            SmoothShapes.Rounded(box, radius / box.Height * 2f, 6, new Color(20, 20, 24, 255));
            Rectangle inner = new(box.X + 3f, box.Y + 3f, box.Width - 6f, box.Height - 6f);
            SmoothShapes.Rounded(inner, radius / box.Height * 2f, 6, new Color(250, 208, 0, 255));
            Outlined(text, inner, inner.Height * 0.46f, new Color(24, 24, 28, 255), new Color(255, 255, 255, 220));
        }
    }
}
