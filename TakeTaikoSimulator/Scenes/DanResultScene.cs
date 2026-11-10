using System.Globalization;
using Raylib_cs;
using TakeTaikoSimulator.Audio;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Diagnostics;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.Input;
using TakeTaikoSimulator.UI;
using static TakeTaikoSimulator.UI.MenuDraw;

namespace TakeTaikoSimulator.Scenes
{
    // 段位リザルト画面
    //   TaikøNauts の動画（10/31 の 2026-10-31_01-44-20.mp4 の 510〜535秒、七段・合格）を測った：
    //     ① まず「曲ごとの内訳」：1st/2nd/3rd の行に曲名と 良・可・不可・連打数
    //     ② 次に「まとめ」：スコア／良・可・不可／連打数・最大コンボ数・叩けた数 →
    //        魂ゲージのバーが伸びる → 条件のバーが上から順に出る → 左の段位プレートに「合格」のスタンプ
    //   満たした条件のバーは虹色。割った条件は灰色。
    //   ドン・Enter で演出を飛ばす／もう一度で段位選択へ（何もしなければ 25秒で戻る）
    //   スキンに Image/09.DanResult/ があれば、その画像で描く（DanResultAStyle。11/5 の六段の動画で測った）
    public sealed class DanResultScene : Scene
    {
        private readonly DanPlay dan;
        private readonly DanResultType result;
        private float age;

        // ===== 時間（動画：内訳 約4秒 → まとめ。ゲージは約1秒で伸び、条件は 0.3秒ずつ） =====
        private const float BreakdownSeconds = 3.6f;
        private const float GaugeFillSeconds = 1.0f;
        private const float RowDelay = 0.3f;
        private const float StampDelay = 0.5f;
        private const float AutoBackSeconds = 25f;

        // ===== 測った値（1920×1080） =====
        private const float PlateX = 145f, PlateY = 180f, PlateHeight = 320f;
        private const float FrameX = 350f, FrameY = 90f, FrameWidth = 1500f, FrameHeight = 920f;
        private const float ScoreX = 468f, ScoreY = 147f, ScoreWidth = 424f, ScoreHeight = 130f;
        private const float Stats1X = 912f, Stats2X = 1292f, StatsY = 147f, StatsHeight = 185f;
        private const float Stats1Width = 364f, Stats2Width = 438f;
        private const float RowTop = 352f, RowStep = 155f, RowHeight = 143f;
        private const float RowX = 405f, RowWidth = 1390f;
        private const float LabelWidth = 367f, LabelHeight = 40f;
        private const float NeedRight = 765f;
        private const float BarX = 797f, BarRight = 1773f, BarHeight = 92f, BarOffset = 31f;
        private const float StampX = 0f, StampY = 740f, StampSize = 350f;

        private static readonly Color Gold = new(214, 176, 72, 255);
        private static readonly Color PanelGray = new(232, 233, 238, 255);
        private static readonly Color Ink = new(44, 32, 26, 255);
        private static readonly Color Tan = new(245, 222, 185, 255);
        private static readonly Color TanEdge = new(196, 150, 96, 255);
        private static readonly Color ScoreBlue = new(32, 96, 128, 255);
        private static readonly Color BarBack = new(30, 30, 34, 255);
        private static readonly Color FailGray = new(150, 150, 158, 255);
        private static readonly Color[] SongColors =
        {
            new(150, 214, 108, 255), new(118, 198, 234, 255), new(246, 160, 184, 255),
        };

        // ===== 音（11/9 の TaikøNauts の録画 2026-11-09 02-21-14.mp4、名人・金合格で確かめた） =====
        //   画面が出る → 0.3秒で Intro.ogg → 1.6秒あとから BGM（BGM/DanResult.ogg をくり返す）
        //   まとめのパネルが入る → PanelIn.ogg、ゲージが伸び始める → Gauge.ogg、
        //   数字が数え上がる条件の行 → ConditionGaugeCountUp.ogg、スタンプ → ClearGold / ClearRed / Failed
        //   （本家はパネルを出すのにボタンを押すけど、TakeTaikoSimulator は今までどおり自動で進む。音はパネルに合わせる）
        //   演出を飛ばしたときは、途中の音は鳴らさずにスタンプの音だけ
        private const string IntroSe = "Sound/DanResult/Intro.ogg";
        private const string BgmFile = "Sound/BGM/DanResult.ogg";
        private const float IntroSeAt = 0.3f, BgmAt = 1.9f;
        private readonly List<(float At, string File, bool Voice)> soundEvents = new();
        private int nextSound;
        private Song? bgm;
        private bool bgmStarted;

