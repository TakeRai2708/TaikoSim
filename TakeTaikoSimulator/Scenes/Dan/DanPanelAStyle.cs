using System.Globalization;
using Raylib_cs;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Graphics;
using TakeTaikoSimulator.UI;
using static TakeTaikoSimulator.UI.MenuDraw;

namespace TakeTaikoSimulator.Scenes
{
    // 段位の演奏中の条件パネルを、A-Style の画像（Image/08.DanGame/ConditionPanel/）で描く。
    //   置き場所は 10/31 の九段の動画（2026-10-31_03-04-03.mp4 の20秒・250秒）のコマに画像を重ねて探した値
    //   （OpenCV の matchTemplate。枠・数字・ミニバーは誤差 1〜10）。行は 159px ずつ下へ。
    //   画像が無いスキンでは DanPanel が今までどおりコードで描く。
    internal static class DanPanelAStyle
    {
        private const string Dir = "Image/08.DanGame/ConditionPanel/";
        private const string Background = Dir + "Background.png";            // 下半分の背景 1920×540 を (0, 540)
        private const string RowBase = Dir + "Base.png";                     // 行の背景（ラベルのタブ入り）1530×150
        private const string Context = Dir + "Context.png";                  // 220×65 が4つ：コイン・1st・2nd・3rd
        private const string LargeBase = Dir + "Large_Gauge_Base.png";       // 1008×120 が2つ：枠・中の暗い帯
        private const string LargeFill = Dir + "Large_Gauge.png";            // 白いバー 972×86（色をかけて使う）
        private const string LargeRainbow = Dir + "Large_Gauge_Rainbow.png"; // 虹 972×86 が16コマ
        private const string SmallBase = Dir + "Small_Gauge_Base.png";       // 曲ごと条件の今の曲：684×120 が2つ
        private const string SmallFill = Dir + "Small_Gauge.png";            // 644×86
        private const string SmallRainbow = Dir + "Small_Gauge_Rainbow.png"; // 644×86 が8コマ
        private const string Number = Dir + "Large_Gauge_Number.png";        // 80×100 が10個×2段（白・虹）
        private const string Disqualify = Dir + "Disqualify_Text.png";       // 「達成失敗」972×86 が2つ（大きいバー用・小さいバー用）
        private const string MiniBase = Dir + "Mini_Gauge_Base.png";         // ほかの曲：254×50 が2つ（枠・暗い帯）
        private const string MiniFill = Dir + "Mini_Gauge.png";              // 232×30
        private const string MiniRainbow = Dir + "Mini_Gauge_Rainbow.png";   // 232×30
        private const string MiniNumber = Dir + "Mini_Gauge_Number.png";     // 30×38 が10個×2段
        private const string MiniContext = Dir + "Mini_Context.png";         // 40×40 が2つ：1st・2nd
        private const string MiniPosition = Dir + "Mini_Gauge_Position.png"; // まだ遊んでいない曲の灰色の丸と帯 288×56

        // ===== 測った場所（1行目。2行目からは +159） =====
        private const float RowStep = 159f;
        private const float BaseX = 123f, BaseY = 579f;   // タブの左はしで合わせた（誤差6）
        private const float LabelCenterX = 305f, LabelCenterY = 598f;
        private const float ContextX = 135f, ContextY = 633f;
        private const float NeedRight = 478f, NeedCenterY = 664f;
        private const float FrameX = 491f, FrameY = 591f;            // ゲージの枠の左上
        private const float FillDx = 18f, FillDy = 16f;               // 枠からバーの中身へ
        private const float DigitDx = 21f, DigitDy = -8f, DigitAdvance = 56f;
        private const float MiniX = 1236f, MiniDy = 10f, MiniStep = 47f;   // ほかの曲の小さい枠（行の枠の y から +10、2つめは +47）
        private const float RainbowFps = 30f;

        public static bool Available => MenuSkin.Has(Background, RowBase, LargeBase, LargeFill, Number);

        public static void DrawBackground() =>
            MenuSkin.Whole(Background, new Rectangle(0, 540, 1920, 540), Color.White);

