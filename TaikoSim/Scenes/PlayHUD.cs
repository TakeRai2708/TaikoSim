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
        private const float ScoreBounceSeconds = 0.15f;
        private const float RainbowFrameSeconds = 1f / 12f;
        private const float GaugeMarkAlpha = 0.28f;
        private float elapsed;
        private float gaugeFlashElapsed;
        private float scoreBounceElapsed = ScoreBounceSeconds;
        private long shownScore = -1;
        private string scoreText = "0", levelText = "0", courseText = "";
        private sealed class ScorePopup
        {
            public string Text = "";
            public float Age;
        }
        // 加算スコア：TaikøNauts の動画(60fps)で測った値
        //   ① 少し右(100px)から、0.08秒でふわっと(だんだん濃く)すべり込んで止まる
        //   ② 0.27秒から0.06秒かけて、全体が少し(6px)上がる
        //   ③ 0.33秒から、一の位・十の位…の順に1桁ずつ(17msおき)上に飛んで消える(1桁0.05秒)
        private const float ScorePopupSlideSeconds = 0.08f;
        private const float ScorePopupSlidePixels = 100f;
        private const float ScorePopupLiftStart = 0.27f, ScorePopupLiftSeconds = 0.06f, ScorePopupLiftPixels = 6f;
        private const float ScorePopupHoldSeconds = 0.33f;
        private const float ScorePopupDigitDelay = 0.017f;
        private const float ScorePopupRiseSeconds = 0.05f;
        private const float ScorePopupRisePixels = 24f;
        private const int MaxScorePopups = 32;
        private readonly List<ScorePopup> scorePopups = new(MaxScorePopups);
        private static readonly string[] ScoreDigitText = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
        private float soulFlashAge = 1f;
        private const float SongTitleRight = 1872f, SongTitleY = 40f;
        private const float SongTitleMaxWidth = 1000f, SongTitleFontSize = 48f, SongTitleOutline = 3f;
        private string songTitle = "";
        private float songTitleX, songTitleSize;
        private int songTitleSkinGeneration = -1;

        public void Reset(Course course, string title = "")
        {
            elapsed = 0;
            gaugeFlashElapsed = 0;
            shownScore = 0;
            scoreBounceElapsed = ScoreBounceSeconds;
            scoreText = "0";
            scorePopups.Clear();
            soulFlashAge = 1f;
            levelText = "★" + Math.Max(0, course.Level).ToString(CultureInfo.InvariantCulture);
            courseText = Chart.GetCourseName(course.Type);
            // 画像がない場合の文字も、演奏開始前に準備する。
            FontManager.Measure(courseText, 22);
            FontManager.Measure(levelText, 24);
            songTitle = title.Replace('\r', ' ').Replace('\n', ' ').Trim();
            FontManager.PrepareSkinMainText(songTitle);
            PrepareSongTitleLayout();
        }

        private void PrepareSongTitleLayout()
        {
            float width = FontManager.MeasureSkinMain(songTitle, SongTitleFontSize).X;
            float scale = width > SongTitleMaxWidth ? SongTitleMaxWidth / width : 1f;
            songTitleSize = SongTitleFontSize * scale;
            songTitleX = SongTitleRight - width * scale;
            songTitleSkinGeneration = SkinManager.Generation;
        }

        public void DrawSongTitle()
        {
            if (songTitle.Length == 0) return;
            // スキン適用時にフォントと必要な文字は準備済み。ここでは幅だけを再計算する。
            if (songTitleSkinGeneration != SkinManager.Generation) PrepareSongTitleLayout();
            float outline = SongTitleOutline;
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                    if (x != 0 || y != 0)
                        FontManager.DrawSkinMain(songTitle, songTitleX + x * outline, SongTitleY + y * outline,
                            songTitleSize, Color.Black);
            FontManager.DrawSkinMain(songTitle, songTitleX, SongTitleY, songTitleSize, Color.White);
        }

        public void Update(float dt, long score)
        {
            elapsed = (elapsed + dt) % 60f;
            gaugeFlashElapsed = (gaugeFlashElapsed + dt) % 0.532f;
            scoreBounceElapsed = Math.Min(ScoreBounceSeconds, scoreBounceElapsed + dt);
            soulFlashAge += dt;
            for (int i = scorePopups.Count - 1; i >= 0; i--)
            {
                scorePopups[i].Age += dt;
                // 左端の桁まで消え終わってから片付ける。
                float life = ScorePopupHoldSeconds + ScorePopupRiseSeconds
                    + Math.Max(0, scorePopups[i].Text.Length - 1) * ScorePopupDigitDelay;
                if (scorePopups[i].Age >= life) scorePopups.RemoveAt(i);
            }
            if (shownScore == score) return;
            shownScore = score;
            scoreText = score.ToString(CultureInfo.InvariantCulture);
            scoreBounceElapsed = 0f;
        }

        public void OnScoreAdded(long points, long total)
        {
            shownScore = total;
            scoreText = total.ToString(CultureInfo.InvariantCulture);
            scoreBounceElapsed = 0f;
            if (scorePopups.Count >= MaxScorePopups) scorePopups.RemoveAt(0);
            scorePopups.Add(new ScorePopup { Text = points.ToString(CultureInfo.InvariantCulture) });
        }

        public void OnSoulArrival() => soulFlashAge = 0f;

        public void DrawPanel(Course course)
        {
            SkinManager.Draw(ScoreBackground, 0, 276);
            // 7桁まで元の大きさ。桁が増えても太鼓やパネルの外へはみ出さない。
            float scale = Math.Min(1f, 7f / scoreText.Length);
            float bounce = 0.15f * MathF.Sin(MathF.PI * scoreBounceElapsed / ScoreBounceSeconds);
            if (!DrawDigits(scoreText, 272, 287, scale, bounce, true))
                Raylib.DrawText(scoreText, Math.Max(8, 272 - scoreText.Length * 25), 292, 42, Color.White);
            DrawScoreAdditions();

            int row = (int)course.Type;
            if (row is >= 0 and < 5 && SkinManager.GetTexture(CourseSymbol) is Texture2D symbol)
            {
                float height = symbol.Height / 5f;
                DrawPart(symbol, new Rectangle(0, row * height, symbol.Width, height), 27, 351);
            }
            else FontManager.Draw(courseText, 30, 370, 22, Color.White);

            FontManager.Draw(levelText, 55, 448, 24, Color.White);
        }

        private static bool DrawDigits(string text, float x, float y, float scale, float bounce, bool alignRight)
        {
            if (SkinManager.GetTexture(ScoreNumber) is not Texture2D texture) return false;
            float width = texture.Width / 10f, height = texture.Height / 3f;
            float step = 34f * scale;
            // 下端を固定して縦だけ伸ばす。加点直後から約150msで元の高さに戻る。
            float drawHeight = height * scale * (1f + bounce);
            float drawY = y + height * scale - drawHeight;
            if (alignRight) x -= (text.Length - 1) * step + width * scale;
            for (int i = 0; i < text.Length; i++)
            {
                int digit = text[i] - '0';
                if (digit is < 0 or > 9) continue;
                Raylib.DrawTexturePro(texture, new Rectangle(digit * width, 0, width, height),
                    new Rectangle(x + i * step, drawY, width * scale, drawHeight), Vector2.Zero, 0f, Color.White);
            }
            return true;
        }

        private void DrawScoreAdditions()
        {
            Texture2D? numberTexture = SkinManager.GetTexture(ScoreNumber);
            float width = numberTexture is Texture2D digits ? digits.Width / 10f : 20f;
            float height = numberTexture is Texture2D image ? image.Height / 3f : 32f;
            float spacing = numberTexture.HasValue ? 34f : 20f;
            foreach (ScorePopup popup in scorePopups)
            {
                // 合計点と同じ右端にそろえる。出てくるときだけ右からすべり込む
                float slide = Math.Clamp(popup.Age / ScorePopupSlideSeconds, 0f, 1f);
                float slideEase = 1f - (1f - slide) * (1f - slide); // 最後はゆっくり止まる
                float offsetX = ScorePopupSlidePixels * (1f - slideEase);
                float lift = ScorePopupLiftPixels * Math.Clamp((popup.Age - ScorePopupLiftStart) / ScorePopupLiftSeconds, 0f, 1f);
                float x = 272 - (popup.Text.Length - 1) * spacing - width + offsetX;
                for (int i = 0; i < popup.Text.Length; i++)
                {
                    int digit = popup.Text[i] - '0';
                    if (digit is < 0 or > 9) continue;
                    int place = popup.Text.Length - 1 - i; // 0が一の位
                    float riseAge = popup.Age - ScorePopupHoldSeconds - place * ScorePopupDigitDelay;
                    float progress = Math.Clamp(riseAge / ScorePopupRiseSeconds, 0f, 1f);
                    if (progress >= 1f) continue;
                    float y = 235 - lift - ScorePopupRisePixels * progress;
                    float alpha = (1f - progress) * slide; // 出てくるときはだんだん濃く
                    if (numberTexture is Texture2D texture)
                    {
                        Raylib.DrawTexturePro(texture, new Rectangle(digit * width, height, width, height),
                            new Rectangle(x + i * spacing, y, width, height), Vector2.Zero, 0f,
                            Raylib.ColorAlpha(Color.White, alpha));
                    }
                    else Raylib.DrawText(ScoreDigitText[digit], (int)(x + i * spacing), (int)y, 32,
                        Raylib.ColorAlpha(Color.Orange, alpha));
                }
            }
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
                // 24枚は80/70/60%の3組×8コマ。組をまたぐと段差まで移動してしまう。
                // TaikøNauts は色がなめらかに流れていたので、今のコマに次のコマを少しずつ重ねてつなぐ
                float height = rainbowTexture.Height / 24f;
                float framePosition = elapsed / RainbowFrameSeconds;
                int current = (int)framePosition % 8;
                int next = (current + 1) % 8;
                float blend = framePosition - MathF.Floor(framePosition); // 次のコマの濃さ 0〜1
                DrawRainbowFrame(rainbowTexture, row * 8 + current, height, Color.White);
                DrawRainbowFrame(rainbowTexture, row * 8 + next, height, Raylib.ColorAlpha(Color.White, blend));

                void DrawRainbowFrame(Texture2D texture, int frameIndex, float frameHeight, Color tint)
                {
                    if (stats.Norma == templateNorma)
                        DrawPart(texture, new Rectangle(0, frameIndex * frameHeight, texture.Width, frameHeight),
                            GaugeX + 6, GaugeY + 6, tint: tint);
                    else
                    {
                        // ★11以上でも背景・目盛りと同じ場所に段差を置く。
                        float sourceStep = templateStep - 6, targetStep = stepX - 6;
                        DrawPart(texture, new Rectangle(0, frameIndex * frameHeight, sourceStep, frameHeight),
                            GaugeX + 6, GaugeY + 6, targetStep, frameHeight, tint);
                        DrawPart(texture, new Rectangle(sourceStep, frameIndex * frameHeight, texture.Width - sourceStep, frameHeight),
                            GaugeX + 6 + targetStep, GaugeY + 6, texture.Width - targetStep, frameHeight, tint);
                    }
                }
            }
            else
            {
                // クリア後は赤い区間全体が黄色く光る。532ms周期、83msずつで点灯・消灯。
                float phase = gaugeFlashElapsed * 1000f;
                float flash = !stats.IsClear || phase <= 365f ? 0f
                    : phase < 448f ? (phase - 365f) / 83f : (531f - phase) / 83f;
                flash = Math.Clamp(flash, 0f, 1f);
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
                    if (i < borderCell && flash > 0f)
                        DrawPart(light, new Rectangle(42, 33, 21, 33), GaugeX + 12 + i * CellWidth, y,
                            tint: Raylib.ColorAlpha(Color.White, flash));
                }
            }

            // 枠内のぼかしを残した光を重ねる。色と強さはスキンの画像に合わせる。
            if (!rainbow && filled > 0)
            {
                int cell = filled - 1;
                int column = cell < borderCell ? 0 : cell == borderCell ? 3 : cell == Cells - 1 ? 5 : 4;
                if (SkinManager.GetGaugeGlowCell(column) is Texture2D glow)
                {
                    DrawPart(glow, new Rectangle(0, 0, glow.Width, glow.Height),
                        GaugeX + 12 + cell * CellWidth - 6, GaugeY + 3);
                }
            }

            // 虹にも目盛りは重なる。黒線が強すぎないように約28%で合成する。
            Color markTint = Raylib.ColorAlpha(Color.White, GaugeMarkAlpha);
            if (stats.Norma == templateNorma)
                DrawPart(frame, new Rectangle(0, (6 + row) * 84, frame.Width, 84), GaugeX, GaugeY, tint: markTint);
            else
            {
                DrawPart(frame, new Rectangle(0, 6 * 84, 12, 84), GaugeX, GaugeY, tint: markTint);
                for (int i = 0; i < Cells; i++)
                {
                    int sourceCell = i < borderCell ? 0 : i == borderCell ? 39 : i == 49 ? 49 : 40;
                    DrawPart(frame, new Rectangle(12 + sourceCell * CellWidth, 6 * 84, CellWidth, 84),
                        GaugeX + 12 + i * CellWidth, GaugeY, tint: markTint);
                }
                DrawPart(frame, new Rectangle(1062, 6 * 84, 133, 84), GaugeX + 1062, GaugeY, tint: markTint);
            }

            if (SkinManager.GetTexture(ClearText) is Texture2D clear)
            {
                float height = clear.Height / 2f;
                DrawPart(clear, new Rectangle(0, stats.IsClear ? height : 0, clear.Width, height),
                    GaugeX + stepX - 7, GaugeY + 9);
            }

            if (stats.IsMax && SkinManager.GetTexture(SoulAnime) is Texture2D soul)
            {
                float width = soul.Width / 8f;
                int frameIndex = (int)(elapsed / 0.05f) % 8;
                DrawPart(soul, new Rectangle(frameIndex * width, 0, width, soul.Height),
                    1764, 89, width, soul.Height);
            }
            if (SkinManager.GetTexture(SoulText) is Texture2D text)
            {
                float height = text.Height / 3f;
                DrawPart(text, new Rectangle(0, height, text.Width, height), 1792, 204);
                // 満タンで常時発光させず、飛んだ音符が到着したときだけ光を重ねる。
                if (soulFlashAge < 0.16f)
                    DrawPart(text, new Rectangle(0, 2 * height, text.Width, height), 1792, 204,
                        tint: Raylib.ColorAlpha(Color.White, 1f - soulFlashAge / 0.16f));
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
            float? width = null, float? height = null, Color? tint = null)
        {
            Raylib.DrawTexturePro(texture, source,
                new Rectangle(x, y, width ?? source.Width, height ?? source.Height), Vector2.Zero, 0f, tint ?? Color.White);
        }
    }
}