        private readonly List<Texture2D> shodanTextures = new();
        private Texture2D plate;
        private DanResultAStyle? aStyle;   // スキンに 09.DanResult の画像があるとき

        public DanResultScene(DanPlay dan)
        {
            this.dan = dan;
            result = dan.Overall();
        }

        public override void Enter()
        {
            age = 0f;
            LoadImages();
            if (DanResultAStyle.Available)
            {
                aStyle = new DanResultAStyle(dan, result);
                aStyle.Load();
            }
            FontManager.PreloadSkinMain(dan.Course.Songs.Select(s => s.Title).Append("???").Append(dan.Course.Title).ToArray());
            BuildSoundEvents();
            string? skin = SkinManager.CurrentPath;
            if (skin != null && aStyle != null)
            {
                bgm = Song.Load(Path.Combine(skin, BgmFile), out _);
                bgm?.SetLoop(true);
            }
            DebugLog.Event("段位", $"{dan.Course.Title}：{Verdict(result)}　ゲージ {dan.Gauge:0.#}%");
        }

        public override void Exit()
        {
            foreach (Texture2D t in shodanTextures) if (t.Id != 0) Raylib.UnloadTexture(t);
            shodanTextures.Clear();
            if (plate.Id != 0) { Raylib.UnloadTexture(plate); plate = default; }
            aStyle?.Unload();
            aStyle = null;
            bgm?.Dispose();
            bgm = null;
        }

        private void BuildSoundEvents()
        {
            soundEvents.Clear();
            nextSound = 0;
            if (aStyle == null) return;   // 本家の画像が無いスキン（シンプル）は鳴らさない
            soundEvents.Add((IntroSeAt, IntroSe, false));
            soundEvents.Add((DanResultAStyle.PanelInAt, "Sound/DanResult/PanelIn.ogg", false));
            soundEvents.Add((aStyle.GaugeStartAt, "Sound/DanResult/Gauge.ogg", false));
            foreach (float t in aStyle.CountStartTimes) soundEvents.Add((t, "Sound/DanResult/ConditionGaugeCountUp.ogg", false));
            string clear = result switch
            {
                DanResultType.Gold => "Sound/DanResult/ClearGold.ogg",
                DanResultType.Red => "Sound/DanResult/ClearRed.ogg",
                _ => "Sound/DanResult/Failed.ogg",
            };
            soundEvents.Add((StampAt, clear, true));
            soundEvents.Sort((a, b) => a.At.CompareTo(b.At));
        }

        private void UpdateSounds()
        {
            while (nextSound < soundEvents.Count && soundEvents[nextSound].At <= age)
            {
                var (_, file, voice) = soundEvents[nextSound++];
                if (voice) MenuVoice.Play(file);
                else MenuVoice.PlaySe(file);
            }
            if (!bgmStarted && bgm != null && age >= BgmAt)
            {
                bgmStarted = true;
                bgm.SetVolume((float)ConfigManager.Sound.GetFinalVolume(ConfigManager.Sound.BgmVolume));
                bgm.Play(0);
            }
        }

        // 演出を飛ばした：スタンプより前の音は鳴らさない
        private void SkipSounds()
        {
            while (nextSound < soundEvents.Count && soundEvents[nextSound].At < StampAt) nextSound++;
        }

        private void LoadImages()
        {
            plate = DanPlates.Load(dan.Course);   // スキンのプレート → 無ければ段位フォルダ
            if (result is DanResultType.Red or DanResultType.Gold)
            {
                foreach (string rel in dan.Course.ShodanTextFor(result == DanResultType.Gold))
                {
                    Texture2D t = LoadFrom(rel);
                    if (t.Id != 0) shodanTextures.Add(t);
                }
            }
        }

