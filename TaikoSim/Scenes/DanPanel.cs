using System.Globalization;
using System.Numerics;
using Raylib_cs;
using TaikoSim.Audio;
using TaikoSim.Charts;
using TaikoSim.Configs;
using TaikoSim.Graphics;
using TaikoSim.UI;
using static TaikoSim.UI.MenuDraw;

namespace TaikoSim.Scenes
{
    // 段位道場の演奏画面だけに出すもの（PlayScene が段位モードのときに描く）
    //   TaikøNauts の動画（10/31 の 2026-10-31_01-44-20.mp4、七段・AUTO）を測った値：
    //     ・下半分（y540〜1080）がずっと条件パネル。右はしに段位プレート
    //     ・右上の曲名の下に「1曲目 / 3曲」
    //     ・魂ゲージの上に「魂ゲージ 99%以上」の札
    //     ・左上に段位用の小さいカウンター（良・可・不可・連打数）
    //   ★「未満」系（可・不可）のバーは残り回数を出して減っていく（DanPlay.Displayed）
    public sealed class DanPanel
    {
        // ===== 下の条件パネル =====
        private const float PanelTop = 540f;
        private const float RowTop = 611f, RowStep = 159f;            // バーの上
        private const int RowsVisible = 3;                            // 一度に出す条件の数
        private const float PageSeconds = 4f;                         // 4つ以上あるときは4秒ごとに切り替え
        private const float BarX = 512f, BarWidth = 965f, BarHeight = 78f;
        private const float PerSongBarWidth = 646f;                   // 曲ごと条件のときの今の曲のバー
        private const float OthersX = 1200f, OthersWidth = 280f;      // 曲ごと条件のときの、ほかの曲の枠
        private const float RowBackX = 126f, RowBackWidth = 1514f;    // 行の背景
        private const float LabelX = 126f, LabelWidth = 364f, LabelHeight = 38f, LabelAbove = 29f;
        private const float NeedRight = 478f;                         // 「285 未満」の右はし
        // 段位プレート：本家（ニジイロ・達人の動画）は幅約230・高さ約485 の枠で x1560〜1790・y560〜1045。
        //   画像の縦横比は保って、この枠に収める（10/31 にたけらいのスクショで測り直した。前は高さ311で小さかった）
        private const float PlateX = 1560f, PlateY = 560f, PlateWidth = 230f, PlateHeight = 485f;

        // ===== 上 =====
        private const float SongCountX = 1587f, SongCountY = 103f, SongCountWidth = 288f, SongCountHeight = 32f;
        // 「魂ゲージ 99%以上」の札：本家（八段のスクショ）は x1190〜1775・y175〜235、タブは幅300。
        //   字は x1722 で右をそろえ、その右にゲージのボーダーをさす赤い下向きの矢印（y190〜228）
        private const float GaugeLabelX = 1190f, GaugeLabelY = 175f, GaugeLabelWidth = 585f, GaugeLabelHeight = 60f;
        private const float GaugeTabWidth = 300f, GaugeTabHeight = 38f;
        private const float GaugeTextRight = 1722f;
        private const float ArrowTop = 190f, ArrowBottom = 228f;
        // 11/5 たけらい「判定の数の文字が小さい」→ 箱を少し大きくして、ラベル 17→21px・数字 22→27px
        private const float CounterX = 28f, CounterY = 98f, CounterWidth = 212f, CounterHeight = 128f;
        private const float CounterLabelSize = 21f, CounterValueSize = 27f;

