using System.Globalization;
using System.Numerics;
using Raylib_cs;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.UI;
using static TakeTaikoSimulator.UI.MenuDraw;

namespace TakeTaikoSimulator.Scenes
{
    // 段位リザルトを、スキンの画像（Image/09.DanResult/）で描く。
    //   置き場所と時間は 11/5 の六段の動画（2026-11-05_09-32-24.mp4 の 423.9〜445秒）を 1/60 秒ずつ見て、
    //   画像をコマに重ねて探した値（OpenCV の matchTemplate）。
    //   画像が無いスキンでは DanResultScene が今までどおりコードで描く。
    //
    //   流れ（0秒＝画面が明るくなり始めたとき）：
    //     ① 曲ごとの内訳：木の板（Song/Panel_Background）の上に、曲の板（Song/Panel_Base）が 0.78秒から 0.425秒おきに右からすべり込む
    //     ② 6.93秒：まとめのパネル（Overall/Panel）が右からすべり込んで、左へ18px 行きすぎてから戻る
    //        → 0.97秒後にスコアの板・魂ゲージの行 → 1.55秒後からゲージが 1秒に40%ずつ伸びる
    //        → 伸びきって 1.1秒後から条件の行が1つずつ（数を 0 から数えあげる） → 最後の数が出て 0.53秒後にスタンプ
    //   スタンプは Stamp/{n}/Anime.aup2（1920×1080 の画面まるごと。そのまま再生すれば置き場所も合う）
    //     n：0＝不合格、1＝赤合格、2＝金合格、3＝赤のフルコンボ、4＝金のフルコンボ、5＝赤のドンダフル、6＝金のドンダフル
    //     （六段の動画は赤合格＋ドンダフルで、5 の虹の輪のスタンプだった）
    internal sealed class DanResultAStyle
    {
        private const string Dir = "Image/09.DanResult/";
        private const string Background = Dir + "Background.png";                 // 1920×1080
        private const string SongBack = Dir + "Song/Panel_Background.png";        // 木の板 1532×928
        private const string SongBase = Dir + "Song/Panel_Base.png";              // 曲の板 1440×256（★・良可不可連打のラベル入り）
        private const string SongStage = Dir + "Song/SongStage.png";              // 1st/2nd/3rd の玉 80×80 が3つ
        private const string NotReached = Dir + "Song/NotReached.png";            // 「未到達」345×150
        private const string Panel = Dir + "Overall/Panel.png";                   // まとめの大きいパネル 1520×940
        private const string JudgePanel = Dir + "Overall/JudgementPanel.png";     // スコアの板と良可不可・連打の箱 1520×270
        private const string RowBase = Dir + "Overall/Base.png";                  // 行の白い板（タブ入り）1400×160
        private const string GaugeBase = Dir + "Overall/Gauge_Base.png";          // 魂ゲージの帯 1252×91 が2つ（上を使う）
        private const string GaugeNumber = Dir + "Overall/Gauge_Number.png";      // 0〜9 と % の 55×62 が2段（下＝オレンジ）
        private const string ScoreNumber = Dir + "Score_Number.png";              // 0〜9 の 28×40（黒い数字）
        private const string StampAnime = Dir + "Stamp/{0}/Anime.aup2";

        private const string CourseSymbol = "Image/06.DanSelect/Song_CourseSymbol.png";   // 難易度のマーク 78×52 が5つ
        private const string Context = "Image/08.DanGame/ConditionPanel/Context.png";     // コイン・1st・2nd・3rd 220×65
        private const string LargeBase = "Image/08.DanGame/ConditionPanel/Large_Gauge_Base.png";
        private const string LargeFill = "Image/08.DanGame/ConditionPanel/Large_Gauge.png";
        private const string LargeRainbow = "Image/08.DanGame/ConditionPanel/Large_Gauge_Rainbow.png";
        private const string LargeNumber = "Image/08.DanGame/ConditionPanel/Large_Gauge_Number.png";
        private const string MiniFill = "Image/08.DanGame/ConditionPanel/Mini_Gauge.png";
        private const string MiniRainbow = "Image/08.DanGame/ConditionPanel/Mini_Gauge_Rainbow.png";
        private const string Stopper = "Image/08.DanGame/Gauge/Condition_Stopper.png";
        private const string GaugeLight = "Image/04.EnsoGame/Gauge/Gauge_Light.png";
        private const string GaugeRainbow = "Image/04.EnsoGame/Gauge/Rainbow.png";
        private const string SoulAnime = "Image/04.EnsoGame/Gauge/Soul_Anime.png";
        private const string SoulText = "Image/04.EnsoGame/Gauge/Soul_Text.png";

