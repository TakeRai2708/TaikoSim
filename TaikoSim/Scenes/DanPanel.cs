using System.Globalization;
using Raylib_cs;
using TaikoSim.Charts;
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
        private const float PlateX = 1628f, PlateY = 652f, PlateHeight = 311f;

        // ===== 上 =====
        private const float SongCountX = 1587f, SongCountY = 103f, SongCountWidth = 288f, SongCountHeight = 32f;
        private const float GaugeLabelX = 1190f, GaugeLabelY = 175f, GaugeLabelWidth = 550f, GaugeLabelHeight = 60f;
        private const float GaugeTabWidth = 295f, GaugeTabHeight = 38f;
        private const float CounterX = 28f, CounterY = 105f, CounterWidth = 187f, CounterHeight = 110f;

        // ===== 色（動画から拾った） =====
        private static readonly Color PanelBack = new(230, 232, 238, 255);
        private static readonly Color PanelGold = new(222, 188, 92, 255);
        private static readonly Color RowBack = new(244, 245, 248, 255);
        private static readonly Color Tan = new(245, 222, 185, 255);
        private static readonly Color TanEdge = new(196, 150, 96, 255);
        private static readonly Color Ink = new(44, 32, 26, 255);
        private static readonly Color BarBack = new(62, 62, 66, 255);
        private static readonly Color BarPink = new(250, 170, 190, 255);
        private static readonly Color BarYellow = new(246, 214, 40, 255);
        private static readonly Color BarGray = new(140, 140, 148, 255);
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

        public DanPanel(DanPlay dan) => this.dan = dan;

        public void Update(float dt) => clock += dt;

        public void Unload()
        {
            if (plate.Id != 0) Raylib.UnloadTexture(plate);
            plate = default;
        }

        private Texture2D? Plate()
        {
            if (!plateTried)
            {
                plateTried = true;
                if (dan.Course.PlatePath.Length > 0)
                {
                    try
                    {
                        string full = Path.Combine(dan.Course.FolderPath, dan.Course.PlatePath);
                        if (File.Exists(full)) plate = Raylib.LoadTexture(full);
                    }
                    catch { /* 読めなければプレートなし */ }
                }
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

        // 「1曲目 / 3曲」（左が白で今の曲、右が水色で全部の曲数）
        private void DrawSongCount()
        {
            Rectangle all = new(SongCountX, SongCountY, SongCountWidth, SongCountHeight);
            Raylib.DrawRectangleRounded(all, 1f, 12, new Color(200, 228, 246, 255));
            Rectangle left = new(all.X, all.Y, all.Width * 0.56f, all.Height);
            Raylib.DrawRectangleRounded(left, 1f, 12, Color.White);
            Centered($"{dan.SongIndex + 1}曲目", left, 22f, Ink);
            Centered($"{dan.SongCount}曲", new Rectangle(left.X + left.Width, all.Y, all.Width - left.Width, all.Height), 22f, Ink);
        }

        // 魂ゲージの上の「魂ゲージ 99%以上」
        private void DrawGaugeLabel()
        {
            if (dan.Course.Gauge.Red <= 0) return;
            Rectangle box = new(GaugeLabelX, GaugeLabelY, GaugeLabelWidth, GaugeLabelHeight);
            Raylib.DrawRectangleRounded(box, Roundness(box, 12f), 8, new Color(255, 255, 255, 235));
            Rectangle tab = new(GaugeLabelX, GaugeLabelY, GaugeTabWidth, GaugeTabHeight);
            Raylib.DrawRectangleRounded(tab, 1f, 12, Tan);
            Raylib.DrawRectangleRoundedLinesEx(tab, 1f, 12, 2f, TanEdge);
            Centered("魂ゲージ", tab, 24f, Ink);
            Outlined($"{dan.Course.Gauge.Red} %以上",
                new Rectangle(GaugeLabelX + GaugeTabWidth, GaugeLabelY + 4f, GaugeLabelWidth - GaugeTabWidth, GaugeLabelHeight - 8f),
                34f, Color.White, new Color(20, 20, 24, 255));
        }

        // 左上の小さいカウンター（良・可・不可・連打数）
        private void DrawCounter()
        {
            Rectangle box = new(CounterX, CounterY, CounterWidth, CounterHeight);
            Raylib.DrawRectangleRounded(box, Roundness(box, 10f), 8, new Color(232, 128, 56, 235));
            Rectangle inner = new(box.X + 4f, box.Y + 4f, box.Width - 8f, box.Height - 8f);
            Raylib.DrawRectangleRounded(inner, Roundness(inner, 8f), 8, new Color(120, 40, 30, 200));

            DanSongTally t = dan.Current;
            (string Label, Color Color, long Value)[] rows =
            {
                ("良", new Color(255, 214, 60, 255), t.Great),
                ("可", Color.White, t.Good),
                ("不可", new Color(120, 170, 255, 255), t.Miss),
                ("連打数", new Color(255, 170, 90, 255), t.Roll),
            };
            float rowHeight = (inner.Height - 4f) / rows.Length;
            for (int i = 0; i < rows.Length; i++)
            {
                float y = inner.Y + 2f + i * rowHeight;
                Rectangle labelBox = new(inner.X + 4f, y + 2f, 100f, rowHeight - 4f);
                Raylib.DrawRectangleRounded(labelBox, 1f, 8, new Color(255, 255, 255, 60));
                Centered(rows[i].Label, labelBox, 17f, rows[i].Color);
                string value = rows[i].Value.ToString(CultureInfo.InvariantCulture);
                float w = Measure(value, 22f).X;
                OutlinedAt(value, inner.X + inner.Width - 8f - w, y + 1f, 22f, Color.White, new Color(20, 20, 24, 255));
            }
        }

        // ===== 下の条件パネル =====

        public void DrawBottom()
        {
            // 背景（明るい灰に、上に金の線）
            Raylib.DrawRectangle(0, (int)PanelTop, Screen.Width, Screen.Height - (int)PanelTop, PanelBack);
            Raylib.DrawRectangle(0, (int)PanelTop, Screen.Width, 6, Ink);
            Raylib.DrawRectangle(0, (int)PanelTop + 6, Screen.Width, 10, PanelGold);

            List<DanExam> exams = dan.Course.Exams;
            int pages = Math.Max(1, (exams.Count + RowsVisible - 1) / RowsVisible);
            int page = pages <= 1 ? 0 : (int)(clock / PageSeconds) % pages;

            for (int row = 0; row < RowsVisible; row++)
            {
                int index = page * RowsVisible + row;
                if (index >= exams.Count) break;
                DrawRow(exams[index], RowTop + row * RowStep);
            }

            if (pages > 1)
                Text($"{page + 1}/{pages}", RowBackX + RowBackWidth - 70f, Screen.Height - 36f, 22f, new Color(110, 110, 120, 255));

            if (Plate() is Texture2D p)
            {
                float w = PlateHeight * p.Width / Math.Max(1, p.Height);
                Raylib.DrawTexturePro(p, new Rectangle(0, 0, p.Width, p.Height),
                    new Rectangle(PlateX + 65f - w / 2f, PlateY, w, PlateHeight), System.Numerics.Vector2.Zero, 0f, Color.White);
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
            Color fill = BarColor(exam, song);

            Raylib.DrawRectangleRounded(bar, Roundness(bar, 8f), 8, new Color(20, 20, 24, 255));
            Rectangle inner = new(bar.X + 4f, bar.Y + 4f, bar.Width - 8f, bar.Height - 8f);
            Raylib.DrawRectangleRounded(inner, Roundness(inner, 6f), 8, BarBack);
            if (progress > 0f)
            {
                Rectangle part = new(inner.X, inner.Y, Math.Max(inner.Height * 0.3f, inner.Width * progress), inner.Height);
                Raylib.DrawRectangleRounded(part, Roundness(part, 6f), 8, fill);
            }

            // 大きい数字（バーの左はしから少し上にはみ出す）
            OutlinedAt(shown.ToString(CultureInfo.InvariantCulture), bar.X + 14f, bar.Y - 12f, 84f,
                Color.White, new Color(20, 20, 24, 255));
        }

        // バーの色：満たしている＝ピンク、まだ届かない＝黄色、もう割った＝灰
        private Color BarColor(DanExam exam, int song)
        {
            long amount = exam.PerSong ? dan.Tallies[song].Value(exam.Type) : dan.Total(exam.Type);
            bool ok = DanPlay.Satisfies(amount, exam.For(song).Red, exam.IsLess);
            if (ok) return BarPink;
            return exam.IsLess ? BarGray : BarYellow;   // 「未満」で割れた＝もう戻らない
        }

        private void DrawOtherSongs(DanExam exam, float barTop, int current)
        {
            int others = 0;
            for (int i = 0; i < dan.SongCount && i < exam.Thresholds.Length; i++)
            {
                if (i == current) continue;
                float y = barTop + 4f + others * 40f;
                others++;
                if (others > 2) break;

                Color dot = i < current ? SongColors[Math.Min(i, SongColors.Length - 1)] : new Color(208, 208, 214, 255);
                Raylib.DrawCircleV(new System.Numerics.Vector2(OthersX + 20f, y + 15f), 15f, dot);
                Rectangle line = new(OthersX + 46f, y + 2f, OthersWidth - 46f, 26f);
                Raylib.DrawRectangleRounded(line, 0.4f, 6, new Color(208, 208, 214, 255));
                if (i < current)
                {
                    // 遊び終わった曲は、その曲の数と ○×
                    long v = dan.Tallies[i].Value(exam.Type);
                    bool ok = DanPlay.Satisfies(v, exam.Thresholds[i].Red, exam.IsLess);
                    Text($"{v}　{(ok ? "○" : "×")}", line.X + 10f, line.Y + 1f, 22f, ok ? Ink : new Color(180, 60, 60, 255));
                }
            }
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