        // ===== 色（動画から拾った） =====
        private static readonly Color PanelBack = new(230, 232, 238, 255);
        private static readonly Color PanelGold = new(222, 188, 92, 255);
        private static readonly Color RowBack = new(244, 245, 248, 255);
        private static readonly Color Tan = new(245, 222, 185, 255);
        private static readonly Color TanEdge = new(196, 150, 96, 255);
        private static readonly Color Ink = new(44, 32, 26, 255);
        // バーの色は 10/31 の九段の動画（2026-10-31_03-04-03.mp4）から1px拾った値
        private static readonly Color BarBack = new(61, 61, 61, 255);       // 空のところ
        private static readonly Color BarPink = new(254, 167, 190, 255);    // 満たしている（金もセーフ）
        private static readonly Color BarYellow = new(254, 252, 41, 255);   // あと少し／「未満」で金だけ割った
        private static readonly Color BarRed = new(247, 104, 50, 255);      // 「未満」の残りが少ない
        private static readonly Color FailedText = new(232, 24, 24, 255);   // 「達成失敗」
        private const float PillSeconds = 2f;                               // 「1曲目/3曲」とジャンルを入れかえる間隔
        private const float PillFade = 0.2f;
        private const string FailSound = "Sound/DanGame/Disqualify.ogg";     // A-Style。無ければ OpenTaiko の名前も見る
        private const string FailSoundOpenTaiko = "Sounds/Dan/Failed.ogg";
        private static readonly Color[] SongColors =
        {
            new(150, 214, 108, 255),   // 1st 緑
            new(118, 198, 234, 255),   // 2nd 水色
            new(246, 160, 184, 255),   // 3rd ピンク
        };

        private readonly DanPlay dan;
        private Texture2D plate;
        private bool plateTried;
        private float clock;
        private SoundEffect? failSound;
        private bool soundTried;
        private readonly HashSet<(int Exam, int Song)> failed = new();   // もう「達成失敗」になった条件（音を1回だけ鳴らす）

        public DanPanel(DanPlay dan) => this.dan = dan;

        public void Update(float dt)
        {
            clock += dt;
            CheckFailures();
        }

        // 「未満」の条件を割った瞬間に音を鳴らす（OpenTaiko の Sound_Failed）
        private void CheckFailures()
        {
            List<DanExam> exams = dan.Course.Exams;
            int song = Math.Clamp(dan.SongIndex, 0, Math.Max(0, dan.SongCount - 1));
            for (int i = 0; i < exams.Count; i++)
            {
                int key = exams[i].PerSong ? song : -1;
                if (failed.Contains((i, key))) continue;
                if (dan.Reach(exams[i], song) != DanReach.Failure) continue;
                failed.Add((i, key));
                PlayFailSound();
            }
        }

        private void PlayFailSound()
        {
            if (!soundTried)
            {
                soundTried = true;
                string? skin = SkinManager.CurrentPath;
                if (skin != null)
                    failSound = SoundEffect.Load(Path.Combine(skin, FailSound), out _, 1)
                                ?? SoundEffect.Load(Path.Combine(skin, FailSoundOpenTaiko), out _, 1);
            }
            if (failSound == null) return;
            SoundConfig sound = ConfigManager.Sound;
            failSound.Play((float)sound.GetFinalVolume(sound.SeVolume));
        }

        public void Unload()
        {
            if (plate.Id != 0) Raylib.UnloadTexture(plate);
            plate = default;
            failSound?.Dispose();
            failSound = null;
        }

        private Texture2D? Plate()
        {
            if (!plateTried)
            {
                plateTried = true;
                plate = DanPlates.Load(dan.Course);   // スキンのプレート → 無ければ段位フォルダ
            }
            return plate.Id == 0 ? null : plate;
        }

        // ===== 上の3つ =====

        public void DrawTop()
        {
            DrawSongCount();
            DrawGaugeLabel();
            DrawCounter();
        }

