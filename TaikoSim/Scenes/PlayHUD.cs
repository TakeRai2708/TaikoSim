using System.Globalization;
using System.Numerics;
using Raylib_cs;
using TaikoSim.Charts;
using TaikoSim.Graphics;

namespace TaikoSim.Scenes
{
    // A-Style の得点・難易度・魂ゲージ。画像の位置は1920×1080基準。
    public sealed class PlayHUD
    {
        private const string Dir = "Image/04.EnsoGame/";
        private const string GaugeBase = Dir + "Gauge/Gauge_Base.png";
        private const string GaugeLight = Dir + "Gauge/Gauge_Light.png";
        private const string GaugeGlow = Dir + "Gauge/Gauge_Light_Glow.png";
        private const string ClearText = Dir + "Gauge/Gauge_Clear_Text.png";
        private const string Rainbow = Dir + "Gauge/Rainbow.png";
        private const string SoulAnime = Dir + "Gauge/Soul_Anime.png";
        private const string SoulText = Dir + "Gauge/Soul_Text.png";
        private const string ScoreBackground = Dir + "MiniTaiko/Background_Score.png";
        private const string ScoreNumber = Dir + "MiniTaiko/Score.png";
        private const string CourseSymbol = Dir + "MiniTaiko/CourseSymbol.png";

        // ゲージの画像は横1195、1段84px。上から1Pの80/70/60%、2P、目盛り。
        private const float GaugeX = 725f, GaugeY = 204f;
        private const float CellWidth = 21f;
        private const int Cells = 50;
        private float elapsed;
        private long shownScore = -1;
        private string scoreText = "0", levelText = "0", courseText = "";

        public void Reset(Course course)
        {
            elapsed = 0;
            shownScore = -1;
            scoreText = "0";
            levelText = "★" + Math.Max(0, course.Level).ToString(CultureInfo.InvariantCulture);
            courseText = Chart.GetCourseName(course.Type);
            // 画像がない場合の文字も、演奏開始前に準備する。
            FontManager.Measure(courseText, 22);
            FontManager.Measure(levelText, 24);
        }

        public void Update(float dt, long score)
        {
            elapsed = (elapsed + dt) % 60f;
            if (shownScore == score) return;
            shownScore = score;
            scoreText = score.ToString(CultureInfo.InvariantCulture);
        }

        public void DrawPanel(Course course)
        {
            SkinManager.Draw(ScoreBackground, 0, 276);
            // 7桁まで元の大きさ。桁が増えても太鼓やパネルの外へはみ出さない。
            float scale = Math.Min(1f, 7f / scoreText.Length);
            if (!DrawDigits(scoreText, 270, 283, scale, true))
                Raylib.DrawText(scoreText, Math.Max(8, 270 - scoreText.Length * 25), 288, 42, Color.White);

            int row = (int)course.Type;
            if (row is >= 0 and < 5 && SkinManager.GetTexture(CourseSymbol) is Texture2D symbol)
            {
                float height = symbol.Height / 5f;
                DrawPart(symbol, new Rectangle(0, row * height, symbol.Width, height), 30, 352);
            }
            else FontManager.Draw(courseText, 30, 370, 22, Color.White);

            FontManager.Draw(levelText, 55, 448, 24, Color.White);
        }

        private static bool DrawDigits(string text, float x, float y, float scale, bool alignRight)
        {
            if (SkinManager.GetTexture(ScoreNumber) is not Texture2D texture) return false;
            float width = texture.Width / 10f, height = texture.Height / 3f;
            float step = 34f * scale;
            if (alignRight) x -= (text.Length - 1) * step + width * scale;
            for (int i = 0; i < text.Length; i++)
            {
                int digit = text[i] - '0';
                if (digit is < 0 or > 9) continue;
                Raylib.DrawTexturePro(texture, new Rectangle(digit * width, 0, width, height),
                    new Rectangle(x + i * step, y, width * scale, height * scale), Vector2.Zero, 0f, Color.White);
            }
            return true;
        }