        private Texture2D LoadFrom(string relative)
        {
            try
            {
                string full = Path.Combine(dan.Course.FolderPath, relative);
                if (File.Exists(full)) return Raylib.LoadTexture(full);
            }
            catch { /* 読めなければ画像なし */ }
            return default;
        }

        public override void OnBack() => Back();
        private static void Back() => SceneManager.Simple(new DanSelectScene());

        // ===== 時間の区切り =====
        private float SummaryAt => BreakdownSeconds;
        private float RowsAt => SummaryAt + GaugeFillSeconds;
        private int ExamCount => dan.Course.Exams.Count;
        private float StampAt => aStyle?.StampAt ?? RowsAt + ExamCount * RowDelay + StampDelay;
        private float FullyShownAt => aStyle?.FullyShownAt ?? StampAt + 0.6f;

        public override void Update(float dt)
        {
            age += dt;
            UpdateSounds();
            if (age >= Math.Max(AutoBackSeconds, FullyShownAt + 10f)) { Back(); return; }

            bool pressed = InputManager.IsDonPressed()
                           || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter);
            if (!pressed) return;

            if (age < FullyShownAt)
            {
                SkipSounds();
                age = FullyShownAt;
                MenuSounds.Ka();
            }
            else
            {
                MenuSounds.Don();
                Back();
            }
        }

        // ===== 描画 =====

        public override void Draw()
        {
            using OutlineRatioScope outlineScope = UseOutlineRatio(DanOutlineRatio);   // 段位の画面は文字の縁を本家に合わせて細く
            Gui.BeginFrame();
            if (aStyle != null)
            {
                aStyle.DrawBackground();
                DrawPlate(Math.Clamp((age - PlateAt) / 0.2f, 0f, 1f));
                aStyle.Draw(age);
                if (aStyle.HasStampAnime) aStyle.DrawStamp(age);
                else DrawStamp(AStyleStampX, AStyleStampY);
                DrawShodan();
                PuchiChara.Draw(SongHistory.ActiveProfile, LocalSession.Controller, "danResult");
                NamePlate.Draw(SongHistory.ActiveProfile, LocalSession.Controller, 16, 950);
                DrawFooter();
                return;
            }
            Raylib.DrawRectangleGradientV(0, 0, Screen.Width, Screen.Height, new Color(40, 70, 130, 255), new Color(20, 36, 80, 255));

            DrawPlate();

            Rectangle frame = new(FrameX, FrameY, FrameWidth, FrameHeight);
            Raylib.DrawRectangleRounded(frame, Roundness(frame, 8f), 8, new Color(20, 20, 24, 255));
            Rectangle gold = new(frame.X + 6f, frame.Y + 6f, frame.Width - 12f, frame.Height - 12f);
            Raylib.DrawRectangleRounded(gold, Roundness(gold, 6f), 8, Gold);
            Rectangle inner = new(gold.X + 18f, gold.Y + 18f, gold.Width - 36f, gold.Height - 36f);
            Raylib.DrawRectangleRounded(inner, Roundness(inner, 6f), 8, PanelGray);

            if (age < SummaryAt) DrawBreakdown();
            else DrawSummary();

            PuchiChara.Draw(SongHistory.ActiveProfile, LocalSession.Controller, "danResult");
            NamePlate.Draw(SongHistory.ActiveProfile, LocalSession.Controller, 16, 950);
            DrawStamp(StampX + StampSize / 2f, StampY + StampSize * 0.3f);
            DrawShodan();
            DrawFooter();
        }

        // A-Style：プレートは明るくなってから 0.33秒で、0.2秒かけて出る（六段の動画）
        private const float PlateAt = 0.33f;
        // A-Style の画像でスタンプのアニメが無いスキン（シンプル）は、ここにコードのスタンプ（aup2 のスタンプのまんなか）
        private const float AStyleStampX = 196f, AStyleStampY = 556f;