        public static bool Available => MenuSkin.Has(SongBack, SongBase, Panel, JudgePanel, RowBase);

        // ===== ① 曲ごとの内訳（測った値） =====
        private const float WoodX = 340f, WoodY = 88f;
        private const float SongX = 380f, SongY = 148f, SongStep = 276f;
        private const float StageDx = 48f, StageDy = 47f;
        private const float SymbolDx = 147f, SymbolDy = 47f;        // 札のマーク（★ は Panel_Base に入っている）
        private const float LevelCx = 201f, LevelCy = 110f;
        private const float TitleDx = 252f, TitleCy = 88f, TitleSize = 44f, TitleMaxWidth = 1120f;
        private static readonly float[] CountRight = { 443f, 760f, 1077f, 1395f };   // 良・可・不可・連打数の数字の右はし
        private const float CountDy = 166f;
        private const float RowFirstAt = 0.78f, RowEvery = 0.425f;

        // ===== ② まとめ（測った値） =====
        private const float SummaryAt = 6.93f;
        private const float PanelX = 346f, PanelY = 88f, JudgeX = 345f, JudgeY = 92f;
        private const float ScoreCx = 680f, ScoreCy = 222f, ScoreSize = 92f;
        private const float StatsLeftRight = 1261f, StatsRightRight = 1715f, StatsY = 157f, StatsStep = 62f;
        private const float BaseX = 400f, GaugeRowY = 352f, ExamRowY = 506f, ExamRowStep = 154f;
        private const float LabelCx = 187f, LabelCy = 19f;
        private const float ContextDx = 20f, ContextDy = 60f;
        private const float NeedRight = 365f, NeedCy = 87f;
        private const float FrameDx = 381f, FrameDy = 19f;              // 大きいバーの枠（08 の Large_Gauge_Base）
        private const float DigitDx = 397f, DigitDy = 7f;
        private const float ColumnStep = 465f;                          // 曲ごと条件の 1st/2nd/3rd の間
        private const float MiniBarDx = 120f, MiniBarDy = 65f, MiniBarWidth = 330f, MiniBarHeight = 50f;
        // 魂ゲージ：演奏画面のゲージと同じ並び（GX・GY は演奏画面の GaugeX・GaugeY にあたる場所）
        private const float GaugeBaseX = 520f, GaugeBaseY = 384f;
        private const float GX = 550f, GY = 385f, CellWidth = 21f;
        private const int Cells = 50;
        private const float PercentX = 764f, PercentY = 357f, PercentAdvance = 34f, PercentSignGap = 47f;
        private const float GaugeSpeed = 40f;                           // 1秒に何％伸びるか
        private const int MaxRows = 3;                                  // パネルに入るのは3行まで（4つめから下ははみ出す）

        // まとめのパネルが入ってくる動き（まとめが始まってからの秒 → 右へずれている px）。動画を1コマずつ測った
        private static readonly (float T, float Offset)[] PanelSlide =
        {
            (0f, 1574f), (0.083f, 325f), (0.1f, 211f), (0.117f, 112f), (0.133f, 36f), (0.15f, 14f),
            (0.167f, -14f), (0.183f, -18f), (0.2f, -10f), (0.217f, -2f), (0.233f, 2f), (0.25f, 6f),
            (0.283f, 8f), (0.3f, 6f), (0.317f, 4f), (0.333f, 2f), (0.367f, 0f),
        };

        private static readonly Color Ink = new(40, 28, 20, 255);
        private static readonly Color Dark = new(24, 18, 16, 255);

        private readonly DanPlay dan;
        private readonly DanResultType result;
        private Aup2? stamp;

        // 条件の行の時間（DanResultScene.age と同じ秒）
        private readonly List<RowTiming> rows = new();
        private readonly float gaugeStart, gaugeEnd;