        // 「1曲目 / 3曲」（左が白で今の曲、右が水色で全部の曲数）と、ジャンルの札を2秒ごとに入れかえる
        //   10/31 の九段の動画：曲名の下の札が「1曲目 3曲」→「ナムコオリジナル」→… と約2秒ずつ交互に出ていた
        private void DrawSongCount()
        {
            Rectangle all = new(SongCountX, SongCountY, SongCountWidth, SongCountHeight);
            string genre = dan.Course.Songs.Count > 0
                ? dan.Course.Songs[Math.Clamp(dan.SongIndex, 0, dan.SongCount - 1)].Genre : "";

            float countAlpha = 1f;
            if (genre.Length > 0)
            {
                // 0〜2秒：曲数、2〜4秒：ジャンル。それぞれの最後の0.2秒で次のものへじわっと入れかわる
                float t = clock % (PillSeconds * 2f);
                countAlpha = t < PillSeconds
                    ? Math.Clamp((PillSeconds - t) / PillFade, 0f, 1f)
                    : 1f - Math.Clamp((PillSeconds * 2f - t) / PillFade, 0f, 1f);
            }

            if (countAlpha > 0f)
            {
                Raylib.DrawRectangleRounded(all, 1f, 12, Fade(new Color(200, 228, 246, 255), countAlpha));
                Rectangle left = new(all.X, all.Y, all.Width * 0.56f, all.Height);
                Raylib.DrawRectangleRounded(left, 1f, 12, Fade(Color.White, countAlpha));
                Centered($"{dan.SongIndex + 1}曲目", left, 22f, Fade(Ink, countAlpha));
                Centered($"{dan.SongCount}曲", new Rectangle(left.X + left.Width, all.Y, all.Width - left.Width, all.Height), 22f, Fade(Ink, countAlpha));
            }
            if (genre.Length > 0 && countAlpha < 1f)
            {
                float a = 1f - countAlpha;
                Color c = Tint(SongList.GenreColor(genre), 0.2f);
                Raylib.DrawRectangleRounded(all, 1f, 12, Fade(c, a));
                Outlined(genre, new Rectangle(all.X + 8f, all.Y, all.Width - 16f, all.Height), 22f,
                    Fade(Color.White, a), Fade(new Color(20, 20, 24, 255), a));
            }
        }

        // 魂ゲージの上の「魂ゲージ 99%以上」
        private const string ConditionPlate = "Image/08.DanGame/Gauge/Condition_Plate.png";

        private void DrawGaugeLabel()
        {
            if (dan.Course.Gauge.Red <= 0) return;
            // A-Style：札の画像（タブと赤い矢印入り）を (1192, 163)。九段の動画のコマに重ねて測った
            if (MenuSkin.Whole(ConditionPlate, new Rectangle(1192f, 163f, 610f, 86f), Color.White))
            {
                Centered("魂ゲージ", new Rectangle(1208f, 175f, 284f, 38f), 26f, Ink);
                string label = $"{dan.Course.Gauge.Red} %以上";
                OutlinedAt(label, GaugeTextRight - Measure(label, 40f).X, GaugeLabelY + 6f, 40f, Color.White, new Color(20, 20, 24, 255));
                return;
            }
            Rectangle box = new(GaugeLabelX, GaugeLabelY, GaugeLabelWidth, GaugeLabelHeight);
            Raylib.DrawRectangleRounded(box, Roundness(box, 12f), 8, new Color(255, 255, 255, 235));
            Rectangle tab = new(GaugeLabelX, GaugeLabelY, GaugeTabWidth, GaugeTabHeight);
            Raylib.DrawRectangleRounded(tab, 1f, 12, Tan);
            Raylib.DrawRectangleRoundedLinesEx(tab, 1f, 12, 2f, TanEdge);
            Centered("魂ゲージ", tab, 24f, Ink);
            string need = $"{dan.Course.Gauge.Red} %以上";
            float width = Measure(need, 40f).X;
            OutlinedAt(need, GaugeTextRight - width, GaugeLabelY + 6f, 40f, Color.White, new Color(20, 20, 24, 255));

            // ボーダーをさす赤い矢印（ゲージの白い目印の真上）
            float ax = PlayHUD.NormaBorderX((int)Math.Clamp(dan.Course.Gauge.Red, 0, 100));
            Color edge = new(60, 10, 10, 255), red = new(232, 30, 30, 255);
            DrawArrow(ax, edge, 3f);
            DrawArrow(ax, red, 0f);
        }

