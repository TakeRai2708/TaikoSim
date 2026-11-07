using System.Globalization;
using System.Numerics;
using Raylib_cs;
using TaikoSim.Charts;
using TaikoSim.UI;
using static TaikoSim.UI.MenuDraw;

namespace TaikoSim.Scenes
{
    // 段位選択画面を A-Style の画像（Image/06.DanSelect/）で描く。
    //   画像の置き場所は、10/31 の動画（2026-10-31_01-44-20.mp4 の34秒＝初段・50秒＝確認ポップアップ）のコマに
    //   画像を重ね合わせて、一番ずれの小さい場所を探した値（OpenCV の matchTemplate。誤差は1〜15くらい）。
    //   画像が無いスキンでは false を返すので、DanSelectScene が今までどおりコードで描く。
    internal static class DanSelectAStyle
    {
        private const string Dir = "Image/06.DanSelect/";
        private const string MiniPlate = Dir + "MiniPlate.png";          // 段位の札 80×168 が6色（級・初〜五段・六〜十段・玄人〜超人・達人・外伝）
        private const string PlateEffect = Dir + "Plate_Effect.png";     // 選んでいる札にかぶせる白い縁（中は29%の白）
        private const string PlateArrow = Dir + "Plate_Arrow.png";       // 選んでいる札の左右の青い矢印（右は左右反転）
        private const string Stamp = Dir + "Stamp.png";                  // 合格した札に乗せる「合格」 90×76 が6つ
        private const string Panel = Dir + "Panel.png";                  // クリーム色の大パネル（合格条件のタブと枠も入っている。右下に影）
        private const string PanelSide = Dir + "Panel_Side.png";         // パネルの左右の模様 40×806 が6色（札と同じ並び）
        private const string SongBar = Dir + "SongBar.png";              // 課題曲の帯 1380×110 が3つ（1st/2nd/3rd の玉入り）
        private const string CourseSymbol = Dir + "Song_CourseSymbol.png"; // 難易度のマーク 78×52 が5つ（かんたん〜裏おに）
        private const string ConditionBar = Dir + "Condition_Bar.png";   // 条件の箱（上にラベルのタブ）
        private const string ConditionContent = Dir + "Condition_Content.png"; // 箱の中身：50px ずつ「コイン／1st／2nd／3rd／白いバー」
        private const string GaugeBar = Dir + "GaugeCondition_Bar.png";  // 魂ゲージの箱
        private const string ConfirmBase = Dir + "Challenge_Check/Panel_Base.png";   // 確認の青い箱（質問の文字も入っている）
        private const string ConfirmButtons = Dir + "Challenge_Check/Panel.png";     // スパナ・挑戦する・挑戦しない
        private const string ConfirmCursor = Dir + "Challenge_Check/Select_Effect.png"; // 350×150 が7つ（大：黄・光・白・白塗り／小：黄・光・白）

        // ===== 測った場所 =====
        private const float TileLeft = 190f - 8f, TileStep = 68f, TileTop = 26f, TileWidth = 80f, TileHeight = 168f;
        private const float SelectedDrop = 22f;                          // 選んでいる札は22px 下がる
        private static readonly Vector2 PanelAt = new(346f, 192f);
        private static readonly Vector2 SongBarAt = new(412f, 231f);
        private const float SongStep = 110f;
        private const float SideLeftX = 364f, SideRightX = 1800f, SideY = 215f;
        private const float CondBarX = 751f, CondBarY = 608f, CondStep = 132f;
        private const float ContentX = 768f, ContentY = 654f;            // コインの行（箱1つめ）
        private const float ContentBarX = 775f, ContentBarY = 690f;      // 白いバーの行（箱1つめ）
        private const float ColumnStep = 331f;                           // 曲ごと条件の列の間隔
        private static readonly Vector2 GaugeBoxAt = new(434f, 611f);
        private static readonly Vector2 ConfirmAt = new(276f, 296f);
        // ボタンのまんなか（Panel.png の中のボタンの場所＋ConfirmAt）：スパナ・挑戦する・挑戦しない
        private static readonly Vector2[] ButtonCenters = { new(613.5f, 581.5f), new(858.5f, 581.5f), new(1196.5f, 581.5f) };

        private static readonly Color Ink = new(24, 18, 16, 255);
        private static readonly string[] SlotNames =
        {
            "五級", "四級", "三級", "二級", "一級", "初段", "二段", "三段", "四段", "五段",
            "六段", "七段", "八段", "九段", "十段", "玄人", "名人", "超人", "達人", "外伝",
        };
        public const int SlotCount = 20;