        public float StampAt { get; }
        public float FullyShownAt => StampAt + 0.6f;

        // 音を鳴らす時間（DanResultScene が使う。11/9 の TaikøNauts の録画で、どれもこの時間に鳴っていた）
        public static float PanelInAt => SummaryAt;
        public float GaugeStartAt => gaugeStart;
        /// <summary>数字が数え上がる条件の行の、数え始める時間</summary>
        public IEnumerable<float> CountStartTimes => rows.Where(r => r.CountSeconds > 0f).Select(r => r.CountStart);
        public bool HasStampAnime => stamp != null;

        private sealed class RowTiming
        {
            public DanExam Exam = null!;
            public float Start, CountStart, CountSeconds;
            public float[] Pops = Array.Empty<float>();   // 数字がポンと出る時間（曲ごと条件は曲の数だけ）
        }

        public DanResultAStyle(DanPlay dan, DanResultType result)
        {
            this.dan = dan;
            this.result = result;

            gaugeStart = SummaryAt + 1.55f;
            gaugeEnd = gaugeStart + (float)Math.Clamp(dan.Gauge, 0, 100) / GaugeSpeed;
            float t = gaugeEnd + 1.1f;
            float last = gaugeEnd;
            foreach (DanExam exam in dan.Course.Exams.Take(MaxRows))
            {
                int count = exam.PerSong ? Math.Min(dan.SongCount, exam.Thresholds.Length) : 1;
                bool counts = false;
                for (int i = 0; i < count; i++)
                    counts |= exam.IsLess ? Amount(exam, i) > 0 : !DanPlay.NoCondition(exam, i);
                RowTiming row = new() { Exam = exam, Start = t, CountStart = t + 0.25f, CountSeconds = counts ? 1.65f : 0f };
                float countEnd = row.CountStart + row.CountSeconds;
                row.Pops = Enumerable.Range(0, count).Select(i => countEnd + (exam.PerSong ? 0.7f * i : 0f)).ToArray();
                rows.Add(row);
                last = row.Pops.Length > 0 ? row.Pops[^1] : countEnd;
                t = last + 1.15f;
            }
            StampAt = last + (rows.Count > 0 ? 0.53f : 1.1f);
        }

        public void Load()
        {
            string? skin = SkinManager.CurrentPath;
            if (skin == null || SkinManager.SystemSkin) return;
            stamp = Aup2.Load(Path.Combine(skin, string.Format(CultureInfo.InvariantCulture, StampAnime, StampIndex())));
            stamp?.Preload();
        }

        public void Unload()
        {
            stamp?.Unload();
            stamp = null;
        }

        // スタンプの番号：不合格 0、合格は 1 + コンボの種類×2 + 金なら1
        private int StampIndex()
        {
            if (result is DanResultType.None or DanResultType.Failed) return 0;
            int combo = dan.Total(DanExamType.Miss) > 0 ? 0 : dan.Total(DanExamType.Good) > 0 ? 1 : 2;
            return 1 + combo * 2 + (result == DanResultType.Gold ? 1 : 0);
        }

        private long Amount(DanExam exam, int song) => dan.Amount(exam, song);

        // ===== 描く =====

        /// <summary>背景（プレートより下に描くもの）</summary>
        public void DrawBackground() => MenuSkin.Whole(Background, new Rectangle(0, 0, 1920, 1080), Color.White);

        /// <summary>パネル（プレートの上に描くもの）。スタンプは DrawStamp で別に</summary>
        public void Draw(float age)
        {
            float s = age - SummaryAt;
            bool summarySettled = s > 0.4f;
            if (!summarySettled) DrawBreakdown(age);
            if (s >= 0f) DrawSummary(age, SlideOffset(s));
        }

        public void DrawStamp(float age)
        {
            if (stamp == null || age < StampAt) return;
            stamp.Draw(stamp.FrameAt(age - StampAt));
        }

        // ① 曲ごとの内訳
        private void DrawBreakdown(float age)
        {
            MenuSkin.Whole(SongBack, new Rectangle(WoodX, WoodY, 1532f, 928f), Color.White);
            for (int i = 0; i < dan.SongCount && i < 3; i++)
            {
                float t = age - (RowFirstAt + i * RowEvery);
                if (t < 0f) continue;
                DrawSongRow(i, SongX + SongRowOffset(t), SongY + i * SongStep);
            }
        }