        // 下向きの矢印（棒＋三角）。grow をつけると、そのぶん太らせて縁にする
        private static void DrawArrow(float x, Color color, float grow)
        {
            float stemTop = ArrowTop - grow, headTop = ArrowTop + 15f - grow, bottom = ArrowBottom + grow;
            Raylib.DrawRectangleRec(new Rectangle(x - 5f - grow, stemTop, 10f + grow * 2f, headTop - stemTop + 2f), color);
            Vector2 l = new(x - 14f - grow, headTop), r = new(x + 14f + grow, headTop), b = new(x, bottom);
            Raylib.DrawTriangle(l, b, r, color);
            Raylib.DrawTriangle(l, r, b, color);
        }

        // 左上の小さいカウンター（良・可・不可・連打数）
        private void DrawCounter()
        {
            Rectangle box = new(CounterX, CounterY, CounterWidth, CounterHeight);
            Raylib.DrawRectangleRounded(box, Roundness(box, 10f), 8, new Color(232, 128, 56, 235));
            Rectangle inner = new(box.X + 4f, box.Y + 4f, box.Width - 8f, box.Height - 8f);
            Raylib.DrawRectangleRounded(inner, Roundness(inner, 8f), 8, new Color(120, 40, 30, 200));

            // 全部の曲の合計（10/31 の動画：1曲目の終わり 良1088 → 2曲目で 1140 と続いていた）
            (string Label, Color Color, long Value)[] rows =
            {
                ("良", new Color(255, 214, 60, 255), dan.Total(DanExamType.Great)),
                ("可", Color.White, dan.Total(DanExamType.Good)),
                ("不可", new Color(120, 170, 255, 255), dan.Total(DanExamType.Miss)),
                ("連打数", new Color(255, 170, 90, 255), dan.Total(DanExamType.Roll)),
            };
            float rowHeight = (inner.Height - 4f) / rows.Length;
            for (int i = 0; i < rows.Length; i++)
            {
                float y = inner.Y + 2f + i * rowHeight;
                Rectangle labelBox = new(inner.X + 4f, y + 2f, 104f, rowHeight - 4f);
                Raylib.DrawRectangleRounded(labelBox, 1f, 8, new Color(255, 255, 255, 60));
                Centered(rows[i].Label, labelBox, CounterLabelSize, rows[i].Color);
                string value = rows[i].Value.ToString(CultureInfo.InvariantCulture);
                Vector2 size = Measure(value, CounterValueSize);
                OutlinedAt(value, inner.X + inner.Width - 8f - size.X, y + (rowHeight - size.Y) / 2f, CounterValueSize,
                    Color.White, new Color(20, 20, 24, 255));
            }
        }

        // ===== 下の条件パネル =====

        public void DrawBottom()
        {
            bool aStyle = DanPanelAStyle.Available;
            if (aStyle) DanPanelAStyle.DrawBackground();
            else
            {
                // 背景（明るい灰に、上に金の線）
                Raylib.DrawRectangle(0, (int)PanelTop, Screen.Width, Screen.Height - (int)PanelTop, PanelBack);
                Raylib.DrawRectangle(0, (int)PanelTop, Screen.Width, 6, Ink);
                Raylib.DrawRectangle(0, (int)PanelTop + 6, Screen.Width, 10, PanelGold);
            }

            List<DanExam> exams = dan.Course.Exams;
            int pages = Math.Max(1, (exams.Count + RowsVisible - 1) / RowsVisible);
            int page = pages <= 1 ? 0 : (int)(clock / PageSeconds) % pages;

            for (int row = 0; row < RowsVisible; row++)
            {
                int index = page * RowsVisible + row;
                if (index >= exams.Count) break;
                if (aStyle) DanPanelAStyle.DrawRow(dan, exams[index], row, clock, FillColor, Blink, Flash);
                else DrawRow(exams[index], RowTop + row * RowStep);
            }

            if (pages > 1)
                Text($"{page + 1}/{pages}", RowBackX + RowBackWidth - 70f, Screen.Height - 36f, 22f, new Color(110, 110, 120, 255));

            if (Plate() is Texture2D p)
            {
                Raylib.DrawTexturePro(p, new Rectangle(0, 0, p.Width, p.Height),
                    FitPlate(p, PlateX, PlateY, PlateWidth, PlateHeight), Vector2.Zero, 0f, Color.White);
            }
        }

