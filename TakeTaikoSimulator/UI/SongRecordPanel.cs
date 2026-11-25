using System.Globalization;
using System.Numerics;
using Raylib_cs;
using Layout = TakeTaikoSimulator.UI.LayoutAdjustments.Records;
using TakeTaikoSimulator.Charts;
using TakeTaikoSimulator.Configs;
using TakeTaikoSimulator.Graphics;

namespace TakeTaikoSimulator.UI;

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

    private static Texture2D? cleanBestPanel;
    private static int cleanBestGeneration = -1;

    // 焼きこまれた「自己ベスト」の字を、左右(x119 と x216)の色を横につないでぬりつぶした絵
    private static unsafe Texture2D? CleanBestPanel()
    {
        if (cleanBestGeneration == SkinManager.Generation) return cleanBestPanel;
        cleanBestGeneration = SkinManager.Generation;
        if (cleanBestPanel is Texture2D old) Raylib.UnloadTexture(old);
        cleanBestPanel = null;
        if (SkinManager.GetTexture(Folder + "BestScorePanel/Panel.png") is not Texture2D tex || tex.Width != 340 || tex.Height != 105) return null;
        Image img = Raylib.LoadImageFromTexture(tex);
        Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);
        byte* px = (byte*)img.Data;
        const int x0 = 119, x1 = 216;
        for (int y = 16; y <= 42; y++)
            for (int x = x0 + 1; x < x1; x++)
            {
                float t = (x - x0) / (float)(x1 - x0);
                for (int c = 0; c < 4; c++)
                    px[(y * 340 + x) * 4 + c] = (byte)(px[(y * 340 + x0) * 4 + c] * (1 - t) + px[(y * 340 + x1) * 4 + c] * t);
            }
        cleanBestPanel = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(cleanBestPanel.Value, TextureFilter.Bilinear);
        return cleanBestPanel;
    }

    public static void DrawSummary(RecordGroup group, float alpha, SongRecordSummary? records = null, int? profile = null, int side = 0)
    {
        using var placement = SidePlacement(side, Layout.SummaryOffset);
        SongRecordSummary totals = records ?? SongHistory.GetSummary(group, profile);
        int row = (int)group;
        Rectangle panel = new(16, 306, 340, 200);
        if (!MenuSkin.Cell(Folder + "ScorePanel.png", row, 1, 5, panel, MenuSkin.White(alpha)))
        {
            Background(panel, alpha);
            Label(row == 4 ? "おに・裏おに" : Chart.GetCourseName((CourseType)group), new(30, 320, 180, 34), 24, Color.White, alpha);
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
        Count(totals.Ranks[6], Layout.HighestCount, alpha);
        for (int i = 0; i < 3; i++)
        {
            Count(totals.Ranks[3 + i], Layout.UpperCounts[i], alpha);
            Count(totals.Ranks[i], Layout.LowerCounts[i], alpha);
            int crowns = i == 0 ? totals.Cleared : i == 1 ? totals.FullCombo : totals.DondaFullCombo;
            Count(crowns, Layout.CrownCounts[i], alpha);
        }
    }

    public static void DrawBest(SongEntry song, CourseType course, float alpha, int? profile = null, int side = 0)
    {
        using var placement = SidePlacement(side, Layout.BestOffset);
        var record = SongHistory.GetRecord(profile ?? SongHistory.ActiveProfile, song.Key, course);
        // 11/14 たけらい「自己ベストの数字ちょっと大きい」：録画（2026-11-13_09-37-49.mp4 76秒）に絵を重ねて測りなおした
        //   パネル Panel.png（340×105）は原寸で (16,572)。前は 332×90 につぶして描いていたので、数字が大きく見えていた
        //   難易度の印 CourseSymbol.png（54×40）は (55,611)、数字 Number.png（1文字 24×28）は原寸で、1けたのとき真ん中 (279.5, 632.5)
        Rectangle panel = new(16, 572, 340, 105);
        // 11/25 たけらい「自己ベストの文字の解像度が低い」：絵に焼きこまれた小さい文字(絵の中 x122〜213・y17〜41)を
        //   まわりの色でぬりつぶした絵を作って、その上にフォントで書き直す
        if (CleanBestPanel() is Texture2D clean)
        {
            Raylib.DrawTexturePro(clean, new Rectangle(0, 0, clean.Width, clean.Height), panel, Vector2.Zero, 0f, MenuSkin.White(alpha));
            using (MenuDraw.UseOutlineRatio(3f / 23f))
                MenuDraw.Outlined("自己ベスト", new Rectangle(panel.X + 112, panel.Y + 15, 112, 28), 23f,
                    MenuDraw.Fade(Color.White, alpha), MenuDraw.Fade(Color.Black, alpha));
        }
        else if (!MenuSkin.Whole(Folder + "BestScorePanel/Panel.png", panel, MenuSkin.White(alpha)))
        {
            Background(panel, alpha);
            Label("自己ベスト", new(38, 582, 296, 30), 24, Color.Gold, alpha);
        }
        int row = Math.Clamp((int)course, 0, 4);
        if (record.Rank > 0)
        {
            // 80×80の各セルには難易度の印も入っている。
            if (!MenuSkin.Cell(Folder + "ScoreRank_Symbol.png", row * 7 + record.Rank - 1, 7, 5,
                new(55, 607, 54, 54), MenuSkin.White(alpha)))
                RankFallback(record.Rank, new(49, 613, 60, 40), alpha);
        }
        else if (!MenuSkin.Cell(Folder + "BestScorePanel/CourseSymbol.png", row, 1, 5,
            new(55, 611, 54, 40), MenuSkin.White(alpha)))
            Label(Chart.GetCourseName(course), new(35, 615, 90, 35), 18, Color.White, alpha);

        string score = record.Score.ToString(CultureInfo.InvariantCulture);
        float width = Layout.BestDigitWidth + Math.Max(0, score.Length - 1) * Layout.BestDigitSpacing;
        float scale = Math.Min(1, Layout.BestMaxWidth / width);
        Number("BestScorePanel/Number.png", record.Score, Layout.BestNumberRight - width * scale / 2,
            Layout.BestNumberY, Layout.BestDigitWidth, Layout.BestDigitHeight, Layout.BestDigitSpacing, Layout.BestMaxWidth, alpha, native: true);
    }

    public static void DrawCard(string key, CourseType course, Rectangle card, float alpha, int? profile = null)
    {
        var record = SongHistory.GetRecord(profile ?? SongHistory.ActiveProfile, key, course);
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

    private static void Count(long value, Vector2 center, float alpha) =>
        Number("ScorePanel_Number.png", value, center.X, center.Y, Layout.CountWidth, Layout.CountHeight, Layout.CountSpacing, Layout.CountMaxWidth, alpha);

    private static Placement SidePlacement(int side, Vector2 offset) => new(side, offset);
    private readonly struct Placement : IDisposable
    {
        public Placement(int side, Vector2 offset)
        {
            Rlgl.PushMatrix();
            Rlgl.Translatef(offset.X + (side == 1 ? Layout.Player2OffsetX : 0), offset.Y, 0);
        }
        public void Dispose() => Rlgl.PopMatrix();
    }

    private sealed record DigitArt(float CellWidth, Rectangle Ink);
    private static readonly Dictionary<string, DigitArt> digitArt = new();
    private static int digitGeneration = -1;

    // 初回だけ透明な余白を調べる。描くたびにGPUから画像を戻さない。
    private static unsafe DigitArt PrepareDigits(string path, Texture2D texture)
    {
        if (digitGeneration != SkinManager.Generation)
        { digitArt.Clear(); digitGeneration = SkinManager.Generation; }
        if (digitArt.TryGetValue(path, out var cached)) return cached;
        int cell = texture.Width / 10;
        int left = cell, top = texture.Height, right = -1, bottom = -1;
        Image image = Raylib.LoadImageFromTexture(texture);
        try
        {
            Color* colors = Raylib.LoadImageColors(image);
            try
            {
                if (colors != null)
                    for (int y = 0; y < image.Height; y++)
                        for (int x = 0; x < cell * 10; x++)
                            if (colors[y * image.Width + x].A > 16)
                            {
                                int localX = x % cell;
                                left = Math.Min(left, localX); right = Math.Max(right, localX);
                                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                            }
            }
            finally { if (colors != null) Raylib.UnloadImageColors(colors); }
        }
        finally { Raylib.UnloadImage(image); }
        Rectangle ink = right >= left ? new(left, top, right - left + 1, bottom - top + 1) : new(0, 0, Math.Max(1, cell), Math.Max(1, texture.Height));
        return digitArt[path] = new(cell, ink);
    }

    private static void Number(string image, long value, float x, float y, float cellWidth, float height,
        float advance, float maxWidth, float alpha, bool native = false)
    {
        string digits = value.ToString(CultureInfo.InvariantCulture);
        float fit = Math.Min(1, maxWidth / (cellWidth + Math.Max(0, digits.Length - 1) * advance));
        string path = Folder + image;
        if (SkinManager.GetTexture(path) is Texture2D texture && texture.Width >= 10)
        {
            DigitArt art = PrepareDigits(path, texture);
            // native：絵の1文字（ます目全体）を cellWidth×height に合わせる（原寸なら 1倍）。
            //   ふだんは色のついた部分をます目いっぱいに広げるので、絵より少し大きくなる
            float scale = (native ? Math.Min(cellWidth / art.CellWidth, height / (float)texture.Height)
                : Math.Min(cellWidth / art.Ink.Width, height / art.Ink.Height)) * fit;
            float firstX = x - advance * fit * (digits.Length - 1) / 2;
            for (int i = 0; i < digits.Length; i++)
            {
                Rectangle source = new((digits[i] - '0') * art.CellWidth + art.Ink.X, art.Ink.Y, art.Ink.Width, art.Ink.Height);
                Rectangle dest = new(firstX + i * advance * fit - art.Ink.Width * scale / 2, y - art.Ink.Height * scale / 2,
                    art.Ink.Width * scale, art.Ink.Height * scale);
                Raylib.DrawTexturePro(texture, source, dest, Vector2.Zero, 0, MenuSkin.White(alpha));
            }
        }
        else
            Label(digits, new(x - maxWidth / 2, y - height / 2, maxWidth, height), height * fit, Color.White, alpha);
    }

    private static void Background(Rectangle rect, float alpha)
    {
        SmoothShapes.Rounded(rect, 0.08f, 8, MenuDraw.Fade(new Color(18, 25, 40, 235), alpha));
        SmoothShapes.RoundedLines(rect, 0.08f, 8, 2, MenuDraw.Fade(new Color(92, 112, 140, 255), alpha));
    }

    private static void Label(string text, Rectangle rect, float size, Color color, float alpha) =>
        MenuDraw.Outlined(text, rect, size, MenuDraw.Fade(color, alpha), MenuDraw.Fade(Color.Black, alpha));

    private static void RankFallback(int rank, Rectangle rect, float alpha)
    {
        // 絵が無いスキン（シンプル）は、演奏・リザルトのスコアランクと同じ E〜SS の字と色で出す
        //   11/11 たけらい「スコアランク英語にして。なんでここだけ」（前は 粋・雅・極 の字だった。tools/skin-gen/play.py の RANKS と同じ）
        string[] letters = { "E", "D", "C", "B", "A", "S", "SS" };
        Color[] colors = { new(150, 150, 160, 255), new(120, 170, 210, 255), new(110, 200, 140, 255), new(240, 200, 70, 255),
            new(255, 150, 60, 255), new(255, 110, 150, 255), new(190, 130, 255, 255) };
        int i = Math.Clamp(rank - 1, 0, 6);
        Label(letters[i], rect, rect.Height * (i == 6 ? 0.62f : 0.8f), colors[i], alpha);
    }

    private static void CrownFallback(SongClearType clear, Rectangle rect, float alpha)
    {
        // 難易度パネルの王冠の絵があれば、それを使う（Simple スキンも 11/26 から絵を持っている）
        float w = rect.Height * 50f / 40f;
        if (MenuSkin.Cell(Folder + "Difficulty_Select/Crown.png", (int)clear, 4, 1,
                new Rectangle(rect.X + (rect.Width - w) / 2f, rect.Y, w, rect.Height), MenuSkin.White(alpha))) return;
        Vector2 P(float x, float y) => new(rect.X + rect.Width * x, rect.Y + rect.Height * y);
        Vector2[] points = { P(0, 0), P(.27f, .4f), P(.5f, 0), P(.73f, .4f), P(1, 0), P(.86f, 1), P(.14f, 1) };
        Color fill = clear == SongClearType.Cleared ? Color.LightGray : clear == SongClearType.FullCombo ? Color.Gold : Color.SkyBlue;
        for (int i = 0; i < points.Length; i++)
        {
            SmoothShapes.Triangle(P(.5f, .7f), points[(i + 1) % points.Length], points[i], MenuDraw.Fade(fill, alpha));
            SmoothShapes.Line(points[i], points[(i + 1) % points.Length], 2, MenuDraw.Fade(Color.Black, alpha));
        }
        if (clear == SongClearType.AllPerfect)
        {
            SmoothShapes.Line(P(.2f, .7f), P(.8f, .7f), 4, MenuDraw.Fade(Color.Pink, alpha));
            SmoothShapes.Line(P(.2f, .85f), P(.8f, .85f), 4, MenuDraw.Fade(Color.Yellow, alpha));
        }
    }
}