        // 曲の板のすべり込み：0.117秒で一気に（だんだん遅く）近くまで来て、残り40px を 0.27秒かけてゆっくり
        private static float SongRowOffset(float t)
        {
            float fast = 1494f * (1f - EaseOutCubic(Math.Clamp(t / 0.117f, 0f, 1f)));
            float slow = 40f * Math.Clamp(1f - (t - 0.083f) / 0.267f, 0f, 1f);
            return fast + slow;
        }

        private void DrawSongRow(int i, float x, float y)
        {
            DanSong song = dan.Course.Songs[i];
            DanSongTally tally = dan.Tallies[i];
            bool reached = i <= dan.SongIndex;
            Color tint = reached ? Color.White : new Color(190, 190, 196, 255);

            MenuSkin.Whole(SongBase, new Rectangle(x, y, 1440f, 256f), tint);
            MenuSkin.Part(SongStage, new Rectangle(i * 80f, 0, 80f, 80f), new Rectangle(x + StageDx, y + StageDy, 80f, 80f), tint);
            int symbol = Math.Clamp((int)song.Course, 0, 4);
            MenuSkin.Part(CourseSymbol, new Rectangle(symbol * 78f, 0, 78f, 52f), new Rectangle(x + SymbolDx, y + SymbolDy, 78f, 52f), tint);
            Centered(song.Level.ToString(CultureInfo.InvariantCulture), new Rectangle(x + LevelCx - 20f, y + LevelCy - 17f, 40f, 34f), 30f, Ink);

            // たどり着かなかった曲：曲名は「???」、数は出さずに「未到達」
            string name = !reached || song.IsHidden ? "???" : song.Title.Length > 0 ? song.Title : song.RelativePath;
            float size = TitleSize;
            float width = Measure(name, size).X;
            if (width > TitleMaxWidth && width > 0f) size *= TitleMaxWidth / width;
            OutlinedAt(name, x + TitleDx, y + TitleCy - size * 0.62f, size, Color.White, Dark);

            if (!reached)
            {
                MenuSkin.Whole(NotReached, new Rectangle(x + 1440f - 345f - 60f, y + 70f, 345f, 150f), Color.White);
                return;
            }
            long[] values = { tally.Great, tally.Good, tally.Miss, tally.Roll };
            for (int k = 0; k < values.Length; k++)
                ScoreDigits(values[k], x + CountRight[k], y + CountDy, 1f);
        }

        // ② まとめ（off は右へのずれ。パネルの中身もいっしょに動く）
        private void DrawSummary(float age, float off)
        {
            float s = age - SummaryAt;
            MenuSkin.Whole(Panel, new Rectangle(PanelX + off, PanelY, 1520f, 940f), Color.White);

            float judge = Math.Clamp((s - 0.97f) / 0.23f, 0f, 1f);
            if (judge <= 0f) return;
            MenuSkin.Whole(JudgePanel, new Rectangle(JudgeX + off, JudgeY, 1520f, 270f), MenuSkin.White(judge));

            long score = dan.Tallies.Sum(t => t.Score);
            Outlined(score.ToString(CultureInfo.InvariantCulture), new Rectangle(ScoreCx + off - 220f, ScoreCy - 60f, 440f, 120f),
                ScoreSize, Fade(Color.White, judge), Fade(Dark, judge));

            long[] left = { dan.Total(DanExamType.Great), dan.Total(DanExamType.Good), dan.Total(DanExamType.Miss) };
            long[] right = { dan.Total(DanExamType.Roll), dan.Total(DanExamType.MaxCombo), dan.Total(DanExamType.Hit) };
            for (int r = 0; r < 3; r++)
            {
                ScoreDigits(left[r], StatsLeftRight + off, StatsY + r * StatsStep, judge);
                ScoreDigits(right[r], StatsRightRight + off, StatsY + r * StatsStep, judge);
            }

            DrawGaugeRow(age, off, judge);
            for (int i = 0; i < rows.Count; i++)
                DrawExamRow(rows[i], age, off, ExamRowY + i * ExamRowStep);
        }