        /// <summary>
        /// 札に書く名前。枠の場所（danIndex）で決まった名前を使う。
        ///   11/7 たけらい：2026段位道場の玄人・名人の札に「2026段位道場…」と出ていた（dan.json の名前をそのまま縦書きにしていた）。
        ///   枠の外（自分で作った段位など）は dan.json の名前
        /// </summary>
        public static string SlotName(int danIndex, string fallback) =>
            danIndex is >= 0 and < SlotCount ? SlotNames[danIndex] : fallback;

        public static bool Available => MenuSkin.Has(MiniPlate, Panel, SongBar);

        // 札の色（MiniPlate・Panel_Side のコマ）：級＝0・初〜五段＝1・六〜十段＝2・玄人〜超人＝3・達人＝4・外伝＝5
        //   本家の動画では達人だけ金色だった
        public static int ColorCell(int danIndex) => danIndex switch
        {
            <= 4 => 0,
            <= 9 => 1,
            <= 14 => 2,
            <= 17 => 3,
            18 => 4,
            _ => 5,
        };

        // ===== 段位の札 =====

        /// <summary>
        /// 20枠の札。無い段位の枠も名前を出して、色を半分の明るさにする（本家：五級〜一級・玄人〜外伝がそうなっていた。色はちょうど×0.5）
        /// </summary>
        public static void DrawTiles(List<DanCourse> courses, int cursor, float clock)
        {
            for (int slot = 0; slot < SlotCount; slot++)
            {
                int index = courses.FindIndex(c => c.DanIndex == slot);
                if (index == cursor) continue;
                DanCourse? course = index >= 0 ? courses[index] : null;
                bool usable = course != null && course.Playable && DanUnlock.IsUnlocked(course, out _);
                DrawTile(slot, SlotNames[slot], TileTop, usable, false, clock);
                if (course != null) DrawStamp(course, slot, TileTop);
            }

            if (cursor >= 0 && cursor < courses.Count && courses[cursor].DanIndex is >= 0 and < SlotCount)
            {
                DanCourse course = courses[cursor];
                bool usable = course.Playable && DanUnlock.IsUnlocked(course, out _);
                DrawTile(course.DanIndex, SlotNames[course.DanIndex], TileTop + SelectedDrop, usable, true, clock);
                DrawStamp(course, course.DanIndex, TileTop + SelectedDrop);
                DrawArrows(course.DanIndex, clock);
            }
        }

        private static void DrawTile(int slot, string name, float top, bool usable, bool selected, float clock)
        {
            float x = TileLeft + slot * TileStep;
            Rectangle dest = new(x, top, TileWidth, TileHeight);
            Color tint = usable ? Color.White : new Color(128, 128, 128, 255);
            MenuSkin.Part(MiniPlate, new Rectangle(ColorCell(slot) * TileWidth, 0, TileWidth, TileHeight), dest, tint);
            if (selected)
            {
                // 白い縁（中は29%の白）。推測なんだけど、本家は少し明るさが行き来していたので、ゆっくり点滅させる
                float pulse = 0.75f + 0.25f * MathF.Sin(clock * MathF.PI * 2f / 1.2f);
                MenuSkin.Whole(PlateEffect, dest, MenuSkin.White(pulse));
            }

            // 段位の名前を縦に（本家：黒い字・1文字36px くらい・1文字めのまんなかが札の上から100px、37px ずつ下へ）
            float size = name.Length <= 2 ? 36f : Math.Max(20f, 74f / name.Length * 1.0f);
            float step = name.Length <= 2 ? 37f : size * 1.02f;
            float first = top + (name.Length <= 2 ? 100f : 100f - (name.Length - 2) * step / 2f);
            Color ink = usable ? Ink : new Color(24, 18, 16, 200);
            for (int i = 0; i < name.Length; i++)
                Centered(name[i].ToString(CultureInfo.InvariantCulture),
                    new Rectangle(x, first + i * step - size / 2f - 2f, TileWidth, size + 4f), size, ink);
        }

        // 合格した段位の札に「合格」（Stamp.png の6つ。推測なんだけど、2つずつ「赤／金」で、
        //   左から「クリア・フルコンボ・全良」の順。本家のニジイロの段位選択のスクショでは札の上の方に乗っていた）
        private static void DrawStamp(DanCourse course, int slot, float top)
        {
            DanClearRecord record = SongHistory.GetDanRecord(SongHistory.KeyFor(course.JsonPath));
            if (record.Combo < SongClearType.Cleared) return;
            int cell = ((int)record.Combo - 1) * 2 + (record.Gold ? 1 : 0);
            float cx = TileLeft + slot * TileStep + TileWidth / 2f;
            MenuSkin.Part(Stamp, new Rectangle(cell * 90f, 0, 90f, 76f), new Rectangle(cx - 45f, top + 4f, 90f, 76f), Color.White);
        }

