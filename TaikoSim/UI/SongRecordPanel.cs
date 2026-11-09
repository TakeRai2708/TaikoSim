using System.Globalization;
using System.Numerics;
using Raylib_cs;
using TaikoSim.Charts;
using TaikoSim.Graphics;

namespace TaikoSim.UI;

// 10/29のお手本：集計は左上、自己ベストはどんちゃんの上。画像は原寸で使う。
public static class SongRecordPanel
{
    private const string Folder = MenuSkin.SongFolder;
    public static readonly string[] Images =
    {
        "ScorePanel.png", "ScorePanel_Number.png", "ScoreRank_Symbol.png",
        "BestScorePanel/Panel.png", "BestScorePanel/CourseSymbol.png", "BestScorePanel/Number.png",
        "Difficulty_Select/Crown.png", "Difficulty_Select/ScoreRank.png",
    };

    public static void DrawSummary(CourseType course, float alpha)
    {
        SongRecordSummary totals = SongHistory.GetSummary(course);
        int row = (int)course >= 3 ? 4 : (int)course;
        Rectangle panel = new(16, 306, 340, 200);
        if (!MenuSkin.Cell(Folder + "ScorePanel.png", row, 1, 5, panel, MenuSkin.White(alpha)))
        {
            Background(panel, alpha);
            Label(row == 4 ? "おに・裏おに" : Chart.GetCourseName(course), new(30, 320, 180, 34), 24, Color.White, alpha);
            int[] rankOrder = { 7, 4, 5, 6, 1, 2, 3 };
            for (int i = 0; i < rankOrder.Length; i++)
            {
                int col = i == 0 ? 2 : (i - 1) % 3;
                int line = i == 0 ? 0 : (i - 1) / 3 + 1;
                RankFallback(rankOrder[i], new(42 + col * 94, 330 + line * 40, 40, 36), alpha);
            }
            for (int i = 0; i < 3; i++) CrownFallback((SongClearType)(i + 1), new(42 + i * 94, 452, 36, 30), alpha);
        }
        // パネルの絵に印が入っているので、ここでは数字だけを重ねる。
        Number("ScorePanel_Number.png", totals.Ranks[6], 300, 348, 18, 22, 16, 70, alpha);
        for (int i = 0; i < 3; i++)
        {
            float x = 110 + i * 94;
            Number("ScorePanel_Number.png", totals.Ranks[3 + i], x, 388, 18, 22, 16, 70, alpha);
            Number("ScorePanel_Number.png", totals.Ranks[i], x, 428, 18, 22, 16, 70, alpha);
            int crowns = i == 0 ? totals.Cleared : i == 1 ? totals.FullCombo : totals.DondaFullCombo;
            Number("ScorePanel_Number.png", crowns, x, 470, 18, 22, 16, 70, alpha);
        }
    }

    public static void DrawBest(SongEntry song, CourseType course, float alpha)
    {
        var record = SongHistory.GetRecord(song.Key, course);
        Rectangle panel = new(20, 575, 332, 90);
        if (!MenuSkin.Whole(Folder + "BestScorePanel/Panel.png", panel, MenuSkin.White(alpha)))
        {
            Background(panel, alpha);
            Label("自己ベスト", new(38, 582, 296, 30), 24, Color.Gold, alpha);
        }
        int row = Math.Clamp((int)course, 0, 4);
        if (record.Rank > 0)
        {
            // 80×80の各セルには難易度の印も入っている。
            if (!MenuSkin.Cell(Folder + "ScoreRank_Symbol.png", row * 7 + record.Rank - 1, 7, 5,
                new(54, 610, 54, 54), MenuSkin.White(alpha)))
                RankFallback(record.Rank, new(48, 616, 60, 40), alpha);
        }
        else if (!MenuSkin.Cell(Folder + "BestScorePanel/CourseSymbol.png", row, 1, 5,
            new(54, 614, 54, 40), MenuSkin.White(alpha)))
            Label(Chart.GetCourseName(course), new(34, 618, 90, 35), 18, Color.White, alpha);

        string score = record.Score.ToString(CultureInfo.InvariantCulture);
        float width = 24 + Math.Max(0, score.Length - 1) * 22;
        float scale = Math.Min(1, 190 / width);
        Number("BestScorePanel/Number.png", record.Score, 308 - width * scale / 2,
            638, 24, 28, 22, 190, alpha);
    }