        private void DrawRow(DanExam exam, float barTop)
        {
            int song = Math.Clamp(dan.SongIndex, 0, Math.Max(0, dan.SongCount - 1));

            // 行の背景
            Rectangle back = new(RowBackX, barTop - LabelAbove - 2f, RowBackWidth, BarHeight + LabelAbove + 28f);
            Raylib.DrawRectangleRounded(back, Roundness(back, 18f), 10, RowBack);

            // ラベル（可の数・不可の数・連打数…）
            Rectangle label = new(LabelX, barTop - LabelAbove, LabelWidth, LabelHeight);
            Raylib.DrawRectangleRounded(label, 1f, 12, Tan);
            Raylib.DrawRectangleRoundedLinesEx(label, 1f, 12, 2f, TanEdge);
            Centered(LabelText(exam.Type), label, 26f, Ink);

            // 左：コイン（全体条件）か 1st/2nd/3rd の札（曲ごと条件）＋「285 未満」
            float midY = barTop + BarHeight / 2f + 12f;
            if (exam.PerSong)
            {
                Color c = SongColors[Math.Min(song, SongColors.Length - 1)];
                Rectangle badge = new(LabelX + 14f, midY - 20f, 58f, 40f);
                Raylib.DrawRectangleRounded(badge, 1f, 10, c);
                Centered(Ordinal(song), badge, 22f, Color.White);
            }
            else
            {
                Raylib.DrawCircleV(new System.Numerics.Vector2(LabelX + 40f, midY), 24f, new Color(250, 214, 90, 255));
                Raylib.DrawCircleLinesV(new System.Numerics.Vector2(LabelX + 40f, midY), 24f, new Color(214, 160, 40, 255));
            }
            string need = $"{exam.For(song).Red} {(exam.IsLess ? "未満" : "以上")}";
            float needWidth = Measure(need, 40f).X;
            OutlinedAt(need, NeedRight - needWidth, midY - 24f, 40f, Color.White, new Color(20, 20, 24, 255));

            // 真ん中：大きいバー
            float barWidth = exam.PerSong ? PerSongBarWidth : BarWidth;
            DrawBar(new Rectangle(BarX, barTop, barWidth, BarHeight), exam, song);

            // 曲ごと条件：右にほかの曲の枠（遊び終わった曲は結果、まだの曲は灰色）
            if (exam.PerSong) DrawOtherSongs(exam, barTop, song);
        }

        private void DrawBar(Rectangle bar, DanExam exam, int song)
        {
            long shown = dan.Displayed(exam, song);
            float progress = dan.Progress(exam, song);
            DanReach reach = dan.Reach(exam, song);

            Raylib.DrawRectangleRounded(bar, Roundness(bar, 8f), 8, new Color(20, 20, 24, 255));
            Rectangle inner = new(bar.X + 4f, bar.Y + 4f, bar.Width - 8f, bar.Height - 8f);
            Raylib.DrawRectangleRounded(inner, Roundness(inner, 6f), 8, BarBack);
            if (reach != DanReach.Failure && progress > 0f)
            {
                Rectangle part = new(inner.X, inner.Y, Math.Max(inner.Height * 0.3f, inner.Width * progress), inner.Height);
                DrawFill(part, reach, exam.IsLess, 1f);
            }

            // 大きい数字（バーの左はしから少し上にはみ出す）。虹色のときは数字も虹っぽく、割ったときは灰色
            string text = shown.ToString(CultureInfo.InvariantCulture);
            float numberX = bar.X + 14f, numberY = bar.Y - 12f;
            if (reach == DanReach.Failure)
                OutlinedAt(text, numberX, numberY, 84f, new Color(150, 150, 154, 255), new Color(40, 40, 44, 255));
            else if (reach == DanReach.Better)
                DrawRainbowNumber(text, numberX, numberY, 84f);
            else
                OutlinedAt(text, numberX, numberY, 84f, Color.White, new Color(20, 20, 24, 255));

            // 割った条件は、バーのまんなかに赤い字で「達成失敗」
            if (reach == DanReach.Failure)
                Outlined("達成失敗", new Rectangle(bar.X + bar.Width * 0.25f, bar.Y, bar.Width * 0.5f, bar.Height), 58f,
                    FailedText, new Color(40, 0, 0, 255));
        }