        private void DrawPlate(float appear = 1f)
        {
            if (appear <= 0f) return;
            if (plate.Id == 0)
            {
                Outlined(dan.Course.Title, new Rectangle(PlateX - 20f, PlateY + 100f, 190f, 120f), 72f, Fade(Color.White, appear), Fade(new Color(150, 30, 30, 255), appear));
                return;
            }
            float w = PlateHeight * plate.Width / Math.Max(1, plate.Height);
            float drop = (1f - EaseOut(appear)) * 20f;
            Raylib.DrawTexturePro(plate, new Rectangle(0, 0, plate.Width, plate.Height),
                new Rectangle(PlateX + 75f - w / 2f, PlateY - drop, w, PlateHeight), System.Numerics.Vector2.Zero, 0f, Fade(Color.White, appear));
        }

        // ① 曲ごとの内訳
        private void DrawBreakdown()
        {
            float fade = Math.Clamp(age / 0.25f, 0f, 1f);
            float rowHeight = 240f, top = 130f;
            for (int i = 0; i < dan.SongCount && i < 3; i++)
            {
                DanSong song = dan.Course.Songs[i];
                DanSongTally t = dan.Tallies[i];
                // たどり着かなかった曲（途中で不合格が決まった・F3 でやめた）は灰色で「???」「未到達」
                //   10/31 の九段の動画：2曲目で落ちたとき、3曲目は灰色の板に「???」と、右に「未到達」と出ていた
                bool reached = i <= dan.SongIndex;
                float y = top + i * (rowHeight + 22f);
                Rectangle row = new(FrameX + 60f, y, FrameWidth - 120f, rowHeight);
                Raylib.DrawRectangleRounded(row, Roundness(row, 10f), 8, Fade(reached ? Color.White : new Color(214, 214, 220, 255), fade));
                Raylib.DrawRectangleRoundedLinesEx(row, Roundness(row, 10f), 8, 3f, Fade(Gold, fade));

                Color c = reached ? SongColors[Math.Min(i, SongColors.Length - 1)] : new Color(160, 160, 168, 255);
                Raylib.DrawCircleV(new System.Numerics.Vector2(row.X + 60f, y + 70f), 36f, Fade(c, fade));
                Centered(Ordinal(i), new Rectangle(row.X + 24f, y + 34f, 72f, 72f), 26f, Fade(Color.White, fade));
                string name = !reached || song.IsHidden ? "???" : song.Title.Length > 0 ? song.Title : song.RelativePath;
                TextFit(name, row.X + 120f, y + 46f, row.Width - 160f, 44f, Fade(Ink, fade));
                if (!reached)
                {
                    Outlined("未到達", new Rectangle(row.X + row.Width - 300f, y + 30f, 260f, 80f), 60f,
                        Fade(new Color(196, 186, 255, 255), fade), Fade(new Color(60, 40, 120, 255), fade));
                }

                // 良・可・不可・連打数
                (string L, Color C, long V)[] items =
                {
                    ("良", new Color(220, 140, 20, 255), t.Great), ("可", Ink, t.Good),
                    ("不可", new Color(60, 100, 200, 255), t.Miss), ("連打数", new Color(220, 110, 40, 255), t.Roll),
                };
                float cell = (row.Width - 120f) / items.Length;
                for (int k = 0; k < items.Length; k++)
                {
                    float x = row.X + 60f + k * cell;
                    Text(items[k].L, x, y + 150f, 30f, Fade(items[k].C, fade));
                    if (!reached) continue;   // たどり着かなかった曲は数を出さない
                    string v = items[k].V.ToString(CultureInfo.InvariantCulture);
                    Text(v, x + cell - 40f - Measure(v, 44f).X, y + 142f, 44f, Fade(Ink, fade));
                }
            }
        }