        // 選んでいる札の左右の矢印（本家：うすい青。札のまんなかから左へ85px・右へ29px の所に画像の左はし、上は +47px）
        private static void DrawArrows(int slot, float clock)
        {
            float cx = TileLeft + slot * TileStep + TileWidth / 2f;
            float y = TileTop + SelectedDrop + 47f;
            float alpha = 0.45f + 0.15f * MathF.Sin(clock * MathF.PI * 2f);
            MenuSkin.Whole(PlateArrow, new Rectangle(cx - 85f, y, 56f, 72f), MenuSkin.White(alpha));
            MenuSkin.Part(PlateArrow, new Rectangle(0, 0, -56f, 72f), new Rectangle(cx + 29f, y, 56f, 72f), MenuSkin.White(alpha));
        }

        // ===== パネル =====

        public static void DrawPanel(DanCourse course)
        {
            MenuSkin.Whole(Panel, new Rectangle(PanelAt.X, PanelAt.Y, 1522f, 858f), Color.White);
            // 左右の模様は段位の色（初段なら青緑）
            int cell = ColorCell(course.DanIndex);
            Rectangle side = new(cell * 40f, 0, 40f, 806f);
            MenuSkin.Part(PanelSide, side, new Rectangle(SideLeftX, SideY, 40f, 806f), Color.White);
            MenuSkin.Part(PanelSide, new Rectangle(side.X, 0, -40f, 806f), new Rectangle(SideRightX, SideY, 40f, 806f), Color.White);

            DrawSongs(course);
            DrawConditions(course);
        }

        // 課題曲の帯：玉（1st/2nd/3rd）と白い札は画像に入っている。札に難易度のマークと「★レベル」、右に曲名
        //   本家：マークは (529, 245)、★は (567, 309) がまんなかの黒い字、曲名は x635 から・まんなか y283 の白い字に黒い縁
        private static void DrawSongs(DanCourse course)
        {
            for (int i = 0; i < course.Songs.Count && i < 3; i++)
            {
                DanSong song = course.Songs[i];
                float dy = i * SongStep;
                MenuSkin.Part(SongBar, new Rectangle(0, i * SongStep, 1380f, SongStep),
                    new Rectangle(SongBarAt.X, SongBarAt.Y + dy, 1380f, SongStep), Color.White);

                int symbol = Math.Clamp((int)song.Course, 0, 4);
                MenuSkin.Part(CourseSymbol, new Rectangle(symbol * 78f, 0, 78f, 52f), new Rectangle(529f, 245f + dy, 78f, 52f), Color.White);
                Outlined($"★{song.Level}", new Rectangle(527f, 294f + dy, 80f, 30f), 28f, Ink, Color.White);

                string name = song.IsHidden ? "???" : song.Title.Length > 0 ? song.Title : song.RelativePath;
                float size = 44f;
                float maxWidth = 1760f - 635f;
                float width = Measure(name, size).X;
                if (width > maxWidth && width > 0) size *= maxWidth / width;
                OutlinedAt(name, 640f, 283f + dy - size * 0.62f, size, Color.White, Ink);
            }
        }

        // 合格条件：左に魂ゲージの箱、右に条件の箱を上から132px ずつ
        //   本家の段位選択では金の数は出していない（赤のボーダーだけ）
        private static void DrawConditions(DanCourse course)
        {
            // 魂ゲージ
            MenuSkin.Whole(GaugeBar, new Rectangle(GaugeBoxAt.X, GaugeBoxAt.Y, 333f, 184f), Color.White);
            Centered("魂ゲージ", new Rectangle(GaugeBoxAt.X + 30f, GaugeBoxAt.Y + 8f, 266f, 36f), 24f, Ink);
            MenuSkin.Part(ConditionContent, new Rectangle(0, 0, 333f, 50f), new Rectangle(439f, 667f, 333f, 50f), Color.White);
            MenuSkin.Part(ConditionContent, new Rectangle(0, 200f, 333f, 50f), new Rectangle(423f, 721f, 333f, 50f), Color.White);
            if (course.Gauge.Red > 0)
                NeedRight($"{course.Gauge.Red} %以上", 735f, 693f, 40f);

            for (int i = 0; i < course.Exams.Count && i < 3; i++)
            {
                DanExam exam = course.Exams[i];
                float dy = i * CondStep;
                MenuSkin.Whole(ConditionBar, new Rectangle(CondBarX, CondBarY + dy, 1033f, 150f), Color.White);
                Centered(exam.Label, new Rectangle(CondBarX + 30f, CondBarY + dy + 9f, 282f, 36f), 24f, Ink);

                string range = exam.IsLess ? "未満" : "以上";
                if (!exam.PerSong)
                {
                    MenuSkin.Part(ConditionContent, new Rectangle(0, 0, 333f, 50f), new Rectangle(ContentX, ContentY + dy, 333f, 50f), Color.White);
                    MenuSkin.Part(ConditionContent, new Rectangle(0, 200f, 333f, 50f), new Rectangle(ContentBarX, ContentBarY + dy, 333f, 50f), Color.White);
                    NeedRight($"{exam.For(0).Red} {range}", 1085f, 679f + dy, 36f);
                    continue;
                }

                // 曲ごと条件：1st/2nd/3rd の列を331px ずつ
                int count = Math.Min(exam.Thresholds.Length, 3);
                for (int s = 0; s < count; s++)
                {
                    float dx = s * ColumnStep;
                    MenuSkin.Part(ConditionContent, new Rectangle(0, 50f * (1 + s), 333f, 50f),
                        new Rectangle(ContentX + dx, ContentY + dy, 333f, 50f), Color.White);
                    MenuSkin.Part(ConditionContent, new Rectangle(0, 200f, 333f, 50f),
                        new Rectangle(ContentBarX + dx, ContentBarY + dy, 319f, 50f), Color.White);
                    NeedRight($"{exam.Thresholds[s].Red} {range}", 1085f + dx, 679f + dy, 36f);
                }
            }
        }