        // 状態ごとのバーの色・点滅（A-Style の画像で描くときも同じものを使う）
        private static Color FillColor(DanReach reach, bool isLess) => reach switch
        {
            DanReach.Success or DanReach.NearBetter or DanReach.NearerBetter => BarPink,
            DanReach.High or DanReach.NearSuccess or DanReach.NearerSuccess => BarYellow,
            DanReach.Low => isLess ? BarRed : BarYellow,
            _ => BarRed,
        };

        private float Blink(DanReach reach) => reach == DanReach.Danger ? Wave(1f) : 1f;

        private float Flash(DanReach reach) => reach switch
        {
            DanReach.NearSuccess or DanReach.NearBetter => Wave(1f) * 0.85f,
            DanReach.NearerSuccess or DanReach.NearerBetter => Wave(2f) * 0.85f,
            _ => 0f,
        };

        // バーの中身を、状態に合わせた色で塗る（点滅も）
        //   OpenTaiko：Danger は灰色に点滅（不透明度が1秒で行き来）、Near〜 は白く点滅（Nearer は2倍の速さ）
        private void DrawFill(Rectangle part, DanReach reach, bool isLess, float alpha)
        {
            if (reach == DanReach.Better)
            {
                DrawRainbow(part, alpha);
                return;
            }

            Color color = reach switch
            {
                DanReach.Success or DanReach.NearBetter or DanReach.NearerBetter => BarPink,
                DanReach.High or DanReach.NearSuccess or DanReach.NearerSuccess => BarYellow,
                DanReach.Low => isLess ? BarRed : BarYellow,   // 推測なんだけど：「以上」の Low は黄色（OpenTaiko のコメントどおり）
                _ => BarRed,                                    // Danger
            };
            if (reach == DanReach.Danger) alpha *= Wave(1f);
            Raylib.DrawRectangleRounded(part, Roundness(part, 6f), 8, Fade(color, alpha));

            float flash = reach switch
            {
                DanReach.NearSuccess or DanReach.NearBetter => Wave(1f),
                DanReach.NearerSuccess or DanReach.NearerBetter => Wave(2f),
                _ => 0f,
            };
            if (flash > 0f) Raylib.DrawRectangleRounded(part, Roundness(part, 6f), 8, Fade(Color.White, flash * alpha * 0.85f));
        }

        // 0〜1 を行き来する波（OpenTaiko の (1 + cos(2π t)) / 2。speed は1秒に何回か）
        private float Wave(float speed) => (1f + MathF.Cos(2f * MathF.PI * clock * speed)) / 2f;

        // 虹色のバー（色が右から左へ流れる）
        private void DrawRainbow(Rectangle part, float alpha)
        {
            const int slices = 24;
            float w = part.Width / slices;
            for (int i = 0; i < slices; i++)
            {
                Color a = Fade(Hue((i / (float)slices) * 300f + clock * 180f), alpha);
                Color b = Fade(Hue(((i + 1) / (float)slices) * 300f + clock * 180f), alpha);
                float x = part.X + i * w;
                // 両はしは丸く、まんなかはふつうの四角
                Raylib.DrawRectangleGradientH((int)x, (int)part.Y, (int)MathF.Ceiling(w) + 1, (int)part.Height, a, b);
            }
        }

        // 色相 → 少しパステルの色
        private static Color Hue(float degrees)
        {
            Color c = Raylib.ColorFromHSV(((degrees % 360f) + 360f) % 360f, 0.62f, 1f);
            return c;
        }