        private void DrawRowBase(string label, float x, float y, float alpha, int contextCell)
        {
            MenuSkin.Whole(RowBase, new Rectangle(x, y, 1400f, 160f), MenuSkin.White(alpha));
            Centered(label, new Rectangle(x + LabelCx - 150f, y + LabelCy - 18f, 300f, 36f), 26f, Fade(Ink, alpha));
            MenuSkin.Part(Context, new Rectangle(0, contextCell * 65f, 220f, 65f), new Rectangle(x + ContextDx, y + ContextDy, 220f, 65f), MenuSkin.White(alpha));
        }

        // 魂ゲージ：演奏画面と同じ赤いマスが 1秒に40%ずつ伸びる。伸びきったら「100%」、満タンなら虹と炎
        private void DrawGaugeRow(float age, float off, float alpha)
        {
            float x = BaseX + off;
            DrawRowBase("魂ゲージ", x, GaugeRowY, alpha, 0);
            MenuSkin.Part(GaugeBase, new Rectangle(0, 0, 1252f, 91f), new Rectangle(GaugeBaseX + off, GaugeBaseY, 1252f, 91f), MenuSkin.White(alpha));

            float target = (float)Math.Clamp(dan.Gauge, 0, 100);
            float shown = Math.Min(target, Math.Max(0f, age - gaugeStart) * GaugeSpeed);
            bool done = age >= gaugeEnd;
            bool max = done && target >= 100f;
            int norma = Math.Clamp((int)dan.Course.Gauge.Red, 0, 100);
            int borderCell = Math.Max(0, norma / 2 - 1);
            float gx = GX + off;

            if (max && SkinManager.GetTexture(GaugeRainbow) is Texture2D rainbow)
            {
                // 演奏画面の虹（24コマ。上の8コマ）の、下の帯の高さだけを使う
                float frameHeight = rainbow.Height / 24f;
                int frame = (int)((age - gaugeEnd) * 12f) % 8;
                float width = Math.Min(rainbow.Width, 1056f);
                MenuSkin.Part(GaugeRainbow, new Rectangle(0, frame * frameHeight + 39f, width, 33f),
                    new Rectangle(gx + 6f, GY + 45f, width, 33f), MenuSkin.White(alpha));
            }
            else
            {
                int filled = Math.Clamp((int)(shown / 2f), 0, Cells);
                for (int i = 0; i < filled; i++)
                {
                    float column = i < borderCell ? 0f : 4f * CellWidth;   // ボーダーから先は黄色いマス（の下半分）
                    MenuSkin.Part(GaugeLight, new Rectangle(column, 33f, CellWidth, 33f),
                        new Rectangle(gx + 12f + i * CellWidth, GY + 45f, CellWidth, 33f), MenuSkin.White(alpha));
                }
            }

            // 合格ラインの目印と「99 以上」
            if (norma > 0)
            {
                float mx = gx + 12f + 1050f * norma / 100f - 4f;
                if (!MenuSkin.Whole(Stopper, new Rectangle(mx - 21f, GY + 26f, 43f, 70f), MenuSkin.White(alpha)))
                    Raylib.DrawRectangleRounded(new Rectangle(mx - 5f, GY + 28f, 10f, 50f), 0.5f, 6, Fade(Color.White, alpha));
                string need = $"{norma} 以上";
                OutlinedAt(need, mx + 8f - Measure(need, 26f).X / 2f, GY + 76f, 26f, Fade(Dark, alpha), Fade(Color.White, alpha));
            }

            // 魂：満タンなら炎（8コマ）と明るい字、まだなら灰色の字
            if (max && SkinManager.GetTexture(SoulAnime) is Texture2D soul)
            {
                float w = soul.Width / 8f;
                int frame = (int)((age - gaugeEnd) / 0.05f) % 8;
                MenuSkin.Part(SoulAnime, new Rectangle(frame * w, 0, w, soul.Height), new Rectangle(gx + 1039f, GY - 115f, w, soul.Height), MenuSkin.White(alpha));
            }
            if (SkinManager.GetTexture(SoulText) is Texture2D text)
            {
                float h = text.Height / 3f;
                MenuSkin.Part(SoulText, new Rectangle(0, max ? h : 0f, text.Width, h), new Rectangle(gx + 1067f, GY, text.Width, h), MenuSkin.White(alpha));
            }

            // 「100%」：伸びている間の数（伸びきってから出る）
            if (done) PercentDigits((int)MathF.Round(target), PercentX + off, PercentY, alpha);
        }