        // ② まとめ
        private void DrawSummary()
        {
            float s = age - SummaryAt;
            float fade = Math.Clamp(s / 0.25f, 0f, 1f);

            // スコア
            long score = dan.Tallies.Sum(t => t.Score);
            Rectangle scoreBox = new(ScoreX, ScoreY, ScoreWidth, ScoreHeight);
            Raylib.DrawRectangleRounded(scoreBox, Roundness(scoreBox, 14f), 8, Fade(ScoreBlue, fade));
            Centered("スコア", new Rectangle(ScoreX, ScoreY + 6f, ScoreWidth, 30f), 24f, Fade(Color.White, fade));
            Outlined(score.ToString(CultureInfo.InvariantCulture), new Rectangle(ScoreX, ScoreY + 34f, ScoreWidth, 90f),
                80f, Fade(Color.White, fade), Fade(new Color(16, 30, 40, 255), fade));

            // 良・可・不可 ／ 連打数・最大コンボ数・叩けた数
            DrawStatBox(new Rectangle(Stats1X, StatsY, Stats1Width, StatsHeight), fade, new[]
            {
                ("良", new Color(220, 140, 20, 255), dan.Total(DanExamType.Great)),
                ("可", Ink, dan.Total(DanExamType.Good)),
                ("不可", new Color(60, 100, 200, 255), dan.Total(DanExamType.Miss)),
            });
            DrawStatBox(new Rectangle(Stats2X, StatsY, Stats2Width, StatsHeight), fade, new[]
            {
                ("連打数", new Color(220, 110, 40, 255), dan.Total(DanExamType.Roll)),
                ("最大コンボ数", new Color(220, 110, 40, 255), dan.Total(DanExamType.MaxCombo)),
                ("叩けた数", new Color(90, 170, 60, 255), dan.Total(DanExamType.Hit)),
            });

            // 魂ゲージ（バーが伸びる）
            DrawGaugeRow(RowTop, Math.Clamp(s / GaugeFillSeconds, 0f, 1f), fade);

            // 条件（上から順に出る）
            for (int i = 0; i < ExamCount; i++)
            {
                float y = RowTop + (i + 1) * RowStep;
                if (y + RowHeight > FrameY + FrameHeight - 20f) break;
                float appear = RowsAt + i * RowDelay;
                if (age < appear) break;
                float t = Math.Clamp((age - appear) / 0.25f, 0f, 1f);
                DrawExamRow(dan.Course.Exams[i], y, t);
            }
        }

        private static void DrawStatBox(Rectangle box, float fade, (string Label, Color Color, long Value)[] rows)
        {
            Raylib.DrawRectangleRounded(box, Roundness(box, 12f), 8, Fade(Color.White, fade));
            float h = box.Height / rows.Length;
            for (int i = 0; i < rows.Length; i++)
            {
                float y = box.Y + i * h;
                if (i % 2 == 1) Raylib.DrawRectangle((int)box.X, (int)y, (int)box.Width, (int)h, Fade(new Color(242, 242, 246, 255), fade));
                Text(rows[i].Label, box.X + 14f, y + h / 2f - 20f, 36f, Fade(rows[i].Color, fade));
                string v = rows[i].Value.ToString(CultureInfo.InvariantCulture);
                Text(v, box.X + box.Width - 16f - Measure(v, 46f).X, y + h / 2f - 26f, 46f, Fade(Ink, fade));
            }
        }

        private void DrawRowFrame(float y, string label, float alpha)
        {
            Rectangle row = new(RowX, y, RowWidth, RowHeight);
            Raylib.DrawRectangleRounded(row, Roundness(row, 14f), 8, Fade(Color.White, alpha));
            Rectangle tab = new(RowX - 2f, y, LabelWidth, LabelHeight);
            Raylib.DrawRectangleRounded(tab, 1f, 12, Fade(Tan, alpha));
            Raylib.DrawRectangleRoundedLinesEx(tab, 1f, 12, 2f, Fade(TanEdge, alpha));
            Centered(label, tab, 26f, Fade(Ink, alpha));
        }

        private void DrawGaugeRow(float y, float fill, float alpha)
        {
            DrawRowFrame(y, "魂ゲージ", alpha);
            float shownGauge = (float)dan.Gauge * fill;
            OutlinedAt($"{shownGauge:0}％", RowX + 375f, y + 2f, 56f, Fade(new Color(255, 120, 150, 255), alpha), Fade(Color.White, alpha));

            Rectangle bar = new(RowX + 115f, y + 63f, RowWidth - 140f, 57f);
            Raylib.DrawRectangleRounded(bar, 0.2f, 8, Fade(BarBack, alpha));
            float shown = Math.Clamp(shownGauge / 100f, 0f, 1f);
            if (shown > 0f)
            {
                Rectangle part = new(bar.X + 6f, bar.Y + 8f, (bar.Width - 12f) * shown, bar.Height - 16f);
                DrawRainbow(part, dan.GaugeStatus() == DanResultType.Failed && fill >= 1f ? FailGray : (Color?)null, alpha);
            }
            // 合格ラインの目印
            if (dan.Course.Gauge.Red > 0)
            {
                float mx = bar.X + 6f + (bar.Width - 12f) * Math.Clamp(dan.Course.Gauge.Red / 100f, 0f, 1f);
                Raylib.DrawRectangle((int)mx - 3, (int)bar.Y + 2, 6, (int)bar.Height - 4, Fade(Color.White, alpha));
                Text($"{dan.Course.Gauge.Red} 以上", mx - 60f, bar.Y + bar.Height + 2f, 20f, Fade(Ink, alpha));
            }
        }