        // 虹色の数字（上半分が黄色・下半分がピンク。本家は字の中もグラデーション）
        private static void DrawRainbowNumber(string text, float x, float y, float size)
        {
            Color edge = new(60, 20, 40, 255);
            OutlinedAt(text, x, y, size, new Color(255, 96, 170, 255), edge);
            Vector2 measured = Measure(text, size);
            Raylib.BeginScissorMode((int)(x - 20f), (int)(y - 20f), (int)(measured.X + 40f), (int)(20f + measured.Y * 0.5f));
            OutlinedAt(text, x, y, size, new Color(255, 226, 60, 255), edge);
            Raylib.EndScissorMode();
        }

        // 曲ごと条件：右にほかの曲の小さい枠
        //   10/31 の九段の動画：終わった曲は「1st」の札＋その曲の結果のミニバー（色も大きいバーと同じ）＋数字、
        //   まだの曲は灰色の丸と灰色の帯
        private void DrawOtherSongs(DanExam exam, float barTop, int current)
        {
            int others = 0;
            for (int i = 0; i < dan.SongCount && i < exam.Thresholds.Length; i++)
            {
                if (i == current) continue;
                float y = barTop + 4f + others * 40f;
                others++;
                if (others > 2) break;

                bool played = i < current;
                Color dot = played ? SongColors[Math.Min(i, SongColors.Length - 1)] : new Color(208, 208, 214, 255);
                Raylib.DrawCircleV(new Vector2(OthersX + 20f, y + 15f), 15f, dot);
                if (played) Centered(Ordinal(i), new Rectangle(OthersX + 5f, y + 2f, 30f, 26f), 14f, Color.White);

                Rectangle line = new(OthersX + 46f, y + 2f, OthersWidth - 46f, 26f);
                Raylib.DrawRectangleRounded(line, 0.3f, 6, played ? new Color(20, 20, 24, 255) : new Color(208, 208, 214, 255));
                if (!played) continue;

                Rectangle inner = new(line.X + 2f, line.Y + 2f, line.Width - 4f, line.Height - 4f);
                Raylib.DrawRectangleRec(inner, BarBack);
                DanReach reach = dan.Reach(exam, i);
                float progress = dan.Progress(exam, i);
                if (reach != DanReach.Failure && progress > 0f)
                    DrawFill(new Rectangle(inner.X, inner.Y, Math.Max(4f, inner.Width * progress), inner.Height), reach, exam.IsLess, 1f);
                string v = dan.Displayed(exam, i).ToString(CultureInfo.InvariantCulture);
                if (reach == DanReach.Better) DrawRainbowNumber(v, inner.X + 4f, inner.Y - 4f, 26f);
                else OutlinedAt(v, inner.X + 4f, inner.Y - 4f, 26f, Color.White, new Color(20, 20, 24, 255));
            }
        }

        /// <summary>プレートの画像を、縦横比を保って枠に収める（横はまんなか・上はそろえる）</summary>
        public static Rectangle FitPlate(Texture2D image, float x, float y, float width, float height)
        {
            float scale = Math.Min(width / Math.Max(1, image.Width), height / Math.Max(1, image.Height));
            float w = image.Width * scale, h = image.Height * scale;
            return new Rectangle(x + (width - w) / 2f, y, w, h);
        }

        private static string LabelText(DanExamType type) => type switch
        {
            DanExamType.Great => "良の数",
            DanExamType.Good => "可の数",
            DanExamType.Miss => "不可の数",
            DanExamType.Roll => "連打数",
            DanExamType.Hit => "叩けた数",
            DanExamType.Score => "スコア",
            DanExamType.MaxCombo => "最大コンボ数",
            _ => type.ToString(),
        };

        private static string Ordinal(int index) => index switch
        {
            0 => "1st",
            1 => "2nd",
            2 => "3rd",
            _ => $"{index + 1}th",
        };
    }
}