        /// <summary>条件1つの行（row は 0〜2）</summary>
        public static void DrawRow(DanPlay dan, DanExam exam, int row, float clock, Func<DanReach, bool, Color> fillColor,
            Func<DanReach, float> blink, Func<DanReach, float> flash)
        {
            float dy = row * RowStep;
            int song = Math.Clamp(dan.SongIndex, 0, Math.Max(0, dan.SongCount - 1));

            MenuSkin.Whole(RowBase, new Rectangle(BaseX, BaseY + dy, 1530f, 150f), Color.White);
            Centered(Label(exam.Type), new Rectangle(LabelCenterX - 150f, LabelCenterY + dy - 18f, 300f, 36f), 26f, new Color(40, 28, 20, 255));

            int contextCell = exam.PerSong ? Math.Min(1 + song, 3) : 0;
            MenuSkin.Part(Context, new Rectangle(0, contextCell * 65f, 220f, 65f), new Rectangle(ContextX, ContextY + dy, 220f, 65f), Color.White);
            string need = $"{exam.For(song).Red} {(exam.IsLess ? "未満" : "以上")}";
            float needWidth = Measure(need, 40f).X;
            OutlinedAt(need, NeedRight - needWidth, NeedCenterY + dy - 25f, 40f, new Color(24, 18, 16, 255), Color.White);

            bool small = exam.PerSong;
            float fx = FrameX, fy = FrameY + dy;
            float baseWidth = small ? 684f : 1008f, fillWidth = small ? 644f : 972f;
            string basePath = small ? SmallBase : LargeBase;
            MenuSkin.Part(basePath, new Rectangle(0, 120f, baseWidth, 120f), new Rectangle(fx, fy, baseWidth, 120f), Color.White);

            DanReach reach = dan.Reach(exam, song);
            float progress = dan.Progress(exam, song);
            if (reach != DanReach.Failure && progress > 0f)
                DrawFill(small ? SmallFill : LargeFill, small ? SmallRainbow : LargeRainbow, small ? 8 : 16,
                    fx + FillDx, fy + FillDy, fillWidth, 86f, progress, reach, exam.IsLess, clock, fillColor, blink, flash);

            MenuSkin.Part(basePath, new Rectangle(0, 0, baseWidth, 120f), new Rectangle(fx, fy, baseWidth, 120f), Color.White);

            // 数字（虹のときは虹の数字、割ったときは灰色）
            long shown = dan.Displayed(exam, song);
            Color digitTint = reach == DanReach.Failure ? new Color(150, 150, 150, 255) : Color.White;
            DrawDigits(Number, shown, fx + DigitDx, fy + DigitDy, 80f, 100f, DigitAdvance, reach == DanReach.Better ? 1 : 0, digitTint);

            if (reach == DanReach.Failure)
                MenuSkin.Part(Disqualify, new Rectangle(0, small ? 86f : 0f, 972f, 86f),
                    new Rectangle(fx + FillDx, fy + FillDy + 1f, 972f, 86f), Color.White);

            if (small) DrawOtherSongs(dan, exam, song, fy, clock, fillColor, blink, flash);
        }

        // 曲ごと条件：右にほかの曲の小さい枠（終わった曲はミニバー、まだの曲は灰色の丸と帯）
        private static void DrawOtherSongs(DanPlay dan, DanExam exam, int current, float frameY, float clock,
            Func<DanReach, bool, Color> fillColor, Func<DanReach, float> blink, Func<DanReach, float> flash)
        {
            int slot = 0;
            for (int i = 0; i < dan.SongCount && i < exam.Thresholds.Length && slot < 2; i++)
            {
                if (i == current) continue;
                float my = frameY + MiniDy + slot * MiniStep;
                slot++;
                if (i > current)
                {
                    MenuSkin.Whole(MiniPosition, new Rectangle(MiniX - 36f, my, 288f, 56f), Color.White);
                    continue;
                }

                MenuSkin.Part(MiniBase, new Rectangle(0, 50f, 254f, 50f), new Rectangle(MiniX, my, 254f, 50f), Color.White);
                DanReach reach = dan.Reach(exam, i);
                float progress = dan.Progress(exam, i);
                if (reach != DanReach.Failure && progress > 0f)
                {
                    Rectangle fill = new(MiniX + 11f, my + 9f, 232f * progress, 30f);
                    if (reach == DanReach.Better)
                        MenuSkin.Part(MiniRainbow, new Rectangle(0, 0, 232f * progress, 30f), fill, Color.White);
                    else
                        MenuSkin.Part(MiniFill, new Rectangle(0, 0, 232f * progress, 30f), fill, Fade(fillColor(reach, exam.IsLess), blink(reach)));
                }
                MenuSkin.Part(MiniBase, new Rectangle(0, 0, 254f, 50f), new Rectangle(MiniX, my, 254f, 50f), Color.White);
                if (i <= 1) MenuSkin.Part(MiniContext, new Rectangle(i * 40f, 0, 40f, 40f), new Rectangle(MiniX - 36f, my + 5f, 40f, 40f), Color.White);
                DrawDigits(MiniNumber, dan.Displayed(exam, i), MiniX + 10f, my - 1f, 30f, 38f, 20f, reach == DanReach.Better ? 1 : 0,
                    reach == DanReach.Failure ? new Color(150, 150, 150, 255) : Color.White);
            }
        }

        // バーの中身：ふつうは白いバーに状態の色をかける、虹はコマ送り。点滅も DanPanel と同じ
        internal static void DrawFill(string fillPath, string rainbowPath, int rainbowFrames, float x, float y, float width, float height,
            float progress, DanReach reach, bool isLess, float clock,
            Func<DanReach, bool, Color> fillColor, Func<DanReach, float> blink, Func<DanReach, float> flash)
        {
            float w = width * Math.Clamp(progress, 0f, 1f);
            Rectangle dest = new(x, y, w, height);
            if (reach == DanReach.Better)
            {
                int frame = (int)(clock * RainbowFps) % rainbowFrames;
                MenuSkin.Part(rainbowPath, new Rectangle(0, frame * height, w, height), dest, Color.White);
                return;
            }
            Rectangle source = new(0, 0, w, height);
            MenuSkin.Part(fillPath, source, dest, Fade(fillColor(reach, isLess), blink(reach)));
            float white = flash(reach);
            if (white > 0f) MenuSkin.Part(fillPath, source, dest, MenuSkin.White(white));
        }

        // 数字の画像を左から並べる（row 0＝白・1＝虹）
        internal static void DrawDigits(string path, long value, float x, float y, float cellWidth, float cellHeight, float advance,
            int row, Color tint)
        {
            string text = Math.Max(0, value).ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < text.Length; i++)
            {
                int d = text[i] - '0';
                MenuSkin.Part(path, new Rectangle(d * cellWidth, row * cellHeight, cellWidth, cellHeight),
                    new Rectangle(x + i * advance, y, cellWidth, cellHeight), tint);
            }
        }

        internal static string Label(DanExamType type) => type switch
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
    }
}