        private void DrawExamRow(DanExam exam, float y, float alpha)
        {
            DrawRowFrame(y, ExamLabel(exam.Type), alpha);

            if (!exam.PerSong)
            {
                string need = $"{exam.For(0).Red} {(exam.IsLess ? "未満" : "以上")}";
                OutlinedAt(need, NeedRight - Measure(need, 40f).X, y + 66f, 40f, Fade(Color.White, alpha), Fade(new Color(20, 20, 24, 255), alpha));
                Rectangle bar = new(BarX, y + BarOffset + 8f, BarRight - BarX, BarHeight);
                DrawResultBar(bar, exam, 0, alpha, 84f);
                return;
            }

            // 曲ごと条件：1st/2nd/3rd の小さいバーを横に並べる
            int count = Math.Min(dan.SongCount, exam.Thresholds.Length);
            float cellWidth = (RowWidth - 40f) / Math.Max(1, count);
            for (int i = 0; i < count; i++)
            {
                float x = RowX + 20f + i * cellWidth;
                Color c = SongColors[Math.Min(i, SongColors.Length - 1)];
                Rectangle badge = new(x, y + 66f, 54f, 34f);
                Raylib.DrawRectangleRounded(badge, 1f, 8, Fade(c, alpha));
                Centered(Ordinal(i), badge, 20f, Fade(Color.White, alpha));
                Rectangle bar = new(x + 62f, y + 56f, cellWidth - 80f, 50f);
                DrawResultBar(bar, exam, i, alpha, 46f);
                Text($"{exam.Thresholds[i].Red} {(exam.IsLess ? "未満" : "以上")}", bar.X + bar.Width - 120f, bar.Y + bar.Height + 2f, 20f, Fade(Ink, alpha));
            }
        }

        // 満たしていれば虹色、割っていれば灰色
        private void DrawResultBar(Rectangle bar, DanExam exam, int song, float alpha, float numberSize)
        {
            Raylib.DrawRectangleRounded(bar, Roundness(bar, 8f), 8, Fade(BarBack, alpha));
            Rectangle inner = new(bar.X + 4f, bar.Y + 4f, bar.Width - 8f, bar.Height - 8f);
            long amount = exam.PerSong ? dan.Tallies[song].Value(exam.Type) : dan.Total(exam.Type);
            bool ok = DanPlay.Satisfies(amount, exam.For(song).Red, exam.IsLess);
            // 本家のリザルトは、満たした条件は端まで虹色。割った条件はそこまでを灰色
            // 「0 以上」（その曲は条件なし）は空のバー（本家のリザルトもそう）
            float progress = DanPlay.NoCondition(exam, song) ? 0f : ok ? 1f : dan.Progress(exam, song);
            if (progress > 0f)
                DrawRainbow(new Rectangle(inner.X, inner.Y, inner.Width * progress, inner.Height), ok ? null : FailGray, alpha);

            string shown = dan.Displayed(exam, song).ToString(CultureInfo.InvariantCulture);
            OutlinedAt(shown, bar.X + 10f, bar.Y + (bar.Height - numberSize) / 2f - 4f, numberSize,
                Fade(new Color(255, 214, 90, 255), alpha), Fade(new Color(150, 30, 60, 255), alpha));
        }

