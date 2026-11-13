using System.Globalization;
using System.Numerics;
using Raylib_cs;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.UI;

namespace TakeTaikoSimulator.Scenes
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
        // ゲージの左上(リザルト画面で小さくして描くときに使う)
        public const float GaugeLeft = GaugeX, GaugeTop = GaugeY;
        private const float CellWidth = 21f;
        private const int Cells = 50;
        private const float ScoreBounceSeconds = 0.15f;
        private const float RainbowFrameSeconds = 1f / 12f;
        private const float GaugeMarkAlpha = 0.28f;
        private float elapsed;
        private float gaugeFlashElapsed;

        // 魂ゲージの光り方。ゲーセンの録画（11/6 たけらい、2026-11-06_09-19-28.mp4）を1コマずつ測った値
        //   ① クリア後に赤い区間ぜんぶが黄色く光る：0.5秒ごと（クリアした瞬間から数える）。
        //      1回の中で 0.27秒までは光らない → 0.06秒で黄色に → 0.17秒かけて戻る。
        //      38回の間隔が 0.501秒（曲の拍とは関係なかった。小節は 0.983秒）。
        //      OpenTaiko は 532ms ごと（365ms まで消えて、83ms で明るく、83ms で戻る）で、TakeTaikoSimulator も前はそれだった
        //   ② 赤いマスが増えたとき：増えたマスだけオレンジに光る（35回の真ん中の値）。
        //      0.05秒で ふつうの赤 (248,54,12) とオレンジ (255,140,34) の半分の色 (251,98,23) になって、
        //      0.12秒までそのまま → 0.48秒にかけて同じ速さで赤に戻る。となりのマスやマスの外は光らない
        //   ③ 黄色いマス（ボーダーより先）は、増えても光らない
        //   ④ いちばん先のマスにずっと光が乗っていることはない（前は 10/20 にスキンの光を乗せていた。11/6 に外した）
        private const float ClearFlashPeriod = 0.5f, ClearFlashRiseAt = 0.27f, ClearFlashPeakAt = 0.33f;
        private const float NewCellRise = 0.05f, NewCellHold = 0.12f, NewCellEnd = 0.48f, NewCellPeakAlpha = 0.5f;
        private static readonly Color NewCellColor = new(255, 140, 34, 255);
        private readonly float[] cellAge = CreateCellAges();
        private int lastFilled;
        private bool wasClear;
        private static Texture2D cellMask;       // 赤いマスの形だけを白くしたもの（オレンジを重ねるのに使う。全部の演奏で1枚）
        private static uint cellMaskSource;
        private float scoreBounceElapsed = ScoreBounceSeconds;
        private long shownScore = -1;
        private string scoreText = "0", courseText = "";
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
        // 演奏オプションのバッジ(AUTO・×1.5 など)は、左のパネルの難易度マークの右に原寸で出す。
        //   TaikøNauts の動画（10/31 の段位道場、AUTO を付けた演奏）で測った：AUTO の 40×40 の左上が (170, 353)。
        //   前は推測で曲名の下に置いていた
        private const float OptionBadgeX = 170f, OptionBadgeY = 353f, OptionBadgeScale = 1f;
        private const float SongTitleMaxWidth = 1000f, SongTitleFontSize = 48f, SongTitleOutline = 4f; // 輪郭は 4px(3px だと細いフォントで目立たなかった)
        private string songTitle = "";
        private float songTitleX, songTitleSize;
        private int songTitleSkinGeneration = -1;

        public void Reset(Course course, string title = "")
        {
            elapsed = 0;
            gaugeFlashElapsed = 0;
            Array.Fill(cellAge, NewCellEnd);
            lastFilled = 0;
            wasClear = false;
            shownScore = 0;
            scoreBounceElapsed = ScoreBounceSeconds;
            scoreText = "0";
            scorePopups.Clear();
            soulFlashAge = 1f;
            courseText = Chart.GetCourseName(course.Type);
            // 画像がない場合の文字も、演奏開始前に準備する。
            FontManager.Measure(courseText, 22);
            songTitle = title.Replace('\r', ' ').Replace('\n', ' ').Trim();
            FontManager.PrepareSkinMainText(songTitle);
            FontManager.PreloadDetail(new[] { songTitle });
            PrepareSongTitleLayout();
        }

        // 11/13：曲名はスキンの細かい字のフォント（Default2.ttf）に黒い 6px の縁（TaikøNauts の録画で、縁こみの右はし x 1869・
        //   高さ y 34〜95。無いスキンは今までどおり太い字）
        private static bool TitleUsesDetail => FontManager.HasSkinDetail;
        private const float DetailTitleOutline = 6f;

        private void PrepareSongTitleLayout()
        {
            float width = TitleUsesDetail ? FontManager.MeasureDetail(songTitle, SongTitleFontSize).X
                : FontManager.MeasureSkinMain(songTitle, SongTitleFontSize).X;
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
            // 黒い縁 → 白い文字(SDF フォントで2回。前は8方向にずらして9回描いていた)
            if (TitleUsesDetail)
                FontManager.DrawDetailOutlined(songTitle, songTitleX, SongTitleY, songTitleSize, Color.White, Color.Black,
                    Math.Min(DetailTitleOutline * songTitleSize / SongTitleFontSize, DynamicFont.MaxOutline(songTitleSize)));
            else FontManager.DrawSkinMainOutlined(songTitle, songTitleX, SongTitleY, songTitleSize, Color.White, Color.Black, SongTitleOutline);
        }

        public void Update(float dt, long score)
        {
            elapsed = (elapsed + dt) % 60f;
            gaugeFlashElapsed = (gaugeFlashElapsed + dt) % ClearFlashPeriod;
            for (int i = 0; i < cellAge.Length; i++)
                if (cellAge[i] < NewCellEnd) cellAge[i] += dt;
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

        // 段位のときの左のパネルの背景（A-Style の 08.DanGame。九段の動画で (0, 276)）
        private const string DanScoreBackground = "Image/08.DanGame/MiniTaiko/Background.png";
        private const string DanCourseSymbol = "Image/08.DanGame/MiniTaiko/CourseSymbol.png";
        private const string DanStopper = "Image/08.DanGame/Gauge/Condition_Stopper.png";

        public void DrawPanel(Course course, bool dan = false)
        {
            // 段位は段位用のパネルの絵（08.DanGame）。スコアのうしろの暗い板（Background_Score）はどちらでも上に重ねる
            //   11/7 たけらい「スコアのバックグラウンドがない」：段位用の絵には板が入っていなかった（本家の六段の動画には (0,284)〜(303,339) にある）
            if (dan) SkinManager.Draw(DanScoreBackground, 0, 276);
            SkinManager.Draw(ScoreBackground, 0, 276);
            // 7桁まで元の大きさ。桁が増えても太鼓やパネルの外へはみ出さない。
            float scale = Math.Min(1f, 7f / scoreText.Length);
            float bounce = 0.15f * MathF.Sin(MathF.PI * scoreBounceElapsed / ScoreBounceSeconds);
            if (!DrawDigits(scoreText, 272, 287, scale, bounce, true))
                Raylib.DrawText(scoreText, Math.Max(8, 272 - scoreText.Length * 25), 292, 42, Color.White);
            DrawScoreAdditions();

            int row = (int)course.Type;
            if (dan) DrawDanSymbol();
            else if (row is >= 0 and < 5 && SkinManager.GetTexture(CourseSymbol) is Texture2D symbol)
            {
                float height = symbol.Height / 5f;
                DrawPart(symbol, new Rectangle(0, row * height, symbol.Width, height), 27, 351);
            }
            else FontManager.Draw(courseText, 30, 370, 22, Color.White);
            // 難易度の★の数は出さない(難易度のマークだけ)

            // つけているオプションのバッジ(何もつけていなければ何も描かない)
            //   11/12 たけらいのスクショ：3個ずつ2段（難易度の印の右。段の間は約42px）
            OptionBadges.DrawGrid(Configs.ConfigManager.Game, OptionBadgeX, OptionBadgeY, OptionBadgeScale, 3, gap: 2f);
        }

        // 段位道場のときの難易度マーク（赤い布の包み＋「段位道場」）
        //   10/31 の九段の動画を測った：包みは x50〜135・y355〜410、字は「段位道場」で y418〜440・まんなか x93。
        //   A-Style の画像は入れないので、形だけコードでまねる（スキンに CourseSymbol_Dan.png があればそれを使う）
        private const string DanSymbol = Dir + "MiniTaiko/CourseSymbol_Dan.png";
        private static void DrawDanSymbol()
        {
            // A-Style の 08.DanGame/MiniTaiko/CourseSymbol.png（136×136）を (27, 351)。九段の動画のコマに重ねて誤差12
            if (SkinManager.Draw(DanCourseSymbol, 27, 351) || SkinManager.Draw(DanSymbol, 27, 351)) return;

            Vector2 c = new(93f, 382f);
            Color cream = new(244, 230, 196, 255), ink = new(40, 26, 20, 255), red = new(206, 52, 28, 255);
            // うしろの四すみ（ななめに置いたクリーム色の四角）
            DrawDiamond(c, 48f, 34f, ink);
            DrawDiamond(c, 43f, 30f, cream);
            // 赤い布（両わきに白い帯と黒い点）
            Rectangle cloth = new(c.X - 32f, c.Y - 22f, 64f, 44f);
            Raylib.DrawRectangleRec(new Rectangle(cloth.X - 2f, cloth.Y - 2f, cloth.Width + 4f, cloth.Height + 4f), ink);
            Raylib.DrawRectangleRec(cloth, red);
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? cloth.X : cloth.X + cloth.Width - 9f;
                Raylib.DrawRectangleRec(new Rectangle(x, cloth.Y, 9f, cloth.Height), Color.RayWhite);
                for (int i = 0; i < 5; i++) SmoothShapes.Circle(new Vector2(x + 4.5f, cloth.Y + 5f + i * 8.5f), 2f, ink);
            }
            // まんなかの金の紋
            SmoothShapes.Circle(c, 9f, ink);
            SmoothShapes.Circle(c, 7f, new Color(236, 182, 60, 255));
            SmoothShapes.Circle(c, 3f, ink);
            MenuDraw.Outlined("段位道場", new Rectangle(c.X - 50f, 414f, 100f, 30f), 22f, Color.White, ink);
        }

        // ひし形（横の半分 w・縦の半分 h）。三角形は頂点の回る向きで消えることがあるので、両回りで描く
        private static void DrawDiamond(Vector2 c, float w, float h, Color color)
        {
            Vector2 top = new(c.X, c.Y - h), right = new(c.X + w, c.Y), bottom = new(c.X, c.Y + h), left = new(c.X - w, c.Y);
            SmoothShapes.Triangle(top, left, bottom, color);
            SmoothShapes.Triangle(top, bottom, left, color);
            SmoothShapes.Triangle(top, bottom, right, color);
            SmoothShapes.Triangle(top, right, bottom, color);
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

        /// <summary>ノルマの境目（段差）の x。段位の「99%以上」の矢印と目印をここに合わせる</summary>
        public static float NormaBorderX(int norma) => GaugeX + 12 + (norma / 2 - 1) * CellWidth;

        // result：リザルトの小さいゲージ。満タンでないときは「魂」を灰色（Soul_Text.png の1段目）にする
        //   11/13 たけらい「リザルトの魂ゲージ」：TaikøNauts の録画（2026-11-11 23-46-22.mp4 90秒）で、
        //   リザルトの満タンでない魂は灰色だった（演奏画面は満タンでなくても白）
        //   showValue：false ならゲージの中身を出さない（枠だけ。リザルトで、中身が出る前）
        //   allowRainbow：false なら満タンでも虹にしない（リザルトで、中身が出た直後の 0.25秒）
        public void DrawGauge(PlayStats stats, bool dan = false, bool result = false, bool showValue = true, bool allowRainbow = true)
        {
            bool isMax = showValue && stats.IsMax, isClear = showValue && stats.IsClear;
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

            bool rainbow = allowRainbow && isMax && SkinManager.GetTexture(Rainbow) is Texture2D;
            int filled = showValue ? Math.Clamp((int)(stats.Gauge / 2), 0, Cells) : 0;

            // 増えたマスを覚える（オレンジに光らせる）。減ったときは数だけ合わせる
            for (int i = lastFilled; i < filled; i++) cellAge[i] = 0f;
            lastFilled = filled;
            // クリアした瞬間から 0.5秒ごとに点滅を数える
            if (isClear && !wasClear) gaugeFlashElapsed = 0f;
            wasClear = isClear;
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
                // クリア後は赤い区間全体が黄色く光る（上の①）
                float phase = gaugeFlashElapsed;
                float flash = !isClear || phase <= ClearFlashRiseAt ? 0f
                    : phase < ClearFlashPeakAt ? (phase - ClearFlashRiseAt) / (ClearFlashPeakAt - ClearFlashRiseAt)
                    : (ClearFlashPeriod - phase) / (ClearFlashPeriod - ClearFlashPeakAt);
                flash = Math.Clamp(flash, 0f, 1f);
                Texture2D? mask = PrepareCellMask(light);
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
                    // 増えたばかりの赤いマスはオレンジに（上の②。黄色いマスは光らない）
                    float glow = NewCellGlow(cellAge[i]);
                    if (i < borderCell && glow > 0f && mask is Texture2D cellShape)
                        DrawPart(cellShape, new Rectangle(0, 0, cellShape.Width, cellShape.Height), GaugeX + 12 + i * CellWidth, y,
                            CellWidth, 33f, Raylib.ColorAlpha(NewCellColor, glow * NewCellPeakAlpha));
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

            if (dan)
            {
                // 段位：「クリア」の字は出さず、合格ボーダーに白い縦の目印（本家・八段のスクショで x≈1750・y235〜290）
                float mx = NormaBorderX(stats.Norma);
                // A-Style の目印（Condition_Stopper.png 43×70。絵のまんなかは x21）。無ければ白い棒を描く
                if (!SkinManager.Draw(DanStopper, mx - 21f, GaugeY + 26f))
                {
                    SmoothShapes.Rounded(new Rectangle(mx - 7f, GaugeY + 29f, 14f, 59f), 0.5f, 6, new Color(20, 20, 24, 255));
                    SmoothShapes.Rounded(new Rectangle(mx - 4f, GaugeY + 32f, 8f, 53f), 0.5f, 6, Color.White);
                }
            }
            else if (SkinManager.GetTexture(ClearText) is Texture2D clear)
            {
                float height = clear.Height / 2f;
                DrawPart(clear, new Rectangle(0, isClear ? height : 0, clear.Width, height),
                    GaugeX + stepX - 7, GaugeY + 9);
            }

            if (isMax && SkinManager.GetTexture(SoulAnime) is Texture2D soul)
            {
                float width = soul.Width / 8f;
                int frameIndex = (int)(elapsed / 0.05f) % 8;
                DrawPart(soul, new Rectangle(frameIndex * width, 0, width, soul.Height),
                    1764, 89, width, soul.Height);
            }
            if (SkinManager.GetTexture(SoulText) is Texture2D text)
            {
                float height = text.Height / 3f;
                int soulRow = result && !isMax ? 0 : 1;   // 0 = 灰色、1 = 白
                DrawPart(text, new Rectangle(0, soulRow * height, text.Width, height), 1792, 204);
                // 満タンで常時発光させず、飛んだ音符が到着したときだけ光を重ねる。
                if (soulFlashAge < 0.16f)
                    DrawPart(text, new Rectangle(0, 2 * height, text.Width, height), 1792, 204,
                        tint: Raylib.ColorAlpha(Color.White, 1f - soulFlashAge / 0.16f));
            }
            else Raylib.DrawText("SOUL", 1805, 232, 28, Color.White);
        }

        private static float[] CreateCellAges()
        {
            float[] ages = new float[Cells];
            Array.Fill(ages, NewCellEnd);
            return ages;
        }

        // 増えてからの秒 → オレンジの強さ（0〜1）。0.05秒で1、0.12秒まで1、0.48秒で0
        private static float NewCellGlow(float age)
        {
            if (age >= NewCellEnd) return 0f;
            if (age < NewCellRise) return 0.3f + 0.7f * age / NewCellRise;   // 増えた最初のコマですでに3割くらい光っていた
            if (age < NewCellHold) return 1f;
            return 1f - (age - NewCellHold) / (NewCellEnd - NewCellHold);
        }

        // 赤いマス（Gauge_Light の (0,33) 21×33）の形を白くした絵を作る。
        //   明るい本体ほどこく、暗い区切りはうすくする（ゲーセンでも区切りはほとんど光らなかった）
        private static Texture2D? PrepareCellMask(Texture2D light)
        {
            if (cellMaskSource == light.Id) return cellMask.Id != 0 ? cellMask : null;
            if (cellMask.Id != 0) Raylib.UnloadTexture(cellMask);
            cellMask = default;
            cellMaskSource = light.Id;
            if (light.Width < 21 || light.Height < 66) return null;
            Image image = Raylib.LoadImageFromTexture(light);
            Raylib.ImageFormat(ref image, PixelFormat.UncompressedR8G8B8A8);
            Raylib.ImageCrop(ref image, new Rectangle(0, 33, 21, 33));
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    Color c = Raylib.GetImageColor(image, x, y);
                    float body = Math.Clamp((c.R - 120f) / 100f, 0f, 1f);
                    Raylib.ImageDrawPixel(ref image, x, y, new Color((byte)255, (byte)255, (byte)255, (byte)(c.A * body)));
                }
            cellMask = Raylib.LoadTextureFromImage(image);
            Raylib.UnloadImage(image);
            return cellMask.Id != 0 ? cellMask : null;
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