        public void DrawGauge(PlayStats stats)
        {
            if (SkinManager.GetTexture(GaugeBase) is not Texture2D frame
                || SkinManager.GetTexture(GaugeLight) is not Texture2D light)
            {
                DrawFallbackGauge(stats);
                return;
            }

            int row = stats.Norma <= 60 ? 2 : stats.Norma <= 70 ? 1 : 0;
            int templateNorma = row == 2 ? 60 : row == 1 ? 70 : 80;
            int borderCell = stats.Norma / 2 - 1;
            int templateBorder = templateNorma / 2 - 1;
            float templateStep = 12 + templateBorder * CellWidth;
            float stepX = 12 + borderCell * CellWidth;

            // ★11以上の追加ノルマにも合わせて、背景の段差を移す。
            DrawPart(frame, new Rectangle(0, row * 84, templateStep, 84), GaugeX, GaugeY, stepX, 84);
            DrawPart(frame, new Rectangle(templateStep, row * 84, 1062 - templateStep, 84),
                GaugeX + stepX, GaugeY, 1062 - stepX, 84);
            DrawPart(frame, new Rectangle(1062, row * 84, 133, 84), GaugeX + 1062, GaugeY);

            bool rainbow = stats.IsMax && SkinManager.GetTexture(Rainbow) is Texture2D;
            int filled = Math.Clamp((int)(stats.Gauge / 2), 0, Cells);
            if (rainbow && SkinManager.GetTexture(Rainbow) is Texture2D rainbowTexture)
            {
                // 1056×77 のコマが縦24枚。
                float height = rainbowTexture.Height / 24f;
                int frameIndex = (int)(elapsed / 0.025f) % 24;
                DrawPart(rainbowTexture, new Rectangle(0, frameIndex * height, rainbowTexture.Width, height),
                    GaugeX + 6, GaugeY + 6);
            }
            else
            {
                for (int i = 0; i < filled; i++)
                {
                    Rectangle source;
                    float y;
                    if (i < borderCell) { source = new Rectangle(0, 33, 21, 33); y = GaugeY + 45; }
                    else
                    {
                        int column = i == borderCell ? 3 : i == Cells - 1 ? 5 : 4;
                        source = new Rectangle(column * 21, 0, 21, 66);
                        y = GaugeY + 12;
                    }
                    DrawPart(light, source, GaugeX + 12 + i * CellWidth, y);
                }
            }

            // 背景とは別に入っている黒い目盛りを、点灯した画像の上へ重ねる。
            // 虹の画像には輪郭が含まれる。目盛りを重ねず、連続した虹を見せる。
            if (!rainbow && stats.Norma == templateNorma)
                DrawPart(frame, new Rectangle(0, (6 + row) * 84, frame.Width, 84), GaugeX, GaugeY);
            else if (!rainbow)
            {
                DrawPart(frame, new Rectangle(0, 6 * 84, 12, 84), GaugeX, GaugeY);
                for (int i = 0; i < Cells; i++)
                {
                    int sourceCell = i < borderCell ? 0 : i == borderCell ? 39 : i == 49 ? 49 : 40;
                    DrawPart(frame, new Rectangle(12 + sourceCell * CellWidth, 6 * 84, CellWidth, 84),
                        GaugeX + 12 + i * CellWidth, GaugeY);
                }
                DrawPart(frame, new Rectangle(1062, 6 * 84, 133, 84), GaugeX + 1062, GaugeY);
            }

            if (!rainbow && filled > 0 && SkinManager.GetTexture(GaugeGlow) is Texture2D glow)
            {
                int column = filled - 1 < borderCell ? 0 : filled - 1 == borderCell ? 2 : 3;
                float alpha = 0.45f + 0.2f * MathF.Sin(elapsed * 8f);
                Raylib.DrawTexturePro(glow, new Rectangle(column * 30, 0, 30, 83),
                    new Rectangle(GaugeX + 12 + (filled - 1) * CellWidth - 4.5f, GaugeY + 9, 30, 83),
                    Vector2.Zero, 0f, new Color(255, 255, 255, (int)(alpha * 255)));
            }

            if (SkinManager.GetTexture(ClearText) is Texture2D clear)
            {
                float height = clear.Height / 2f;
                DrawPart(clear, new Rectangle(0, stats.IsClear ? height : 0, clear.Width, height),
                    GaugeX + stepX + 10, GaugeY + 13);
            }

            if (stats.IsMax && SkinManager.GetTexture(SoulAnime) is Texture2D soul)
            {
                float width = soul.Width / 8f;
                int frameIndex = (int)(elapsed / 0.05f) % 8;
                DrawPart(soul, new Rectangle(frameIndex * width, 0, width, soul.Height),
                    1798, 110, width * 0.5f, soul.Height * 0.5f);
            }
            if (SkinManager.GetTexture(SoulText) is Texture2D text)
            {
                float height = text.Height / 3f;
                DrawPart(text, new Rectangle(0, (stats.IsMax ? 2 : 1) * height, text.Width, height), 1810, 207);
            }
            else Raylib.DrawText("SOUL", 1805, 232, 28, Color.White);
        }

        private static void DrawFallbackGauge(PlayStats stats)
        {
            Raylib.DrawRectangle((int)GaugeX, 230, 1062, 50, Color.Black);
            Raylib.DrawRectangle((int)GaugeX + 6, 236, 1050, 38, new Color(75, 20, 20, 255));
            Raylib.DrawRectangle((int)GaugeX + 6, 236, (int)(1050 * stats.Gauge / 100), 38,
                stats.IsClear ? Color.Gold : Color.Red);
            int border = (int)(GaugeX + 6 + 1050 * stats.Norma / 100);
            Raylib.DrawRectangle(border, 230, 3, 50, Color.White);
            Raylib.DrawText(stats.IsClear ? "CLEAR" : "SOUL", 1800, 238, 28, Color.White);
        }

        private static void DrawPart(Texture2D texture, Rectangle source, float x, float y,
            float? width = null, float? height = null)
        {
            Raylib.DrawTexturePro(texture, source,
                new Rectangle(x, y, width ?? source.Width, height ?? source.Height), Vector2.Zero, 0f, Color.White);
        }
    }
}