        private void DrawExamRow(RowTiming row, float age, float off, float y)
        {
            if (age < row.Start) return;
            float alpha = Math.Clamp((age - row.Start) / 0.2f, 0f, 1f);
            float x = BaseX + off;
            DanExam exam = row.Exam;
            DrawRowBase(DanPanelAStyle.Label(exam.Type), x, y, alpha, exam.PerSong ? 1 : 0);   // 曲ごと条件は左に「1st」
            float count = row.CountSeconds <= 0f ? 1f : Math.Clamp((age - row.CountStart) / row.CountSeconds, 0f, 1f);

            if (!exam.PerSong)
            {
                string need = $"{exam.For(0).Red} {(exam.IsLess ? "未満" : "以上")}";
                OutlinedAt(need, x + NeedRight - Measure(need, 40f).X, y + NeedCy - 25f, 40f, Fade(Dark, alpha), Fade(Color.White, alpha));

                long amount = (long)MathF.Round(Amount(exam, 0) * count);
                float fx = x + FrameDx, fy = y + FrameDy;
                MenuSkin.Part(LargeBase, new Rectangle(0, 120f, 1008f, 120f), new Rectangle(fx, fy, 1008f, 120f), MenuSkin.White(alpha));
                DanReach reach = DanPlay.ReachFor(exam, 0, amount);
                float progress = DanPlay.ProgressFor(exam, 0, amount);
                if (reach != DanReach.Failure && progress > 0f)
                    DanPanelAStyle.DrawFill(LargeFill, LargeRainbow, 16, fx + 18f, fy + 16f, 972f, 86f, progress, reach, exam.IsLess,
                        age, DanPanel.FillColor, _ => alpha, _ => 0f);
                MenuSkin.Part(LargeBase, new Rectangle(0, 0, 1008f, 120f), new Rectangle(fx, fy, 1008f, 120f), MenuSkin.White(alpha));

                float pop = Pop(age, row.Pops[0]);
                if (pop > 0f)
                {
                    Color digitTint = reach == DanReach.Failure ? new Color(150, 150, 150, 255) : Color.White;
                    DanPanelAStyle.DrawDigits(LargeNumber, DanPlay.DisplayedFor(exam, 0, amount), x + DigitDx, y + DigitDy - (1f - pop) * 10f,
                        80f, 100f, 56f, reach == DanReach.Better ? 1 : 0, Fade(digitTint, alpha * pop));
                }
                return;
            }

            // 曲ごと条件：1st/2nd/3rd を横に3つ
            int songs = Math.Min(dan.SongCount, exam.Thresholds.Length);
            for (int i = 0; i < songs && i < 3; i++)
            {
                float cx = x + i * ColumnStep;
                if (i > 0) MenuSkin.Part(Context, new Rectangle(0, (1 + i) * 65f, 220f, 65f), new Rectangle(cx + ContextDx, y + ContextDy, 220f, 65f), MenuSkin.White(alpha));

                Rectangle bar = new(cx + MiniBarDx, y + MiniBarDy, MiniBarWidth, MiniBarHeight);
                Raylib.DrawRectangleRounded(bar, 0.15f, 6, Fade(new Color(16, 16, 18, 255), alpha));
                Rectangle inner = new(bar.X + 3f, bar.Y + 3f, bar.Width - 6f, bar.Height - 6f);
                Raylib.DrawRectangleRec(inner, Fade(new Color(61, 61, 61, 255), alpha));

                long amount = (long)MathF.Round(Amount(exam, i) * count);
                DanReach reach = DanPlay.ReachFor(exam, i, amount);
                float progress = DanPlay.ProgressFor(exam, i, amount);
                if (reach != DanReach.Failure && progress > 0f)
                {
                    Rectangle fill = new(inner.X, inner.Y, inner.Width * progress, inner.Height);
                    if (reach == DanReach.Better && SkinManager.GetTexture(MiniRainbow) is Texture2D rb)
                        MenuSkin.Part(MiniRainbow, new Rectangle(0, 0, rb.Width * progress, rb.Height), fill, MenuSkin.White(alpha));
                    else if (SkinManager.GetTexture(MiniFill) is Texture2D mf)
                        MenuSkin.Part(MiniFill, new Rectangle(0, 0, mf.Width * progress, mf.Height), fill, Fade(DanPanel.FillColor(reach, exam.IsLess), alpha));
                    else
                        Raylib.DrawRectangleRec(fill, Fade(DanPanel.FillColor(reach, exam.IsLess), alpha));
                }

                string need = $"{exam.Thresholds[i].Red} {(exam.IsLess ? "未満" : "以上")}";
                OutlinedAt(need, bar.X + bar.Width - Measure(need, 24f).X, bar.Y + bar.Height - 10f, 24f, Fade(Dark, alpha), Fade(Color.White, alpha));

                float pop = Pop(age, row.Pops[i]);
                if (pop > 0f)
                {
                    // 本家のリザルトの曲ごとの数は、大きいバーの数字（Large_Gauge_Number）の虹の段を半分（40×50）にして 25px ずつ。
                    //   条件の無い「0 以上」の曲も同じ色だった（六段の動画）
                    Color digitTint = reach == DanReach.Failure ? new Color(150, 150, 150, 255) : Color.White;
                    string text = DanPlay.DisplayedFor(exam, i, amount).ToString(CultureInfo.InvariantCulture);
                    for (int k = 0; k < text.Length; k++)
                        MenuSkin.Part(LargeNumber, new Rectangle((text[k] - '0') * 80f, 100f, 80f, 100f),
                            new Rectangle(bar.X + 6f + k * 25f, bar.Y - 9f - (1f - pop) * 6f, 40f, 50f), Fade(digitTint, alpha * pop));
                }
            }
        }