        // 虹色のグラデ（ピンク → 黄 → 緑 → 水色 → 紫）。solid を渡すとその1色
        private static void DrawRainbow(Rectangle area, Color? solid, float alpha)
        {
            if (area.Width <= 0f) return;
            if (solid is Color only)
            {
                Raylib.DrawRectangleRec(area, Fade(only, alpha));
                return;
            }
            Color[] stops =
            {
                new(250, 90, 170, 255), new(255, 200, 60, 255), new(240, 250, 40, 255),
                new(90, 240, 150, 255), new(80, 220, 240, 255), new(150, 140, 250, 255), new(220, 90, 230, 255),
            };
            float seg = area.Width / (stops.Length - 1);
            for (int i = 0; i < stops.Length - 1; i++)
            {
                Raylib.DrawRectangleGradientH((int)(area.X + i * seg), (int)area.Y, (int)Math.Ceiling(seg), (int)area.Height,
                    Fade(stops[i], alpha), Fade(stops[i + 1], alpha));
            }
        }

        // 左の段位プレートの上に「合格」のスタンプ
        private void DrawStamp(float cx, float cy)
        {
            if (age < StampAt) return;
            float t = Math.Clamp((age - StampAt) / 0.25f, 0f, 1f);
            float scale = 1f + (1f - EaseOut(t)) * 0.8f;
            float size = StampSize * scale;

            Color ring = result switch
            {
                DanResultType.Gold => new Color(255, 214, 80, 255),
                DanResultType.Red => new Color(255, 170, 60, 255),
                _ => new Color(150, 150, 160, 255),
            };
            Color word = result == DanResultType.Failed ? new Color(80, 80, 90, 255) : new Color(220, 40, 40, 255);
            Raylib.DrawCircleV(new System.Numerics.Vector2(cx, cy), size * 0.32f, Fade(ring, t));
            Raylib.DrawCircleV(new System.Numerics.Vector2(cx, cy), size * 0.27f, Fade(new Color(255, 248, 220, 255), t));
            Outlined(Verdict(result), new Rectangle(cx - size / 2f, cy - size * 0.2f, size, size * 0.4f),
                size * (result == DanResultType.Gold ? 0.2f : 0.26f), Fade(word, t), Fade(Color.White, t));
        }

        // 昇段演出のテキスト画像（dan.json の shodanTextPaths。あれば）
        private void DrawShodan()
        {
            if (shodanTextures.Count == 0 || age < StampAt + 0.3f) return;
            float gap = 16f;
            float totalWidth = shodanTextures.Sum(x => (float)x.Width) + gap * (shodanTextures.Count - 1);
            float scale = Math.Min(1f, (Screen.Width - 160f) / Math.Max(1f, totalWidth));
            float x = (Screen.Width - totalWidth * scale) / 2f;
            float y = Screen.Height - 220f;
            for (int i = 0; i < shodanTextures.Count; i++)
            {
                Texture2D tex = shodanTextures[i];
                float each = Math.Clamp((age - StampAt - 0.3f - i * 0.12f) / 0.4f, 0f, 1f);
                float w = tex.Width * scale, h = tex.Height * scale;
                Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height),
                    new Rectangle(x, y + (1f - EaseOut(each)) * 24f, w, h), System.Numerics.Vector2.Zero, 0f, Fade(Color.White, each));
                x += w + gap * scale;
            }
        }

        private void DrawFooter()
        {
            string hint = age < FullyShownAt ? "ドン・Enter：演出を飛ばす" : "ドン・Enter・Esc：段位選択へ";
            var measured = Measure(hint, 24);
            Raylib.DrawRectangleRounded(new Rectangle(28, 1030, measured.X + 32, measured.Y + 14), 0.5f, 8, new Color(0, 0, 0, 150));
            Text(hint, 44, 1037, 24, new Color(220, 224, 236, 255));
            string songs = $"{Math.Min(dan.SongIndex + 1, dan.SongCount)} / {dan.SongCount} 曲";
            Text(songs, Screen.Width - 180f, 1037f, 24, new Color(200, 206, 222, 255));
        }

        private static string ExamLabel(DanExamType type) => type switch
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

        private static string Ordinal(int index) => index switch { 0 => "1st", 1 => "2nd", 2 => "3rd", _ => $"{index + 1}th" };

        private static string Verdict(DanResultType result) => result switch
        {
            DanResultType.Gold => "金合格",
            DanResultType.Red => "合格",
            _ => "不合格",
        };
    }
}