    public static void DrawCard(string key, CourseType course, Rectangle card, float alpha)
    {
        var record = SongHistory.GetRecord(key, course);
        float sx = card.Width / 196, sy = card.Height / 354;
        Rectangle At(float x, float y, float w, float h) => new(card.X + x * sx, card.Y + y * sy, w * sx, h * sy);
        if (record.Clear != SongClearType.None)
        {
            Rectangle crown = At(15, 15, 50, 40);
            if (!MenuSkin.Cell(Folder + "Difficulty_Select/Crown.png", (int)record.Clear, 4, 1, crown, MenuSkin.White(alpha)))
                CrownFallback(record.Clear, At(23, 24, 32, 26), alpha);
        }
        if (record.Rank > 0)
        {
            Rectangle rank = At(131, 15, 50, 40);
            if (!MenuSkin.Cell(Folder + "Difficulty_Select/ScoreRank.png", record.Rank - 1, 7, 1, rank, MenuSkin.White(alpha)))
                RankFallback(record.Rank, rank, alpha);
        }
    }

    private static void Number(string image, long value, float x, float y, float cellWidth, float height,
        float advance, float maxWidth, float alpha)
    {
        string digits = value.ToString(CultureInfo.InvariantCulture);
        float scale = Math.Min(1, maxWidth / (cellWidth + Math.Max(0, digits.Length - 1) * advance));
        if (!MenuSkin.Digits(Folder + image, digits, x, y, advance, scale, MenuSkin.White(alpha)))
            Label(digits, new(x - maxWidth / 2, y - height / 2, maxWidth, height), height * scale, Color.White, alpha);
    }

    private static void Background(Rectangle rect, float alpha)
    {
        Raylib.DrawRectangleRounded(rect, 0.08f, 8, MenuDraw.Fade(new Color(18, 25, 40, 235), alpha));
        Raylib.DrawRectangleRoundedLinesEx(rect, 0.08f, 8, 2, MenuDraw.Fade(new Color(92, 112, 140, 255), alpha));
    }

    private static void Label(string text, Rectangle rect, float size, Color color, float alpha) =>
        MenuDraw.Outlined(text, rect, size, MenuDraw.Fade(color, alpha), MenuDraw.Fade(Color.Black, alpha));

    private static void RankFallback(int rank, Rectangle rect, float alpha)
    {
        Color[] colors = { Color.White, new(204, 140, 83, 255), Color.LightGray, Color.Gold, Color.Pink, Color.Purple, Color.Yellow };
        Label(rank == 7 ? "極" : rank >= 4 ? "雅" : "粋", rect, rect.Height * 0.8f, colors[Math.Clamp(rank - 1, 0, 6)], alpha);
    }

    private static void CrownFallback(SongClearType clear, Rectangle rect, float alpha)
    {
        Vector2 P(float x, float y) => new(rect.X + rect.Width * x, rect.Y + rect.Height * y);
        Vector2[] points = { P(0, 0), P(.27f, .4f), P(.5f, 0), P(.73f, .4f), P(1, 0), P(.86f, 1), P(.14f, 1) };
        Color fill = clear == SongClearType.Cleared ? Color.LightGray : clear == SongClearType.FullCombo ? Color.Gold : Color.SkyBlue;
        for (int i = 0; i < points.Length; i++)
        {
            Raylib.DrawTriangle(P(.5f, .7f), points[(i + 1) % points.Length], points[i], MenuDraw.Fade(fill, alpha));
            Raylib.DrawLineEx(points[i], points[(i + 1) % points.Length], 2, MenuDraw.Fade(Color.Black, alpha));
        }
        if (clear == SongClearType.AllPerfect)
        {
            Raylib.DrawLineEx(P(.2f, .7f), P(.8f, .7f), 4, MenuDraw.Fade(Color.Pink, alpha));
            Raylib.DrawLineEx(P(.2f, .85f), P(.8f, .85f), 4, MenuDraw.Fade(Color.Yellow, alpha));
        }
    }
}