        // 数字がポンと出る（0.12秒で 0→1）
        private static float Pop(float age, float at) => Math.Clamp((age - at) / 0.12f, 0f, 1f);

        // 黒い数字（Score_Number 28×40）を右そろえで
        private static void ScoreDigits(long value, float right, float y, float alpha)
        {
            string text = Math.Max(0, value).ToString(CultureInfo.InvariantCulture);
            float x = right - text.Length * 28f;
            if (SkinManager.GetTexture(ScoreNumber) is null)
            {
                Text(text, right - Measure(text, 40f).X, y - 2f, 40f, Fade(Dark, alpha));
                return;
            }
            for (int i = 0; i < text.Length; i++)
                MenuSkin.Part(ScoreNumber, new Rectangle((text[i] - '0') * 28f, 0, 28f, 40f), new Rectangle(x + i * 28f, y, 28f, 40f), MenuSkin.White(alpha));
        }

        // 「100%」（Gauge_Number の下の段＝オレンジ。数字は 34px ずつ、% はその 47px 先）
        private static void PercentDigits(int value, float x, float y, float alpha)
        {
            string text = Math.Max(0, value).ToString(CultureInfo.InvariantCulture);
            if (SkinManager.GetTexture(GaugeNumber) is null)
            {
                OutlinedAt(text + "%", x, y, 56f, Fade(new Color(255, 120, 150, 255), alpha), Fade(Color.White, alpha));
                return;
            }
            for (int i = 0; i < text.Length; i++)
                MenuSkin.Part(GaugeNumber, new Rectangle((text[i] - '0') * 55f, 62f, 55f, 62f), new Rectangle(x + i * PercentAdvance, y, 55f, 62f), MenuSkin.White(alpha));
            MenuSkin.Part(GaugeNumber, new Rectangle(10 * 55f, 62f, 55f, 62f),
                new Rectangle(x + (text.Length - 1) * PercentAdvance + PercentSignGap, y, 55f, 62f), MenuSkin.White(alpha));
        }

        private static float SlideOffset(float t)
        {
            if (t <= 0f) return PanelSlide[0].Offset;
            for (int i = 1; i < PanelSlide.Length; i++)
            {
                if (t > PanelSlide[i].T) continue;
                var (t0, o0) = PanelSlide[i - 1];
                var (t1, o1) = PanelSlide[i];
                return o0 + (o1 - o0) * (t - t0) / (t1 - t0);
            }
            return 0f;
        }

        private static float EaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3f);
    }
}
