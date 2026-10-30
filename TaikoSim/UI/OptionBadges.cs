using Raylib_cs;
using TaikoSim.Configs;
using static TaikoSim.UI.MenuDraw;

namespace TaikoSim.UI
{
    // 演奏オプションのバッジ(「AUTO」「×1.5」など)。
    //   A-Style の Image/99.Common/Option_Icon/ の画像を使う。1コマ 40×40 を横に並べた画像で、
    //   コマ0 は空っぽ(＝オフ)、コマ1 から中身が入っている。
    //     Auto.png        80×40  …… 2コマ（空 / AUTO）
    //     Speed.png      600×40  …… 15コマ（空 / ×1.1 1.2 1.3 1.4 1.5 1.6 1.7 1.8 1.9 2.0 2.5 3.0 3.5 4.0）
    //     Abekobe / Doron / HiSpeedLock / Adjust は 80×40 の2コマ、Random は 120×40 の3コマ、
    //     PlaySpeed は 480×40 の12コマ（0.5〜0.9 / 空 / 1.1〜1.5 / 2.0）。TaikoSim にその機能が無いので今は使わない。
    //
    //   画像が無いスキン(シンプルなど)では、代わりに文字でバッジを描く。
    public static class OptionBadges
    {
        private const string Folder = "Image/99.Common/Option_Icon/";
        private const float Cell = 40f;   // 画像1コマの大きさ
        private const float Gap = 6f;     // バッジどうしのすき間(原寸のとき)

        // Speed.png のコマ1から順に、どの倍率のバッジかを並べたもの
        //   TaikoSim の HiSpeedSteps から 0.5 と 1.0 を抜いたものと同じ(0.5・1.0 はバッジを出さない)
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

        /// <summary>今つけているオプションのバッジを、右はしをそろえて描く。描いた幅を返す</summary>
        public static float DrawRightAligned(float right, float y, float scale = 1f)
        {
            List<(string File, int CellIndex, string Text)> badges = new();

            // はやさ(1.0 倍のときは出さない。0 は「譜面どおり」＝1.0倍)
            float hiSpeed = Cfg.HiSpeed > 0f ? Cfg.HiSpeed : 1f;
            int speedCell = SpeedCell(hiSpeed);
            if (speedCell > 0)
                badges.Add(("Speed.png", speedCell, $"×{hiSpeed.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture)}"));

            // オート演奏
            if (Cfg.AutoPlay) badges.Add(("Auto.png", 1, "AUTO"));

            if (badges.Count == 0) return 0f;

            float size = Cell * scale, gap = Gap * scale;
            float total = badges.Count * size + (badges.Count - 1) * gap;
            float x = right - total;

            foreach ((string file, int cell, string text) in badges)
            {
                Rectangle dest = new(x, y, size, size);
                bool drawn = MenuSkin.Part(Folder + file,
                    new Rectangle(cell * Cell, 0f, Cell, Cell), dest, Color.White);
                if (!drawn) DrawTextBadge(dest, text);
                x += size + gap;
            }
            return total;
        }

        // 画像が無いスキン用。黄色い角丸の札に、黒っぽい字で書く(A-Style の Speed バッジに寄せた色)
        private static void DrawTextBadge(Rectangle box, string text)
        {
            float radius = box.Height * 0.22f;
            Raylib.DrawRectangleRounded(box, radius / box.Height * 2f, 6, new Color(20, 20, 24, 255));
            Rectangle inner = new(box.X + 3f, box.Y + 3f, box.Width - 6f, box.Height - 6f);
            Raylib.DrawRectangleRounded(inner, radius / box.Height * 2f, 6, new Color(250, 208, 0, 255));
            Outlined(text, inner, inner.Height * 0.46f, new Color(24, 24, 28, 255), new Color(255, 255, 255, 220));
        }
    }
}