        // 「814 以上」：右はしをそろえた、黒い字に白い縁
        private static void NeedRight(string text, float right, float centerY, float size)
        {
            float w = Measure(text, size).X;
            OutlinedAt(text, right - w, centerY - size * 0.62f, size, Ink, Color.White);
        }

        // ===== 確認ポップアップ =====
        //   本家（動画 45.9〜46.8秒）：0.2秒で暗く → 青い箱が 0.75倍から0.13秒で大きくなって出る → 0.2秒あとにボタンがじわっと出る
        public const float ConfirmBoxIn = 0.13f, ConfirmButtonsAt = 0.2f, ConfirmButtonsFade = 0.13f;

        public static bool ConfirmAvailable => MenuSkin.Has(ConfirmBase, ConfirmButtons);

        /// <summary>choice：0＝スパナ（演奏オプション）・1＝挑戦する・2＝挑戦しない</summary>
        public static void DrawConfirm(float age, int choice, float clock)
        {
            float dim = Math.Clamp(age / 0.2f, 0f, 1f);
            Raylib.DrawRectangle(0, 0, 1920, 1080, new Color(0, 0, 0, (int)(120 * dim)));

            float t = Math.Clamp(age / ConfirmBoxIn, 0f, 1f);
            float scale = 0.75f + 0.25f * EaseOut(t);
            Rectangle full = new(ConfirmAt.X, ConfirmAt.Y, 1368f, 488f);
            Vector2 center = new(full.X + full.Width / 2f, full.Y + full.Height / 2f);
            Rectangle box = new(center.X - full.Width * scale / 2f, center.Y - full.Height * scale / 2f, full.Width * scale, full.Height * scale);
            MenuSkin.Whole(ConfirmBase, box, MenuSkin.White(t));

            float b = Math.Clamp((age - ConfirmButtonsAt) / ConfirmButtonsFade, 0f, 1f);
            if (b <= 0f) return;
            MenuSkin.Whole(ConfirmButtons, full, MenuSkin.White(b));

            // カーソルの枠：黄色い枠＋光（光はゆっくり点滅）。スパナは小さい枠
            bool small = choice == 0;
            Vector2 at = ButtonCenters[Math.Clamp(choice, 0, 2)];
            int frameCell = small ? 4 : 0, glowCell = small ? 5 : 1;
            Rectangle dest = new(at.X - 175f, at.Y - 75f, 350f, 150f);
            float glow = 0.5f + 0.5f * MathF.Sin(clock * MathF.PI * 2f);
            MenuSkin.Part(ConfirmCursor, new Rectangle(0, glowCell * 150f, 350f, 150f), dest, MenuSkin.White(b * glow));
            MenuSkin.Part(ConfirmCursor, new Rectangle(0, frameCell * 150f, 350f, 150f), dest, MenuSkin.White(b));
        }

        /// <summary>クリックされたボタン（無ければ -1）</summary>
        public static int ButtonAt(Vector2 mouse)
        {
            for (int i = 0; i < ButtonCenters.Length; i++)
            {
                float halfWidth = i == 0 ? 50f : 151f;
                if (Math.Abs(mouse.X - ButtonCenters[i].X) <= halfWidth && Math.Abs(mouse.Y - ButtonCenters[i].Y) <= 50f) return i;
            }
            return -1;
        }
    }
}
